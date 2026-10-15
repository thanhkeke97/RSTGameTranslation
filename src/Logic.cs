using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using MessageBox = System.Windows.MessageBox;

namespace RSTGameTranslation
{
    public class Logic
    {
        private static Logic? _instance;
        private List<TextObject> _textObjects;
        private List<TextObject> _textObjectsOld;
        // Copy of the frame being OCR'd, for sampling the overlay background colour once its result is in
        // (the OCR call disposes the captured bitmap). Only kept while the auto background colour is on.
        private Bitmap? _ocrSourceFrame;
        private readonly object _ocrSourceFrameLock = new object();
        private Random _random;
        private Grid? _overlayContainer;
        private int _textIDCounter = 0;
        private DateTime _lastOcrRequestTime = DateTime.MinValue;
        private readonly TimeSpan _minOcrInterval = TimeSpan.FromSeconds(0.2);

        private readonly List<string> _audioBatch = new List<string>();
        private readonly object _audioBatchLock = new object();
        private bool _isProcessingAudioBatch = false;
        private bool _hasNewAudioSinceLastTranslation = false;

        private string _lastOcrHash = string.Empty;
        private string _lastTextContent = string.Empty;
        // Last OCR text logged as skipped, so a static screen is logged once instead of every frame
        private string _lastSkipLoggedText = string.Empty;

        // Track the current capture position
        private int _currentCaptureX;
        private int _currentCaptureY;
        private DateTime _lastChangeTime = DateTime.MinValue;
        // The last change was text typing out (typewriter dialogue) rather than new text appearing
        private bool _textIsGrowing;
        private const double TypewriterExtraWaitSeconds = 0.6;

        // Properties to expose to other classes
        public List<TextObject> TextObjects => _textObjects;
        public List<TextObject> TextObjectsOld => _textObjectsOld;

        // Events
        public event EventHandler<TextObject>? TextObjectAdded;

        // Event when translation is completed
        public event EventHandler<TranslationEventArgs>? TranslationCompleted;

        bool _waitingForTranslationToFinish = false;

        public bool GetWaitingForTranslationToFinish()
        {
            return _waitingForTranslationToFinish;
        }
        public int GetNextTextID()
        {
            return _textIDCounter++;
        }
        public void SetWaitingForTranslationToFinish(bool value)
        {
            _waitingForTranslationToFinish = value;
        }

        // Singleton pattern
        public static Logic Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new Logic();
                }
                return _instance;
            }
        }
        Stopwatch _translationStopwatch = new Stopwatch();
        Stopwatch _ocrProcessingStopwatch = new Stopwatch();

        // Constructor
        private Logic()
        {
            // Private constructor to enforce singleton pattern
            _textObjects = new List<TextObject>();
            _textObjectsOld = new List<TextObject>();
            _random = new Random();
        }

        // Called with each frame sent to OCR. Takes ownership of the copy.
        public void SetOcrSourceFrame(Bitmap? frame)
        {
            lock (_ocrSourceFrameLock)
            {
                _ocrSourceFrame?.Dispose();
                _ocrSourceFrame = frame;
            }
        }

        // Set reference to the overlay container
        public void SetOverlayContainer(Grid overlayContainer)
        {
            _overlayContainer = overlayContainer;
        }

        // Get the list of current text objects
        public IReadOnlyList<TextObject> GetTextObjects()
        {
            return _textObjects.AsReadOnly();
        }
        public IReadOnlyList<TextObject> GetTextObjectsOld()
        {
            return _textObjectsOld.AsReadOnly();
        }

        // Called when the application starts
        public async void Init()
        {
            try
            {
                // Initialize resources, settings, etc.
                Console.WriteLine("Logic initialized");

                // Load configuration
                string geminiApiKey = ConfigManager.Instance.GetGeminiApiKey();
                Console.WriteLine($"Loaded Gemini API key: {(string.IsNullOrEmpty(geminiApiKey) ? "Not set" : "Set")}");

                // Load LLM prompt
                string llmPrompt = ConfigManager.Instance.GetLlmPrompt();
                Console.WriteLine($"Loaded LLM prompt: {(string.IsNullOrEmpty(llmPrompt) ? "Not set" : $"{llmPrompt.Length} chars")}");

                // Load force cursor visible setting
                // Force cursor visibility is now handled by MouseManager

                Console.WriteLine($"Using {MainWindow.Instance.GetSelectedOcrMethod()} (built-in)");

                // Update status message in the UI
                MainWindow.Instance.SetStatus($"Using {MainWindow.Instance.GetSelectedOcrMethod()} (built-in)");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_ErrorInitialization"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        void OnFinishedThings(bool bResetTranslationStatus)
        {
            SetWaitingForTranslationToFinish(false);
            MonitorWindow.Instance.RefreshOverlays();

            // Hide translation status
            if (bResetTranslationStatus)
            {
                MonitorWindow.Instance.HideTranslationStatus();
            }
        }

        // The last OCR result is fully handled: shown (or being translated) and not waiting to settle,
        // so OCR of an identical frame would change nothing
        public bool IsOcrResultSettled => _lastChangeTime == DateTime.MinValue && _lastOcrHash.Length > 0;

        public void ResetHash()
        {
            _lastOcrHash = "";
            _lastTextContent = string.Empty;
            _lastChangeTime = DateTime.Now;
        }

        /// <summary>
        /// Retry the current translation bypassing similarity checks.
        /// Clears the last OCR hash/text and triggers translation for the current text objects.
        /// </summary>
        public void RetryCurrentTranslation()
        {
            // Ensure this runs on the UI thread
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (_textObjects == null || _textObjects.Count == 0)
                    {
                        Console.WriteLine("No text objects available to retry translation.");
                        return;
                    }

                    if (GetWaitingForTranslationToFinish())
                    {
                        Console.WriteLine("Translation already in progress; retry request ignored.");
                        return;
                    }

                    Console.WriteLine("Retrying current translation (bypassing similarity checks and the translation cache).");
                    _bypassTranslationCacheOnce = true;

                    // Clear the last-hash / last-text so similarity gating is bypassed
                    _lastOcrHash = string.Empty;
                    _lastTextContent = string.Empty;
                    _lastChangeTime = DateTime.MinValue;

                    // Start translation asynchronously
                    _ = TranslateTextObjectsAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in RetryCurrentTranslation: {ex.Message}");
                }
            });
        }


        // Process Google Translate JSON response
        private void ProcessGoogleTranslateJson(JsonElement rootElement, bool addToChatBox = true)
        {
            try
            {
                Console.WriteLine("Processing Google Translate response");

                if (rootElement.TryGetProperty("translations", out JsonElement translationsArray) &&
                    translationsArray.ValueKind == JsonValueKind.Array)
                {
                    // Get current target language
                    string targetLanguage = ConfigManager.Instance.GetTargetLanguage().ToLower();

                    // Define RTL (Right-to-Left) languages
                    HashSet<string> rtlLanguages = new HashSet<string> {
                        "ar", "arabic", "fa", "farsi", "persian", "he", "hebrew", "ur", "urdu"
                    };

                    // Check if target language is RTL
                    bool isRtlLanguage = rtlLanguages.Contains(targetLanguage);

                    // Process each translation
                    for (int i = 0; i < translationsArray.GetArrayLength(); i++)
                    {
                        var translation = translationsArray[i];

                        if (translation.TryGetProperty("id", out JsonElement idElement) &&
                            translation.TryGetProperty("translated_text", out JsonElement translatedTextElement))
                        {
                            string id = idElement.GetString() ?? "";
                            string translatedText = translatedTextElement.GetString() ?? "";

                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(translatedText))
                            {
                                // Check if this is our combined text block with ID=999
                                if (id == "999")
                                {
                                    // Split the translated text using the separator
                                    string[] translatedParts = translatedText.Split("##|||##");

                                    // Assign each part to the corresponding text object
                                    for (int j = 0; j < Math.Min(translatedParts.Length, _textObjects.Count); j++)
                                    {
                                        // Clean up the translated text - remove any remaining (possibly mangled) ##|||## separators.
                                        // Only fragments around "|||" are removed, so real text such as "#1" is left intact.
                                        string cleanTranslatedText = System.Text.RegularExpressions.Regex.Replace(
                                            translatedParts[j],
                                            @"#{0,2}\s*\|{3}\s*#{0,2}",
                                            ""
                                        );

                                        // Apply RTL specific handling if needed
                                        if (isRtlLanguage)
                                        {
                                            // Set flow direction for RTL languages
                                            _textObjects[j].FlowDirection = FlowDirection.RightToLeft;

                                            // Optionally add Unicode RLM (Right-to-Left Mark) if needed
                                            if (!cleanTranslatedText.StartsWith('\u200F'))
                                            {
                                                cleanTranslatedText = "\u200F" + cleanTranslatedText;
                                            }
                                        }
                                        else
                                        {
                                            // Ensure LTR for non-RTL languages
                                            _textObjects[j].FlowDirection = FlowDirection.LeftToRight;
                                        }

                                        // Update the text object with the cleaned translated text
                                        _textObjects[j].TextTranslated = cleanTranslatedText;
                                        _textObjects[j].UpdateUIElement();
                                        Console.WriteLine($"Updated text object at index {j} with translation from Google Translate");
                                    }

                                    // We've processed the combined text block, so we can break out of the loop
                                    break;
                                }
                                else
                                {
                                    // Handle the old way for backward compatibility
                                    // Find the matching text object by ID
                                    var matchingTextObj = _textObjects.FirstOrDefault(t => t.ID == id);
                                    if (matchingTextObj != null)
                                    {
                                        // Apply RTL specific handling if needed
                                        if (isRtlLanguage)
                                        {
                                            // Set flow direction for RTL languages
                                            matchingTextObj.FlowDirection = FlowDirection.RightToLeft;

                                            // Optionally add Unicode RLM (Right-to-Left Mark) if needed
                                            if (!translatedText.StartsWith('\u200F'))
                                            {
                                                translatedText = "\u200F" + translatedText;
                                            }
                                        }
                                        else
                                        {
                                            // Ensure LTR for non-RTL languages
                                            matchingTextObj.FlowDirection = FlowDirection.LeftToRight;
                                        }

                                        // Update the corresponding text object
                                        matchingTextObj.TextTranslated = translatedText;
                                        matchingTextObj.UpdateUIElement();
                                        Console.WriteLine($"Updated text object {id} with translation from Google Translate");
                                    }
                                    else if (id.StartsWith("text_"))
                                    {
                                        // Try to extract index from ID (text_X format)
                                        string indexStr = id.Substring(5); // Remove "text_" prefix
                                        if (int.TryParse(indexStr, out int index) && index >= 0 && index < _textObjects.Count)
                                        {
                                            // Apply RTL specific handling if needed
                                            if (isRtlLanguage)
                                            {
                                                // Set flow direction for RTL languages
                                                _textObjects[index].FlowDirection = FlowDirection.RightToLeft;

                                                // Optionally add Unicode RLM (Right-to-Left Mark) if needed
                                                if (!translatedText.StartsWith('\u200F'))
                                                {
                                                    translatedText = "\u200F" + translatedText;
                                                }
                                            }
                                            else
                                            {
                                                // Ensure LTR for non-RTL languages
                                                _textObjects[index].FlowDirection = FlowDirection.LeftToRight;
                                            }

                                            // Update by index if ID matches format
                                            _textObjects[index].TextTranslated = translatedText;
                                            _textObjects[index].UpdateUIElement();
                                            Console.WriteLine($"Updated text object at index {index} with translation from Google Translate");
                                        }
                                        else
                                        {
                                            Console.WriteLine($"Could not find text object with ID {id}");
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (addToChatBox)
                    {
                        AddTranslatedTextObjectsToChatBox();
                    }
                }
                else
                {
                    Console.WriteLine("No translations array found in Google Translate response");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing Google Translate JSON: {ex.Message}");
            }
        }

        private string ExtractTextContent(JsonElement resultsElement)
        {
            if (resultsElement.ValueKind != JsonValueKind.Array)
                return string.Empty;

            StringBuilder textBuilder = new StringBuilder();

            foreach (JsonElement element in resultsElement.EnumerateArray())
            {
                if (element.TryGetProperty("text", out JsonElement textElement))
                {
                    string text = textElement.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        textBuilder.Append(text);
                        textBuilder.Append(' ');
                    }
                }
            }

            return textBuilder.ToString().Trim();
        }

        //! Process the OCR text data, this is before it's been translated
        public void ProcessReceivedTextJsonData(string data)
        {
            _ocrProcessingStopwatch.Restart();
            MainWindow.Instance.SetOCRCheckIsWanted(true);

            if (GetWaitingForTranslationToFinish())
            {
                Console.WriteLine("Skipping OCR results - waiting for translation to finish");
                return;
            }

            try
            {
                // Check if the data is JSON
                if (data.StartsWith("{") && data.EndsWith("}"))
                {
                    // Try to parse as JSON
                    try
                    {
                        // Parse JSON with options that preserve Unicode characters
                        var options = new JsonDocumentOptions
                        {
                            AllowTrailingCommas = true,
                            CommentHandling = JsonCommentHandling.Skip
                        };

                        using JsonDocument doc = JsonDocument.Parse(data, options);
                        JsonElement root = doc.RootElement;

                        // Check if it's an OCR response
                        if (root.TryGetProperty("status", out JsonElement statusElement))
                        {
                            string status = statusElement.GetString() ?? "unknown";

                            if (status == "success" && root.TryGetProperty("results", out JsonElement resultsElement))
                            {
                                // Pre-filter low-confidence characters before block detection
                                JsonElement filteredResults = FilterLowConfidenceCharacters(resultsElement);

                                // Process character-level OCR data using CharacterBlockDetectionManager
                                // Use the filtered results for consistency
                                JsonElement modifiedResults = CharacterBlockDetectionManager.Instance.ProcessCharacterResults(filteredResults);

                                // Filter out text objects that should be ignored based on ignore phrases
                                modifiedResults = FilterIgnoredPhrases(modifiedResults);

                                // Generate content hash AFTER block detection and filtering
                                string contentHash = GenerateContentHash(modifiedResults);
                                string textContent = ExtractTextContent(modifiedResults);
                                OnboardingTips.OnOcrResult(!string.IsNullOrWhiteSpace(textContent));

                                // Handle settle time if enabled
                                double settleTime = ConfigManager.Instance.GetBlockDetectionSettleTime();
                                double similarThreshold = ConfigManager.Instance.GetTextSimilarThreshold();
                                bool hashMatch = contentHash == _lastOcrHash;
                                // Typewriter text: a few more characters per frame still scores as similar, which
                                // settled (and translated) half a sentence, then skipped the rest as already shown
                                bool growing = !hashMatch && TextGrowth.IsGrowth(_lastTextContent, textContent);
                                double similarity = 0.0;
                                if (settleTime > 0)
                                {
                                    if (hashMatch || (!growing && IsTextSimilar(textContent, _lastTextContent, similarThreshold, out similarity)))
                                    {
                                        if (_lastChangeTime == DateTime.MinValue)
                                        {
                                            LogSimilaritySkip(hashMatch, similarity, similarThreshold, textContent);
                                            OnFinishedThings(true);
                                            return; // Already rendered it, just ignore until it changes again
                                        }
                                        else
                                        {
                                            // Text that was typing out and stopped mid-sentence (a pause on a comma or
                                            // an ellipsis) gets a little longer before it counts as settled
                                            double requiredTime = _textIsGrowing && !TextGrowth.EndsSentence(textContent)
                                                ? settleTime + TypewriterExtraWaitSeconds
                                                : settleTime;
                                            if ((DateTime.Now - _lastChangeTime).TotalSeconds < requiredTime)
                                            {
                                                OnFinishedThings(false);
                                                return;
                                            }
                                            else
                                            {
                                                // Settle time reached
                                                _lastChangeTime = DateTime.MinValue;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        _textIsGrowing = growing;
                                        _lastChangeTime = DateTime.Now;
                                        _lastOcrHash = contentHash;
                                        _lastTextContent = textContent;
                                        _lastSkipLoggedText = string.Empty;
                                        // An empty frame (text disappeared) has nothing to time or translate
                                        if (!string.IsNullOrWhiteSpace(textContent))
                                        {
                                            OcrTiming.NewTextDetected(growing
                                                ? $"still typing out, waiting for it to settle: '{OcrTiming.Preview(textContent)}'"
                                                : $"waiting {settleTime:F2}s for it to settle: '{OcrTiming.Preview(textContent)}'");
                                            // A prefetch of text that is still typing out would be thrown away; the
                                            // frame that completes the sentence is still prefetched
                                            if (!growing || TextGrowth.EndsSentence(textContent))
                                                StartTranslationPrefetch(modifiedResults);
                                        }

                                        //only run if translation is still active
                                        if (MainWindow.Instance.GetIsStarted())
                                        {

                                            MonitorWindow.Instance.ShowTranslationStatus(true);
                                            ChatBoxWindow.Instance?.ShowTranslationStatus(true);
                                        }

                                        OnFinishedThings(false);
                                        return; // Sure, it's new, but we probably aren't ready to show it yet
                                    }
                                }
                                else if (!growing && IsTextSimilar(textContent, _lastTextContent, similarThreshold, out similarity))
                                {
                                    LogSimilaritySkip(false, similarity, similarThreshold, textContent);
                                    OnFinishedThings(true);
                                    return;
                                }
                                // Looks like new stuff
                                if (string.IsNullOrWhiteSpace(textContent))
                                {
                                    // Text disappeared: nothing to time
                                }
                                else if (settleTime > 0)
                                    OcrTiming.Log("2. Text settled");
                                else
                                    OcrTiming.NewTextDetected($"'{OcrTiming.Preview(textContent)}'");
                                _lastOcrHash = contentHash;
                                _lastTextContent = textContent;
                                _lastSkipLoggedText = string.Empty;
                                double scale = BlockDetectionManager.Instance.GetBlockDetectionScale();
                                Console.WriteLine($"Character-level processing (scale={scale:F2}): {resultsElement.GetArrayLength()} characters → {modifiedResults.GetArrayLength()} blocks");

                                // Create a new JsonDocument with the modified results
                                using (var stream = new MemoryStream())
                                {
                                    using (var writer = new Utf8JsonWriter(stream))
                                    {
                                        writer.WriteStartObject();

                                        // Copy over all existing properties except 'results'
                                        foreach (var property in root.EnumerateObject())
                                        {
                                            if (property.Name != "results")
                                            {
                                                property.WriteTo(writer);
                                            }
                                        }

                                        // Add our modified results
                                        writer.WritePropertyName("results");
                                        modifiedResults.WriteTo(writer);

                                        // Add marker to indicate this is character-level data
                                        writer.WriteBoolean("char_level", true);

                                        writer.WriteEndObject();
                                    }

                                    stream.Position = 0;
                                    using (JsonDocument newDoc = JsonDocument.Parse(stream))
                                    {
                                        DisplayOcrResults(newDoc.RootElement);
                                    }

                                    _ocrProcessingStopwatch.Stop();
                                    Console.WriteLine($"OCR JSON processing took {_ocrProcessingStopwatch.ElapsedMilliseconds} ms");

                                }

                                // Add the detected text to the ChatBox
                                if (_textObjects.Count > 0)
                                {
                                    // Build a string with all the detected text
                                    StringBuilder detectedText = new StringBuilder();
                                    foreach (var textObject in _textObjects)
                                    {
                                        detectedText.AppendLine(textObject.Text);
                                    }

                                    // Add to ChatBox with empty translation if translate is disabled
                                    string combinedText = detectedText.ToString().Trim();
                                    if (!string.IsNullOrEmpty(combinedText))
                                    {
                                        if (MainWindow.Instance.GetTranslateEnabled())
                                        {
                                            // If translation is enabled, translate the text
                                            if (!GetWaitingForTranslationToFinish())
                                            {
                                                //Console.WriteLine($"Translating text: {combinedText}");
                                                // Translate the text objects
                                                _lastChangeTime = DateTime.MinValue;
                                                _ = TranslateTextObjectsAsync();
                                                return;
                                            }
                                        }
                                        else
                                        {
                                            // Only add to chat history if translation is disabled.
                                            // Note: AddTranslationToHistory already calls
                                            // ChatBoxWindow.OnTranslationWasAdded internally, so we must NOT
                                            // call it again here — doing so would enqueue the TTS request
                                            // twice and cause the text to be spoken twice.
                                            _lastChangeTime = DateTime.MinValue;
                                            MainWindow.Instance.AddTranslationToHistory(combinedText, combinedText);

                                            // Mirror the one-shot latch normally set at the end of
                                            // TranslateTextObjectsAsync: when auto-OCR is off, stop
                                            // after this OCR pass so the capture loop does not keep
                                            // re-running OCR (OCR once per trigger, no translation).
                                            if (!ConfigManager.Instance.IsAutoOCREnabled())
                                            {
                                                MainWindow.Instance.isStopOCR = true;
                                            }
                                        }
                                    }

                                    OnFinishedThings(true);
                                }
                                else
                                {
                                    OnFinishedThings(true);
                                }
                            }
                            else if (status == "error" && root.TryGetProperty("message", out JsonElement messageElement))
                            {
                                // Display error message
                                string errorMsg = messageElement.GetString() ?? "Unknown error";
                                Console.WriteLine(errorMsg);
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"JSON parsing error: {ex.Message}");
                        AddTextObject($"JSON Error: {ex.Message}");
                    }
                }
                else
                {
                    // Just display the raw data
                    AddTextObject(data);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing socket data: {ex.Message}");
            }

        }

        /// <summary>
        /// Determines if two text strings are similar using normalized Levenshtein similarity.
        /// Order-aware, so it tolerates small OCR noise but does not treat two different
        /// sentences that merely share common words/characters as the same text.
        /// </summary>
        /// <param name="s1">First text string to compare</param>
        /// <param name="s2">Second text string to compare</param>
        /// <param name="threshold">Similarity threshold (0.0-1.0) to consider texts as similar</param>
        /// <param name="similarity">The computed similarity score (0.0-1.0)</param>
        /// <returns>True if texts are considered similar, false otherwise</returns>
        private bool IsTextSimilar(string s1, string s2, double threshold, out double similarity)
        {
            similarity = 0.0;

            // Handle special cases
            if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) { similarity = 1.0; return true; }
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return false;

            string n1 = NormalizeTextForComparison(s1);
            string n2 = NormalizeTextForComparison(s2);
            if (n1 == n2) { similarity = 1.0; return true; }

            // For very short strings, only exact matching (already checked above) counts
            if (n1.Length < 5 || n2.Length < 5) return false;

            // Levenshtein similarity can never exceed the length ratio, so skip the expensive
            // computation when the lengths alone already rule out a match
            double lengthRatio = Math.Min(n1.Length, n2.Length) / (double)Math.Max(n1.Length, n2.Length);
            if (lengthRatio < threshold)
            {
                similarity = lengthRatio;
                return false;
            }

            similarity = CalculateTextSimilarity(n1, n2);
            return similarity >= threshold;
        }

        /// <summary>
        /// Logs why an OCR result was skipped as similar to the previous one, so false positives can be diagnosed.
        /// Each distinct OCR text is logged only once, since a static screen is skipped on every frame.
        /// </summary>
        private void LogSimilaritySkip(bool hashMatch, double similarity, double threshold, string textContent)
        {
            static string Truncate(string text) =>
                text.Length <= 80 ? text : text.Substring(0, 80) + "...";

            if (textContent == _lastSkipLoggedText) return;
            _lastSkipLoggedText = textContent;

            if (hashMatch)
            {
                Console.WriteLine("Content is similar to previous, skipping translation (hash match)");
                return;
            }

            Console.WriteLine($"Content is similar to previous, skipping translation (similarity={similarity:F2} >= threshold={threshold:F2})");
            Console.WriteLine($"  prev: '{Truncate(_lastTextContent)}'");
            Console.WriteLine($"  new:  '{Truncate(textContent)}'");
        }

        // Filter results array to remove objects that should be ignored based on ignore phrases
        private JsonElement FilterIgnoredPhrases(JsonElement resultsElement)
        {
            if (resultsElement.ValueKind != JsonValueKind.Array)
                return resultsElement;

            try
            {
                // Create a new JSON array for filtered results
                using (var ms = new MemoryStream())
                {
                    using (var writer = new Utf8JsonWriter(ms))
                    {
                        writer.WriteStartArray();

                        // Process each element in the array
                        for (int i = 0; i < resultsElement.GetArrayLength(); i++)
                        {
                            var item = resultsElement[i];

                            // Skip items that don't have the text property
                            if (!item.TryGetProperty("text", out var textElement))
                            {
                                // Include items without text values (might be non-character elements)
                                item.WriteTo(writer);
                                continue;
                            }

                            // Get the text from the element
                            string text = textElement.GetString() ?? "";

                            // Check if we should ignore this text
                            var (shouldIgnore, filteredText) = ShouldIgnoreText(text);

                            if (shouldIgnore)
                            {
                                // Skip this element entirely
                                continue;
                            }

                            // If the text was filtered but not ignored completely
                            if (filteredText != text)
                            {
                                // We need to create a new JSON object with the filtered text
                                writer.WriteStartObject();

                                // Copy all properties except 'text'
                                foreach (var property in item.EnumerateObject())
                                {
                                    if (property.Name != "text")
                                    {
                                        property.WriteTo(writer);
                                    }
                                }

                                // Write the filtered text
                                writer.WritePropertyName("text");
                                writer.WriteStringValue(filteredText);

                                writer.WriteEndObject();
                            }
                            else
                            {
                                // No change to the text, write the entire item
                                item.WriteTo(writer);
                            }
                        }

                        writer.WriteEndArray();
                        writer.Flush();

                        // Read the filtered JSON back
                        ms.Position = 0;
                        using (JsonDocument doc = JsonDocument.Parse(ms))
                        {
                            return doc.RootElement.Clone();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error filtering ignored phrases: {ex.Message}");
                return resultsElement; // Return original on error
            }
        }

        // Check if a text block should be ignored based on ignore phrases.
        // - Exact match / regex on the whole block drop the block.
        // - Contains phrases are removed from the text.
        // - Exact-match phrases also drop single lines: block detection joins nearby lines with '\n',
        //   so a UI label ("Skip") next to dialogue would otherwise never match.
        // Exact match and regex are then checked again on what is left, so "HP Menu" with
        // Contains "HP" and Exact "Menu" is dropped.
        private (bool ShouldIgnore, string FilteredText) ShouldIgnoreText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (true, string.Empty);

            var ignoreSet = ConfigManager.Instance.GetIgnorePhraseSet();
            if (ignoreSet.Phrases.Count == 0)
                return (false, text); // No phrases to check, keep the text as is

            if (MatchesWholeBlock(text, ignoreSet))
                return (true, string.Empty);

            // 1. Remove "Contains" phrases
            string filteredText = text;
            foreach (var (phrase, matchType) in ignoreSet.Phrases)
            {
                if (matchType == IgnorePhraseMatchType.Contains)
                    filteredText = RemoveContainsPhrase(filteredText, phrase);
            }

            // 2. Drop lines that are exactly an exact-match phrase
            if (filteredText.Contains('\n'))
            {
                var keptLines = filteredText.Split('\n').Where(line => !ignoreSet.Phrases.Any(p =>
                    p.MatchType == IgnorePhraseMatchType.ExactMatch && IsExactIgnoreMatch(line, p.Phrase)));
                filteredText = string.Join("\n", keptLines);
            }

            if (filteredText == text)
                return (false, text);

            filteredText = CleanUpFilteredText(filteredText);
            // Only punctuation left over from a removed phrase ("Loading..." -> "...") is noise too
            if (TrimIgnoreNoise(filteredText).Length == 0)
            {
                Console.WriteLine("Ignoring text because it's empty after filtering");
                return (true, string.Empty);
            }

            if (MatchesWholeBlock(filteredText, ignoreSet))
                return (true, string.Empty);

            return (false, filteredText);
        }

        private static bool MatchesWholeBlock(string text, IgnorePhraseSet ignoreSet)
        {
            foreach (var (phrase, matchType) in ignoreSet.Phrases)
            {
                switch (matchType)
                {
                    case IgnorePhraseMatchType.ExactMatch:
                        if (IsExactIgnoreMatch(text, phrase))
                            return true;
                        break;

                    case IgnorePhraseMatchType.RegularExpression:
                        if (ignoreSet.Regexes.TryGetValue(phrase, out var regex))
                        {
                            try
                            {
                                if (regex.IsMatch(text))
                                    return true;
                            }
                            catch (RegexMatchTimeoutException)
                            {
                                Console.WriteLine($"Regex timed out for phrase '{phrase}' on text '{text}'");
                            }
                        }
                        break;
                }
            }
            return false;
        }

        // Remove every occurrence of a phrase (case-insensitive). In space-separated scripts only
        // whole words are removed, so "HP" leaves "WHIP" alone but still strips "HP100" -> "100".
        // Scripts written without spaces (Japanese, Chinese, Thai, Korean particles) match anywhere.
        private static string RemoveContainsPhrase(string text, string phrase)
        {
            if (string.IsNullOrEmpty(phrase))
                return text;

            var sb = new StringBuilder(text.Length);
            int copyFrom = 0;
            int searchFrom = 0;
            while (searchFrom < text.Length)
            {
                int index = text.IndexOf(phrase, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    break;

                int end = index + phrase.Length;
                bool joinsWordBefore = index > 0 && ContinuesWord(text[index - 1], phrase[0]);
                bool joinsWordAfter = end < text.Length && ContinuesWord(phrase[phrase.Length - 1], text[end]);

                if (joinsWordBefore || joinsWordAfter)
                {
                    searchFrom = index + 1;
                    continue;
                }

                sb.Append(text, copyFrom, index - copyFrom);
                copyFrom = end;
                searchFrom = end;
            }

            if (copyFrom == 0)
                return text;
            sb.Append(text, copyFrom, text.Length - copyFrom);
            return sb.ToString();
        }

        // True when two adjacent characters belong to the same word of a space-separated script
        private static bool ContinuesWord(char a, char b)
        {
            static bool IsSpacedScriptLetter(char c) =>
                char.IsLetter(c) && (c < 0x0590 || (c >= 0x1E00 && c <= 0x1FFF)); // Latin incl. Vietnamese, Greek, Cyrillic, Armenian

            return (IsSpacedScriptLetter(a) && IsSpacedScriptLetter(b)) ||
                   (char.IsDigit(a) && char.IsDigit(b));
        }

        // Tidy up after removals: collapse the spaces left behind, trim lines, drop empty lines
        private static string CleanUpFilteredText(string text)
        {
            var lines = text.Split('\n')
                .Select(line => MultipleSpacesRegex.Replace(line, " ").Trim())
                .Where(line => line.Length > 0);
            return string.Join("\n", lines);
        }

        private static readonly Regex MultipleSpacesRegex = new Regex(@"[ \t\u3000]{2,}", RegexOptions.Compiled);

        // Exact match that tolerates what OCR adds around a word: surrounding whitespace and
        // punctuation ("Menu " / "Menu." match "Menu"). A phrase made only of punctuation
        // ("...") is compared on whitespace-trimmed text instead.
        private static bool IsExactIgnoreMatch(string text, string phrase)
        {
            string core = TrimIgnoreNoise(phrase);
            if (core.Length == 0)
                return text.Trim().Equals(phrase.Trim(), StringComparison.OrdinalIgnoreCase);
            return TrimIgnoreNoise(text).Equals(core, StringComparison.OrdinalIgnoreCase);
        }

        private static string TrimIgnoreNoise(string value)
        {
            int start = 0, end = value.Length;
            while (start < end && (char.IsWhiteSpace(value[start]) || char.IsPunctuation(value[start]))) start++;
            while (end > start && (char.IsWhiteSpace(value[end - 1]) || char.IsPunctuation(value[end - 1]))) end--;
            return value.Substring(start, end - start);
        }

        // Display OCR results from JSON - processes character-level blocks
        private void DisplayOcrResults(JsonElement root)
        {
            // Taken over from the field so a frame stored meanwhile cannot dispose it while it is in use
            Bitmap? sourceBitmap = null;
            if (ConfigManager.Instance.IsAutoSetOverlayBackground())
            {
                lock (_ocrSourceFrameLock)
                {
                    sourceBitmap = _ocrSourceFrame;
                    _ocrSourceFrame = null;
                }
            }

            try
            {
                // Check for results array
                if (root.TryGetProperty("results", out JsonElement resultsElement) &&
                    resultsElement.ValueKind == JsonValueKind.Array)
                {
                    // Get processing time if available
                    double processingTime = 0;
                    if (root.TryGetProperty("processing_time_seconds", out JsonElement timeElement))
                    {
                        processingTime = timeElement.GetDouble();
                    }

                    // Get minimum text fragment size from config
                    int minTextFragmentSize = ConfigManager.Instance.GetMinTextFragmentSize();

                    // Clear existing text objects before adding new ones
                    ClearAllTextObjects();

                    // Check if auto merge overlapping text is enabled
                    bool autoMergeEnabled = ConfigManager.Instance.IsAutoMergeOverlappingTextEnabled();

                    // Process text blocks that have already been grouped by CharacterBlockDetectionManager
                    int resultCount = resultsElement.GetArrayLength();

                    // If auto merge is enabled, collect all text blocks first
                    List<TempTextBlock> tempBlocks = new List<TempTextBlock>();

                    for (int i = 0; i < resultCount; i++)
                    {
                        JsonElement item = resultsElement[i];

                        if (item.TryGetProperty("text", out JsonElement textElement) &&
                            item.TryGetProperty("confidence", out JsonElement confElement))
                        {
                            string text = textElement.GetString() ?? "";

                            if (text.Length < minTextFragmentSize)
                            {
                                continue;
                            }

                            double confidence = confElement.GetDouble();
                            var (x, y, width, height) = ReadRect(item);

                            if (autoMergeEnabled)
                            {
                                int? bubbleId = item.TryGetProperty("bubble_id", out JsonElement bubbleElement) && bubbleElement.ValueKind == JsonValueKind.Number
                                    ? bubbleElement.GetInt32()
                                    : null;
                                tempBlocks.Add(new TempTextBlock { Text = text, X = x, Y = y, Width = width, Height = height, Confidence = confidence, BubbleId = bubbleId });
                            }
                            else
                            {
                                // OCR boxes are in capture pixels; the overlay canvas is in MonitorWindow DIPs
                                DpiHelper.GetWindowRenderDpi(MonitorWindow.Instance, out double dpiScaleX, out double dpiScaleY);
                                CreateTextObjectAtPosition(text, x / dpiScaleX, y / dpiScaleY, width / dpiScaleX, height / dpiScaleY, confidence, sourceBitmap);
                            }
                        }
                    }

                    // If auto merge is enabled, merge overlapping text blocks
                    if (autoMergeEnabled && tempBlocks.Count > 0)
                    {
                        var (mergedBlocks, blockOriginalBounds) = MergeOverlappingTextBlocks(tempBlocks);
                        // OCR boxes are in capture pixels; the overlay canvas is in MonitorWindow DIPs
                        DpiHelper.GetWindowRenderDpi(MonitorWindow.Instance, out double dpiScaleX, out double dpiScaleY);

                        for (int i = 0; i < mergedBlocks.Count; i++)
                        {
                            var block = mergedBlocks[i];
                            var bounds = blockOriginalBounds[i];
                            // Calculate dimensions from original bounding box to cover all original text
                            double originalX = bounds.MinX;
                            double originalY = bounds.MinY;
                            double originalWidth = bounds.MaxX - bounds.MinX;
                            double originalHeight = bounds.MaxY - bounds.MinY;

                            CreateTextObjectAtPosition(block.Text, originalX / dpiScaleX, originalY / dpiScaleY,
                                originalWidth / dpiScaleX, originalHeight / dpiScaleY, block.Confidence, sourceBitmap);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error displaying OCR results: {ex.Message}");
                OnFinishedThings(true);
            }
            finally
            {
                if (sourceBitmap != null)
                {
                    sourceBitmap.Dispose();
                }
            }
        }

        // Bounding box of an OCR result item from its "rect" points (zero size when missing)
        private static (double X, double Y, double Width, double Height) ReadRect(JsonElement item)
        {
            if (!item.TryGetProperty("rect", out JsonElement boxElement) || boxElement.ValueKind != JsonValueKind.Array)
                return (0, 0, 0, 0);

            try
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                for (int p = 0; p < boxElement.GetArrayLength(); p++)
                {
                    if (boxElement[p].ValueKind == JsonValueKind.Array &&
                        boxElement[p].GetArrayLength() >= 2)
                    {
                        double pointX = boxElement[p][0].GetDouble();
                        double pointY = boxElement[p][1].GetDouble();

                        minX = Math.Min(minX, pointX);
                        minY = Math.Min(minY, pointY);
                        maxX = Math.Max(maxX, pointX);
                        maxY = Math.Max(maxY, pointY);
                    }
                }

                if (minX > maxX)
                    return (0, 0, 0, 0);
                return (minX, minY, maxX - minX, maxY - minY);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing rect: {ex.Message}");
                return (0, 0, 0, 0);
            }
        }

        // The texts DisplayOcrResults will turn into text objects, in the same order: same fragment-size
        // filter and the same merging. The prefetch translates these, so its cache keys match.
        private List<string> GetTranslatableTexts(JsonElement resultsElement)
        {
            var texts = new List<string>();
            if (resultsElement.ValueKind != JsonValueKind.Array)
                return texts;

            int minTextFragmentSize = ConfigManager.Instance.GetMinTextFragmentSize();
            bool autoMergeEnabled = ConfigManager.Instance.IsAutoMergeOverlappingTextEnabled();
            var tempBlocks = new List<TempTextBlock>();

            foreach (JsonElement item in resultsElement.EnumerateArray())
            {
                if (!item.TryGetProperty("text", out JsonElement textElement) ||
                    !item.TryGetProperty("confidence", out JsonElement confElement))
                    continue;

                string text = textElement.GetString() ?? "";
                if (text.Length < minTextFragmentSize)
                    continue;

                if (autoMergeEnabled)
                {
                    var (x, y, width, height) = ReadRect(item);
                    int? bubbleId = item.TryGetProperty("bubble_id", out JsonElement bubbleElement) && bubbleElement.ValueKind == JsonValueKind.Number
                        ? bubbleElement.GetInt32()
                        : null;
                    tempBlocks.Add(new TempTextBlock { Text = text, X = x, Y = y, Width = width, Height = height, Confidence = confElement.GetDouble(), BubbleId = bubbleId });
                }
                else
                {
                    texts.Add(text);
                }
            }

            if (autoMergeEnabled && tempBlocks.Count > 0)
            {
                texts.AddRange(MergeOverlappingTextBlocks(tempBlocks).blocks.Select(b => b.Text));
            }

            // CreateTextObjectAtPosition skips blank text
            return texts.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        }

        // Create a text object at the specified position with confidence info
        private void CreateTextObjectAtPosition(string text, double x, double y, double width, double height, double confidence, Bitmap? sourceBitmap)
        {
            try
            {
                // Check if we need to run on the UI thread
                if (!Application.Current.Dispatcher.CheckAccess())
                {
                    // Run on UI thread to ensure STA compliance
                    Application.Current.Dispatcher.Invoke(() =>
                        CreateTextObjectAtPosition(text, x, y, width, height, confidence, sourceBitmap));
                    return;
                }

                // Store current capture position with the text object
                int captureX = _currentCaptureX;
                int captureY = _currentCaptureY;

                // Validate input parameters
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("Cannot create text object with empty text");
                    return;
                }

                // Ensure width and height are valid
                if (double.IsNaN(width) || double.IsInfinity(width) || width < 0)
                {
                    width = 0; // Let the text determine natural width
                }

                if (double.IsNaN(height) || double.IsInfinity(height) || height < 0)
                {
                    height = 0; // Let the text determine natural height
                }

                // Ensure coordinates are valid
                if (double.IsNaN(x) || double.IsInfinity(x))
                {
                    x = 10; // Default x position
                }

                if (double.IsNaN(y) || double.IsInfinity(y))
                {
                    y = 10; // Default y position
                }

                // Configured colors are the default; auto background only overrides them when sampling succeeds
                Color textColor = ConfigManager.Instance.GetOverlayTextColor();
                Color bgColor = ConfigManager.Instance.GetOverlayBackgroundColor();
                System.Windows.Media.Imaging.BitmapSource? erasedPatch = null;
                Rect erasedBounds = Rect.Empty;
                if (ConfigManager.Instance.IsAutoSetOverlayBackground() && sourceBitmap != null)
                {
                    try
                    {
                        // Back from MonitorWindow DIPs to capture pixels (same scale as the conversion above)
                        DpiHelper.GetWindowRenderDpi(MonitorWindow.Instance, out double dpiScaleX, out double dpiScaleY);

                        int bitmapX = (int)(x * dpiScaleX);
                        int bitmapY = (int)(y * dpiScaleY);
                        int bitmapWidth = Math.Max(1, (int)(width * dpiScaleX));
                        int bitmapHeight = Math.Max(1, (int)(height * dpiScaleY));
                        var textBox = new System.Drawing.Rectangle(bitmapX, bitmapY, bitmapWidth, bitmapHeight);

                        (bgColor, textColor) = ColorUtils.SampleOverlayColors(sourceBitmap, textBox);

                        // Original text removed from its own background, shown under the translation
                        erasedPatch = TextEraser.CreateErasedPatch(sourceBitmap, textBox, bgColor, out var patchPixels);
                        erasedBounds = new Rect(patchPixels.X / dpiScaleX, patchPixels.Y / dpiScaleY,
                                                patchPixels.Width / dpiScaleX, patchPixels.Height / dpiScaleY);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error sampling overlay colors: {ex.Message}");
                    }
                }

                SolidColorBrush textBrush = new SolidColorBrush(textColor);
                SolidColorBrush bgBrush = new SolidColorBrush(bgColor);

                // Add the text object to the UI
                TextObject textObject = new TextObject(
                    text,  // Just the text, without confidence
                    x, y, width, height,
                    textBrush,
                    bgBrush,
                    captureX, captureY  // Store original capture coordinates
                );
                textObject.ID = "text_" + GetNextTextID();
                if (erasedPatch != null)
                {
                    textObject.SetErasedBackground(erasedPatch, erasedBounds);
                }

                // Add to our collection
                _textObjects.Add(textObject);

                // Raise event to notify listeners (MonitorWindow)
                TextObjectAdded?.Invoke(this, textObject);

                if (ConfigManager.Instance.IsLeaveTranslationOnscreenEnabled()
                    && ConfigManager.Instance.IsAutoTranslateEnabled())
                {
                    //do nothing, don't want to show the source language
                }
                else
                {
                    textObject.UIElement = textObject.CreateUIElement();
                }
                MonitorWindow.Instance.CreateMonitorOverlayFromTextObject(this, textObject);

                // Console.WriteLine($"Added text '{text}' at position ({x}, {y}) with size {width}x{height}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating text object: {ex.Message}");
                if (ex.StackTrace != null)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
        }


        // Set the current capture position
        public void SetCurrentCapturePosition(int x, int y)
        {
            _currentCaptureX = x;
            _currentCaptureY = y;
        }

        // Screen position of the top-left pixel of the last captured bitmap
        public (int X, int Y) CurrentCapturePosition => (_currentCaptureX, _currentCaptureY);

        // Update text object positions based on capture position changes
        public void UpdateTextObjectPositions(int offsetX, int offsetY)
        {
            try
            {
                // Only proceed if we have text objects
                if (_textObjects.Count == 0) return;

                // Check if we need to run on the UI thread
                if (!Application.Current.Dispatcher.CheckAccess())
                {
                    // Run on UI thread to ensure UI updates are thread-safe
                    Application.Current.Dispatcher.Invoke(() =>
                        UpdateTextObjectPositions(offsetX, offsetY));
                    return;
                }

                foreach (TextObject textObj in _textObjects)
                {
                    // Calculate new position based on original capture position and current offset
                    // Use negative offset since we want text to move in opposite direction of the window
                    double newX = textObj.X - offsetX;
                    double newY = textObj.Y - offsetY;

                    // Update position
                    textObj.X = newX;
                    textObj.Y = newY;

                    // Update UI element
                    textObj.UpdateUIElement();
                }

                // Refresh the monitor window to show updated positions
                if (MonitorWindow.Instance.IsVisible)
                {
                    MonitorWindow.Instance.RefreshOverlays();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating text positions: {ex.Message}");
            }
        }

        /// <summary>
        /// Filters out low-confidence characters from the OCR results
        /// </summary>
        private JsonElement FilterLowConfidenceCharacters(JsonElement resultsElement)
        {
            if (resultsElement.ValueKind != JsonValueKind.Array)
                return resultsElement;

            try
            {
                // Get minimum confidence threshold from config
                double minLetterConfidence = ConfigManager.Instance.GetMinLetterConfidence();

                // Create output array for high-confidence results only
                using (var ms = new MemoryStream())
                {
                    using (var writer = new Utf8JsonWriter(ms))
                    {
                        writer.WriteStartArray();

                        // Process each element
                        for (int i = 0; i < resultsElement.GetArrayLength(); i++)
                        {
                            var item = resultsElement[i];

                            // Skip items that don't have required properties
                            if (!item.TryGetProperty("confidence", out var confElement))
                            {
                                // Include items without confidence values (might be non-character elements)
                                item.WriteTo(writer);
                                continue;
                            }

                            // Get confidence value
                            double confidence = confElement.GetDouble();

                            // Only include elements with confidence above threshold
                            if (confidence >= minLetterConfidence)
                            {
                                item.WriteTo(writer);
                            }
                        }

                        writer.WriteEndArray();
                        writer.Flush();

                        // Read the filtered JSON back
                        ms.Position = 0;
                        using (JsonDocument doc = JsonDocument.Parse(ms))
                        {
                            return doc.RootElement.Clone();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error filtering low-confidence characters: {ex.Message}");
                return resultsElement; // Return original on error
            }
        }

        static readonly HashSet<char> g_charsToStripFromHash =
             new(" \n\r\t,.-:;ー・…。、~』!^へ?\"'`()[]{}【】「」『』<>+=*/\\|_@#$%&");


        private string GenerateContentHash(JsonElement resultsElement)
        {
            if (resultsElement.ValueKind != JsonValueKind.Array)
                return string.Empty;

            StringBuilder contentBuilder = new();

            // Add each character to the content builder
            contentBuilder.Append(resultsElement.GetArrayLength());
            contentBuilder.Append('|');

            foreach (JsonElement element in resultsElement.EnumerateArray())
            {
                if (!element.TryGetProperty("text", out JsonElement textElement))
                {
                    continue;
                }

                string text = textElement.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                // Normalize the text
                string normalizedText = NormalizeTextForHash(text);
                if (!string.IsNullOrEmpty(normalizedText))
                {
                    contentBuilder.Append(normalizedText);
                    // Add a separator between characters
                    contentBuilder.Append('|');
                }
            }

            string hash = contentBuilder.ToString();
            //Console.WriteLine($"Generated hash: {hash}");
            return hash;
        }

        /// <summary>
        /// Normalizes the text for hashing by removing certain characters and converting to lowercase.
        /// </summary>
        private string NormalizeTextForHash(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;


            text = text.ToLowerInvariant();

            StringBuilder sb = new StringBuilder();
            bool lastWasSpace = true; // Start with a space to handle leading characters

            foreach (char c in text)
            {
                if (c == 'ツ')
                {
                    sb.Append('ッ');
                    lastWasSpace = false;
                }
                // Handle whitespace
                else if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace)
                    {
                        sb.Append(' '); // Only add a space if the last character was not a space
                        lastWasSpace = true;
                    }
                }

                else if (!g_charsToStripFromHash.Contains(c))
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
            }

            // Remove whiespace if it's at the end
            string result = sb.ToString();
            if (result.Length > 0 && result[result.Length - 1] == ' ')
            {
                result = result.Substring(0, result.Length - 1);
            }

            return result;
        }

        // Process bitmap directly with Windows OCR (no file saving)
        public async void ProcessWithWindowsOCR(System.Drawing.Bitmap bitmap, string sourceLanguage)
        {
            try
            {
                //Console.WriteLine("Starting Windows OCR processing directly from bitmap...");

                try
                {
                    // Get the text lines from Windows OCR directly from the bitmap
                    var textLines = await WindowsOCRManager.Instance.GetOcrLinesFromBitmapAsync(bitmap, sourceLanguage);
                    // Console.WriteLine($"Windows OCR found {textLines.Count} text lines");

                    // Process the OCR results with language code
                    await WindowsOCRManager.Instance.ProcessWindowsOcrResults(textLines, sourceLanguage);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Windows OCR error: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing bitmap with Windows OCR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                // Make sure bitmap is properly disposed
                try
                {
                    // Dispose bitmap - System.Drawing.Bitmap doesn't have a Disposed property,
                    // so we'll just dispose it if it's not null
                    if (bitmap != null)
                    {
                        bitmap.Dispose();
                    }
                }
                catch
                {
                    // Ignore disposal errors
                }

                MainWindow.Instance.SetOCRCheckIsWanted(true);

            }
        }

        private bool IsDuplicateAudio(string newText, List<string> recentTexts, int checkLastN = 3)
        {
            if (recentTexts.Count == 0) return false;

            string normalizedNew = NormalizeTextForComparison(newText);

            int checkCount = Math.Min(checkLastN, recentTexts.Count);
            for (int i = recentTexts.Count - checkCount; i < recentTexts.Count; i++)
            {
                string normalizedExisting = NormalizeTextForComparison(recentTexts[i]);

                if (normalizedNew == normalizedExisting)
                {
                    Console.WriteLine($"[DUPLICATE] Exact match: '{newText}'");
                    return true;
                }

                double similarity = CalculateTextSimilarity(normalizedNew, normalizedExisting);
                if (similarity > 0.9)
                {
                    Console.WriteLine($"[DUPLICATE] High similarity ({similarity:P0}): '{newText}' vs '{recentTexts[i]}'");
                    return true;
                }
            }

            return false;
        }

        private string NormalizeTextForComparison(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            return System.Text.RegularExpressions.Regex.Replace(
                text.ToLower().Trim(),
                @"\s+",
                " "
            );
        }

        private double CalculateTextSimilarity(string s1, string s2)
        {
            if (s1 == s2) return 1.0;
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

            // Levenshtein distance
            int maxLen = Math.Max(s1.Length, s2.Length);
            int distance = LevenshteinDistance(s1, s2);

            return 1.0 - ((double)distance / maxLen);
        }

        private int LevenshteinDistance(string s1, string s2)
        {
            // Two-row DP: same result as the full matrix but O(n) memory, since this runs on every OCR frame
            int[] prev = new int[s2.Length + 1];
            int[] curr = new int[s2.Length + 1];

            for (int j = 0; j <= s2.Length; j++)
                prev[j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    curr[j] = Math.Min(
                        Math.Min(prev[j] + 1, curr[j - 1] + 1),
                        prev[j - 1] + cost
                    );
                }
                (prev, curr) = (curr, prev);
            }

            return prev[s2.Length];
        }

        public void AddAudioTextObject(string audioText)
        {
            if (string.IsNullOrEmpty(audioText)) return;

            lock (_audioBatchLock)
            {

                if (IsDuplicateAudio(audioText, _audioBatch, checkLastN: 3))
                {
                    Console.WriteLine($"[SKIP] Duplicate audio detected, ignoring: '{audioText}'");
                    return;
                }


                _audioBatch.Add(audioText);
                Console.WriteLine($"Added audio to batch: '{audioText}'. Batch size: {_audioBatch.Count}");
                _hasNewAudioSinceLastTranslation = true;

                // Lines that arrive while a translation is in flight are batched and sent together
                // as soon as it finishes (see ProcessAudioBatchAsync). No fixed debounce: the VAD
                // already cuts at sentence pauses, so waiting here only added latency.
                if (_isProcessingAudioBatch)
                {
                    Console.WriteLine("Translation in progress, line queued for the next batch");
                    return;
                }
            }

            ProcessAudioBatchCallback(null);
        }

        private void ProcessAudioBatchCallback(object? state)
        {
            Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await ProcessAudioBatchAsync();
            });
        }

        private async Task ProcessAudioBatchAsync()
        {
            List<string> batchToProcess;

            lock (_audioBatchLock)
            {
                if (!_hasNewAudioSinceLastTranslation)
                {
                    Console.WriteLine("[SKIP] No new audio since last translation, ignoring timer trigger");
                    return;
                }

                if (_isProcessingAudioBatch || _audioBatch.Count == 0)
                {
                    return;
                }

                _isProcessingAudioBatch = true;

                batchToProcess = new List<string>(_audioBatch);
                _audioBatch.Clear();
                _hasNewAudioSinceLastTranslation = false;

                Console.WriteLine($"Processing audio batch with {batchToProcess.Count} items");
            }

            try
            {
                _textObjects.Clear();

                string combinedAudio = string.Join(" ", batchToProcess);

                Console.WriteLine($"Combined audio text: '{combinedAudio}'");

                var audioTextObject = new TextObject(
                    text: combinedAudio,
                    x: 100,
                    y: 100,
                    width: 800,
                    height: 100,
                    textColor: null,
                    backgroundColor: null,
                    captureX: 0,
                    captureY: 0
                );

                _textObjects.Add(audioTextObject);

                AudioTiming.Log("4. Translation start", $"{batchToProcess.Count} line(s), {ConfigManager.Instance.GetCurrentTranslationService()}");
                var translateTimer = System.Diagnostics.Stopwatch.StartNew();
                await TranslateTextObjectsAsync();
                AudioTiming.Log("5. Translation done", $"{translateTimer.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing audio batch: {ex.Message}");
            }
            finally
            {
                bool hasPending;
                lock (_audioBatchLock)
                {
                    _isProcessingAudioBatch = false;
                    hasPending = _audioBatch.Count > 0;
                }

                // Lines queued during this translation would otherwise wait for the next line
                // of dialogue to trigger them — forever, if the game goes quiet.
                if (hasPending) ProcessAudioBatchCallback(null);
            }
        }

        // Process bitmap directly with Windows OCR (no file saving)
        public async void ProcessWithOneOCR(System.Drawing.Bitmap bitmap, string sourceLanguage)
        {
            try
            {

                try
                {
                    var textLines = await OneOCRManager.Instance.GetOcrLinesFromBitmapAsync(bitmap);

                    // Process the OCR results with language code
                    await OneOCRManager.Instance.ProcessOneOcrResults(textLines, sourceLanguage, bitmap);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"One OCR error: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing bitmap with One OCR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                // Make sure bitmap is properly disposed
                try
                {
                    // Dispose bitmap - System.Drawing.Bitmap doesn't have a Disposed property,
                    // so we'll just dispose it if it's not null
                    if (bitmap != null)
                    {
                        bitmap.Dispose();
                    }
                }
                catch
                {
                    // Ignore disposal errors
                }

                MainWindow.Instance.SetOCRCheckIsWanted(true);

            }
        }

        // Called when the application is closing
        public void Finish()
        {
            try
            {
                // Clean up resources
                Console.WriteLine("Logic finalized");

                SetOcrSourceFrame(null);

                // Clear all text objects
                ClearAllTextObjects();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during cleanup: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // Add a text object with specific text and optional position and styling
        public void AddTextObject(string text,
                                 double x = 0,
                                 double y = 0,
                                 double width = 0,
                                 double height = 0,
                                 SolidColorBrush? textColor = null,
                                 SolidColorBrush? backgroundColor = null)
        {
            try
            {
                // Validate text
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("Cannot add text object with empty text");
                    return;
                }

                // Ensure position and dimensions are valid
                if (double.IsNaN(x) || double.IsInfinity(x)) x = 0;
                if (double.IsNaN(y) || double.IsInfinity(y)) y = 0;
                if (double.IsNaN(width) || double.IsInfinity(width) || width < 0) width = 0;
                if (double.IsNaN(height) || double.IsInfinity(height) || height < 0) height = 0;

                backgroundColor ??= new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)); // Half-transparent black

                // Create the text object with specified parameters
                TextObject textObject = new TextObject(
                    text,
                    x, y,
                    width, height,
                    textColor,
                    backgroundColor);

                // Add to our collection
                _textObjects.Add(textObject);

                // Don't add to main window UI anymore
                // Just raise the event to notify MonitorWindow
                TextObjectAdded?.Invoke(this, textObject);

                Console.WriteLine($"Added text '{text}' at position {x}, {y}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding text: {ex.Message}");
            }
        }


        // Clear all text objects
        public void ClearAllTextObjects()
        {
            try
            {
                // Check if we need to run on the UI thread
                if (!Application.Current.Dispatcher.CheckAccess())
                {
                    // Run on UI thread to ensure STA compliance
                    Application.Current.Dispatcher.Invoke(() => ClearAllTextObjects());
                    return;
                }

                // Clear the collection
                _textObjects.Clear();
                _textIDCounter = 0;
                // No need to remove from the main window UI anymore

                Console.WriteLine("All text objects cleared");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing text objects: {ex.Message}");
                if (ex.StackTrace != null)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        //! Process structured JSON translation from ChatGPT or other services
        private void ProcessStructuredJsonTranslation(JsonElement translatedRoot, bool addToChatBox = true)
        {
            try
            {
                Console.WriteLine("Processing structured JSON translation");
                // Check if we have text_blocks array in the translated JSON
                if (translatedRoot.TryGetProperty("text_blocks", out JsonElement textBlocksElement) &&
                    textBlocksElement.ValueKind == JsonValueKind.Array)
                {
                    Console.WriteLine($"Found {textBlocksElement.GetArrayLength()} text blocks in translated JSON");

                    // Get current target language
                    string targetLanguage = ConfigManager.Instance.GetTargetLanguage().ToLower();
                    Console.WriteLine($"Target language: ----------------------------{targetLanguage}");
                    // Define RTL (Right-to-Left) languages
                    HashSet<string> rtlLanguages = new HashSet<string> {
                        "ar", "arabic", "fa", "farsi", "persian", "he", "hebrew", "ur", "urdu"
                    };

                    // Check if target language is RTL
                    bool isRtlLanguage = rtlLanguages.Contains(targetLanguage);

                    if (isRtlLanguage)
                    {
                        Console.WriteLine($"Detected RTL language: {targetLanguage}");
                    }

                    // Process each translated block
                    for (int i = 0; i < textBlocksElement.GetArrayLength(); i++)
                    {
                        var block = textBlocksElement[i];

                        if (block.TryGetProperty("id", out JsonElement idElement) &&
                            block.TryGetProperty("text", out JsonElement translatedTextElement))
                        {
                            string id = idElement.GetString() ?? "";
                            string translatedText = translatedTextElement.GetString() ?? "";

                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(translatedText))
                            {
                                // Check if this is our combined text block with ID=1
                                if (id == "999")
                                {
                                    // Split the translated text using the separator
                                    string[] translatedParts = translatedText.Split("##|||##");

                                    // Assign each part to the corresponding text object
                                    for (int j = 0; j < Math.Min(translatedParts.Length, _textObjects.Count); j++)
                                    {
                                        // Clean up the translated text - remove any remaining separators that might have been part of the translation
                                        // (legacy |||RST||| and mangled ##|||## variants). Only fragments around "|||" are removed,
                                        // so real text such as "FIRST" or "#1" is left intact.
                                        string cleanTranslatedText = System.Text.RegularExpressions.Regex.Replace(
                                            translatedParts[j],
                                            @"\|{3}\s*RST\s*\|{3}|#{0,2}\s*\|{3}\s*#{0,2}",
                                            ""
                                        );

                                        // Apply RTL specific handling if needed
                                        if (isRtlLanguage)
                                        {
                                            // Set flow direction for RTL languages
                                            _textObjects[j].FlowDirection = FlowDirection.RightToLeft;

                                            // Optionally add Unicode RLM (Right-to-Left Mark) if needed
                                            if (!cleanTranslatedText.StartsWith('\u200F'))
                                            {
                                                cleanTranslatedText = "\u200F" + cleanTranslatedText;
                                            }
                                        }
                                        else
                                        {
                                            // Ensure LTR for non-RTL languages
                                            _textObjects[j].FlowDirection = FlowDirection.LeftToRight;
                                        }

                                        // Update the text object with the cleaned translated text
                                        _textObjects[j].TextTranslated = cleanTranslatedText;
                                        _textObjects[j].UpdateUIElement();
                                        Console.WriteLine($"Updated text object at index {j} with translation from Google Translate");
                                    }

                                    // We've processed the combined text block, so we can break out of the loop
                                    break;
                                }
                                else
                                {
                                    // Handle the old way for backward compatibility
                                    // Find the matching text object by ID
                                    var matchingTextObj = _textObjects.FirstOrDefault(t => t.ID == id);
                                    if (matchingTextObj != null)
                                    {
                                        // Apply RTL specific handling if needed
                                        if (isRtlLanguage)
                                        {
                                            // Set flow direction for RTL languages
                                            matchingTextObj.FlowDirection = FlowDirection.RightToLeft;

                                            // Optionally add Unicode RLM (Right-to-Left Mark) if needed
                                            // This can help with mixed content
                                            if (!translatedText.StartsWith('\u200F'))
                                            {
                                                translatedText = "\u200F" + translatedText;
                                            }
                                        }
                                        else
                                        {
                                            // Ensure LTR for non-RTL languages
                                            matchingTextObj.FlowDirection = FlowDirection.LeftToRight;
                                        }

                                        // Update the corresponding text object
                                        matchingTextObj.TextTranslated = translatedText;
                                        matchingTextObj.UpdateUIElement();
                                        Console.WriteLine($"Updated text object {id} with translation");
                                    }
                                    else if (id.StartsWith("text_"))
                                    {
                                        // Try to extract index from ID (text_X format)
                                        string indexStr = id.Substring(5); // Remove "text_" prefix
                                        if (int.TryParse(indexStr, out int index) && index >= 0 && index < _textObjects.Count)
                                        {
                                            // Apply RTL specific handling if needed
                                            if (isRtlLanguage)
                                            {
                                                // Set flow direction for RTL languages
                                                _textObjects[index].FlowDirection = FlowDirection.RightToLeft;

                                                // Optionally add Unicode RLM (Right-to-Left Mark) if needed
                                                if (!translatedText.StartsWith('\u200F'))
                                                {
                                                    translatedText = "\u200F" + translatedText;
                                                }
                                            }
                                            else
                                            {
                                                // Ensure LTR for non-RTL languages
                                                _textObjects[index].FlowDirection = FlowDirection.LeftToRight;
                                            }

                                            // Update by index if ID matches format
                                            _textObjects[index].TextTranslated = translatedText;
                                            _textObjects[index].UpdateUIElement();
                                            Console.WriteLine($"Updated text object at index {index} with translation");
                                        }
                                        else
                                        {
                                            Console.WriteLine($"Could not find text object with ID {id}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("No text_blocks array found in translated JSON");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing structured JSON translation: {ex.Message}");
            }

            if (addToChatBox)
            {
                AddTranslatedTextObjectsToChatBox();
            }
        }

        //!Process the finished translation into text blocks and the chatbox
        void ProcessTranslatedJSON(string translationResponse, bool addToChatBox = true)
        {
            try
            {
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();

                // Use shared method to extract inner content
                var (innerContent, isGoogleTranslate) = ExtractInnerContentFromResponse(translationResponse);

                if (string.IsNullOrEmpty(innerContent))
                {
                    Console.WriteLine($"Could not extract content from {currentService} response");
                    OnFinishedThings(true);
                    return;
                }

                if (isGoogleTranslate)
                {
                    Console.WriteLine("Translation response with 'translations' format detected (Google Translate / Yandex / compatible)");
                    using JsonDocument doc = JsonDocument.Parse(innerContent);
                    ProcessGoogleTranslateJson(doc.RootElement, addToChatBox);
                    return;
                }

                // Parse inner content and process structured JSON
                try
                {
                    using JsonDocument translatedDoc = JsonDocument.Parse(innerContent);
                    ProcessStructuredJsonTranslation(translatedDoc.RootElement, addToChatBox);
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Error parsing translation JSON: {ex.Message}");
                    OnFinishedThings(true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ProcessTranslatedJSON: {ex.Message}");
                OnFinishedThings(true);
            }
        }

        private void AddTranslatedTextObjectsToChatBox()
        {
            var sortedTextObjects = _textObjects.OrderBy(t => t.Y).ToList();

            foreach (var textObject in sortedTextObjects)
            {
                string originalText = textObject.Text;
                string translatedText = textObject.TextTranslated;

                if (!string.IsNullOrEmpty(originalText) && !string.IsNullOrEmpty(translatedText))
                {
                    TranslationCompleted?.Invoke(this, new TranslationEventArgs
                    {
                        OriginalText = originalText,
                        TranslatedText = translatedText
                    });
                }
                else
                {
                    Console.WriteLine($"Skipping empty translation - Original: '{originalText}', Translated: '{translatedText}'");
                }
            }
        }

        private string MapLanguageCode(string language)
        {
            return language.ToLower() switch
            {
                "ja" => "japanese",
                "en" => "english",
                "ch_sim" => "chinese",
                "ko" => "korean",
                "vi" => "vietnamese",
                "fr" => "french",
                "de" => "german",
                "es" => "spanish",
                "it" => "italian",
                "ru" => "russian",
                "hi" => "hindi",
                "pt" => "portuguese",
                "ar" => "arabic",
                "bg" or "bg-bg" => "bulgarian",
                "nl" => "dutch",
                "hr" => "Croatian",
                "pl" => "Polish",
                "ro" => "Romanian",
                "fa" => "Farsi",
                "cs" => "Czech",
                "id" => "Indonesian",
                "th" => "ThaiLand",
                "tr" => "Turkish",
                "ch_tra" => "Traditional Chinese",
                "hu" => "Hungarian",
                "si" => "Sinhala",
                "da" => "Danish",
                "uk" => "ukrainian",
                "fi" => "Finnish",
                "ckb" => "Central Kurdish",
                "bn" => "Bengali",
                "el" => "Greek",
                "he" => "Hebrew",
                _ => language
            };
        }

        //!Convert textobjects to json and send for translation
        // Manga mode: a page up to this many bubbles goes in a single request
        private const int MangaSingleRequestMaxBlocks = 15;
        // Larger pages are split into batches of this size, in reading order
        private const int MangaBatchSize = 10;
        private const int MangaMaxParallelRequests = 3;

        public async Task TranslateTextObjectsAsync()
        {
            try
            {
                // Show translation status at the beginning
                MonitorWindow.Instance.ShowTranslationStatus(false);

                // Also show translation status in ChatBoxWindow if it's open
                if (ChatBoxWindow.Instance != null)
                {
                    ChatBoxWindow.Instance.ShowTranslationStatus(false);
                }

                if (_textObjects.Count == 0)
                {
                    Console.WriteLine("No text objects to translate");
                    OnFinishedThings(true);
                    return;
                }

                // Get previous context if enabled
                var previousContext = GetPreviousContext();

                // Get game info if available
                string gameInfo = ConfigManager.Instance.GetGameInfo();

                // Get the prompt template
                string prompt = GetLlmPrompt();
                prompt = prompt.Replace("source_language", MapLanguageCode(GetSourceLanguage())).Replace("target_language", MapLanguageCode(GetTargetLanguage()));

                _translationStopwatch.Restart();

                SetWaitingForTranslationToFinish(true);

                // Create translation service based on current configuration
                ITranslationService translationService = TranslationServiceFactory.CreateService();
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();
                string currentModel = ConfigManager.Instance.GetCurrentTranslationModel();

                bool mangaModeEnabled = ConfigManager.Instance.IsMangaModeEnabled();
                bool hadSuccessfulTranslation = false;

                // Text seen before under the same settings is shown at once from the cache
                bool cacheEnabled = ConfigManager.Instance.IsTranslationCacheEnabled();
                bool readCache = cacheEnabled && !_bypassTranslationCacheOnce;
                _bypassTranslationCacheOnce = false;
                if (cacheEnabled)
                {
                    TranslationCache.Instance.UseContext(BuildTranslationCacheContext(currentService, currentModel, prompt, gameInfo));
                }
                if (readCache)
                {
                    // The same text is already being translated in the background: use that result instead of a second request
                    await WaitForMatchingPrefetchAsync();
                }
                var cachedObjects = readCache ? ApplyCachedTranslations() : new HashSet<TextObject>();

                if (cachedObjects.Count == _textObjects.Count)
                {
                    OcrTiming.Log("3. Translation served from cache", $"{cachedObjects.Count} block(s)");
                    AddTranslatedTextObjectsToChatBox();
                }
                else if (mangaModeEnabled)
                {
                    // Each bubble stays its own id-tagged block, so translations can't bleed into each other,
                    // but the whole page goes in one request: faster, and the model sees the conversation.
                    // Bubbles already translated from the cache are not sent.
                    var blocks = _textObjects.Where(t => !cachedObjects.Contains(t)).ToList();
                    var batches = blocks.Count <= MangaSingleRequestMaxBlocks
                        ? new List<List<TextObject>> { blocks }
                        : blocks.Chunk(MangaBatchSize).Select(c => c.ToList()).ToList();

                    using var concurrency = new SemaphoreSlim(MangaMaxParallelRequests);
                    var processLock = new object();

                    async Task<bool> TranslateBatchAsync(List<TextObject> batch)
                    {
                        await concurrency.WaitAsync();
                        try
                        {
                            var payload = batch.Select(t => (object)new { id = t.ID, text = t.Text }).ToList();
                            string jsonToTranslate = BuildTranslationRequestJson(payload, previousContext, gameInfo);
                            LogManager.Instance.LogLlmRequest(prompt, jsonToTranslate);

                            string? translationResponse = await translationService.TranslateAsync(jsonToTranslate, prompt);
                            if (string.IsNullOrEmpty(translationResponse))
                            {
                                Console.WriteLine($"Translation failed with {currentService} for {batch.Count} block(s) - empty response");
                                return false;
                            }

                            // Batches finish in any order; apply one response at a time
                            lock (processLock)
                            {
                                ProcessTranslatedJSON(translationResponse, false);
                            }
                            return true;
                        }
                        finally
                        {
                            concurrency.Release();
                        }
                    }

                    Console.WriteLine($"Manga translation: {blocks.Count} block(s) in {batches.Count} request(s), {cachedObjects.Count} from cache");
                    LogTranslationRequest(currentService, currentModel, blocks, batches.Count, cachedObjects.Count);
                    var requestStopwatch = Stopwatch.StartNew();
                    bool[] batchResults = await Task.WhenAll(batches.Select(TranslateBatchAsync));
                    hadSuccessfulTranslation = batchResults.Any(ok => ok) || cachedObjects.Count > 0;
                    OcrTiming.Log("4. Translation received", $"{requestStopwatch.ElapsedMilliseconds} ms");

                    // The model may skip or merge an id; translate whatever is still missing on its own.
                    // These go out in parallel too, within the same concurrency limit.
                    var missing = blocks.Where(t => string.IsNullOrEmpty(t.TextTranslated)).ToList();
                    if (missing.Count > 0)
                    {
                        Console.WriteLine($"Manga translation: retrying {missing.Count} untranslated block(s) one by one");
                        bool[] retryResults = await Task.WhenAll(missing.Select(t => TranslateBatchAsync(new List<TextObject> { t })));
                        hadSuccessfulTranslation |= retryResults.Any(ok => ok);
                        OcrTiming.Log("4b. Missing blocks retried", $"{missing.Count} block(s)");
                    }

                    if (!hadSuccessfulTranslation)
                    {
                        OnboardingTips.OnTranslationResult(currentService, false);
                        OnFinishedThings(true);
                        return;
                    }

                    AddTranslatedTextObjectsToChatBox();
                }
                else
                {
                    var combinedText = string.Join("##|||##", _textObjects.Select(obj => obj.Text));
                    var textsToTranslate = new List<object>
                    {
                        new
                        {
                            id = "999",
                            text = combinedText
                        }
                    };

                    string jsonToTranslate = BuildTranslationRequestJson(textsToTranslate, previousContext, gameInfo);
                    LogManager.Instance.LogLlmRequest(prompt, jsonToTranslate);

                    // Blocks are matched back by position in one combined text, so a partial cache hit
                    // still sends everything
                    LogTranslationRequest(currentService, currentModel, _textObjects, 1, 0);
                    var requestStopwatch = Stopwatch.StartNew();
                    string? translationResponse = await translationService.TranslateAsync(jsonToTranslate, prompt);
                    OcrTiming.Log("4. Translation received", $"{requestStopwatch.ElapsedMilliseconds} ms");

                    if (string.IsNullOrEmpty(translationResponse))
                    {
                        Console.WriteLine($"Translation failed with {currentService} - empty response");
                        OnboardingTips.OnTranslationResult(currentService, false);
                        OnFinishedThings(true);
                        return;
                    }

                    ProcessTranslatedJSON(translationResponse);
                }

                // A response that could not be parsed into any translated text also counts as a failure
                OnboardingTips.OnTranslationResult(currentService, _textObjects.Any(t => !string.IsNullOrEmpty(t.TextTranslated)));

                if (cacheEnabled)
                {
                    foreach (var textObject in _textObjects)
                    {
                        TranslationCache.Instance.Set(textObject.Text, textObject.TextTranslated);
                    }
                }

                _translationStopwatch.Stop();
                Console.WriteLine($"Translation took {_translationStopwatch.ElapsedMilliseconds} ms");
                OcrTiming.Log("5. Translation shown", $"translation step took {_translationStopwatch.ElapsedMilliseconds} ms");
                if (!ConfigManager.Instance.IsAutoOCREnabled())
                {
                    MainWindow.Instance.isStopOCR = true;
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error translating text objects: {ex.Message}");
                OnboardingTips.OnTranslationResult(ConfigManager.Instance.GetCurrentTranslationService(), false);
                OnFinishedThings(true);
            }

            //all done
            OnFinishedThings(true);
        }

        // Set when the user asks to retry, so that one translation goes to the service even if cached
        private bool _bypassTranslationCacheOnce;

        // Settings that change a translation: a cached one is only reused under the same settings
        private string BuildTranslationCacheContext(string service, string model, string prompt, string gameInfo) =>
            string.Join("\u001F", service, model, GetSourceLanguage(), GetTargetLanguage(), prompt, gameInfo);

        // ----- Prefetch: translate new text while waiting for it to settle -----
        // When OCR sees new text the pipeline waits for the next OCR pass to confirm it (the settle time,
        // in practice one OCR cycle, ~0.4-0.5 s). The prefetch sends the translation request right away
        // and only fills the cache; once the text has settled, the normal path finds it there (or waits
        // for the request still in flight). Text that is still changing is never shown, it only costs
        // a request. One prefetch runs at a time.
        private Task? _prefetchTask;
        private List<string>? _prefetchTexts;

        private void StartTranslationPrefetch(JsonElement results)
        {
            try
            {
                if (!ConfigManager.Instance.IsTranslationPrefetchEnabled() ||
                    !ConfigManager.Instance.IsTranslationCacheEnabled() ||
                    !MainWindow.Instance.GetIsStarted() ||
                    !MainWindow.Instance.GetTranslateEnabled() ||
                    GetWaitingForTranslationToFinish() ||
                    _prefetchTask is { IsCompleted: false })
                    return;

                List<string> texts = GetTranslatableTexts(results);
                if (texts.Count == 0)
                    return;

                string service = ConfigManager.Instance.GetCurrentTranslationService();
                string model = ConfigManager.Instance.GetCurrentTranslationModel();
                string gameInfo = ConfigManager.Instance.GetGameInfo();
                string prompt = GetLlmPrompt()
                    .Replace("source_language", MapLanguageCode(GetSourceLanguage()))
                    .Replace("target_language", MapLanguageCode(GetTargetLanguage()));
                string cacheContext = BuildTranslationCacheContext(service, model, prompt, gameInfo);
                TranslationCache.Instance.UseContext(cacheContext);

                if (texts.All(t => TranslationCache.Instance.TryGet(t, out _)))
                    return; // Nothing new to translate

                bool mangaMode = ConfigManager.Instance.IsMangaModeEnabled();
                // A large manga page is split into several requests by the normal path; leave it to that
                if (mangaMode && texts.Count > MangaSingleRequestMaxBlocks)
                    return;

                var previousContext = GetPreviousContext();
                _prefetchTexts = texts;
                _prefetchTask = Task.Run(() => PrefetchTranslationsAsync(texts, mangaMode, prompt, gameInfo, previousContext, cacheContext));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting translation prefetch: {ex.Message}");
            }
        }

        private async Task PrefetchTranslationsAsync(List<string> texts, bool mangaMode, string prompt, string gameInfo,
                                                     List<string> previousContext, string cacheContext)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                ITranslationService translationService = TranslationServiceFactory.CreateService();
                var cache = TranslationCache.Instance;
                int stored = 0;

                if (mangaMode)
                {
                    // Only bubbles not cached yet, each with its own id, as the normal path sends them
                    var missing = texts.Where(t => !cache.TryGet(t, out _)).Distinct().ToList();
                    var payload = missing.Select((t, i) => (object)new { id = "prefetch_" + i, text = t }).ToList();
                    OcrTiming.Log("2a. Prefetch request", $"{missing.Count} block(s)");

                    string? response = await translationService.TranslateAsync(BuildTranslationRequestJson(payload, previousContext, gameInfo), prompt);
                    var translations = string.IsNullOrEmpty(response) ? new Dictionary<string, string>() : ParseTranslationsById(response);
                    for (int i = 0; i < missing.Count; i++)
                    {
                        if (translations.TryGetValue("prefetch_" + i, out string? translated))
                        {
                            cache.SetIfContext(cacheContext, missing[i], FinalizeTranslation(translated, isCombinedPart: false));
                            stored++;
                        }
                    }
                }
                else
                {
                    // One combined block, split back by position exactly like the normal path
                    var payload = new List<object> { new { id = "999", text = string.Join("##|||##", texts) } };
                    OcrTiming.Log("2a. Prefetch request", $"{texts.Count} block(s) combined");

                    string? response = await translationService.TranslateAsync(BuildTranslationRequestJson(payload, previousContext, gameInfo), prompt);
                    var translations = string.IsNullOrEmpty(response) ? new Dictionary<string, string>() : ParseTranslationsById(response);
                    if (translations.TryGetValue("999", out string? combined))
                    {
                        string[] parts = combined.Split("##|||##");
                        // A different number of parts would pair translations with the wrong text
                        if (parts.Length == texts.Count)
                        {
                            for (int i = 0; i < texts.Count; i++)
                            {
                                cache.SetIfContext(cacheContext, texts[i], FinalizeTranslation(parts[i], isCombinedPart: true));
                            }
                            stored = texts.Count;
                        }
                    }
                }

                OcrTiming.Log("2b. Prefetch done", $"{stopwatch.ElapsedMilliseconds} ms, {stored} translation(s) cached");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Translation prefetch failed: {ex.Message}");
            }
        }

        // If a prefetch for exactly the current text is still running, wait for it so its result is
        // used instead of sending the same request again
        private async Task WaitForMatchingPrefetchAsync()
        {
            var task = _prefetchTask;
            var texts = _prefetchTexts;
            if (task == null || task.IsCompleted || texts == null ||
                !texts.SequenceEqual(_textObjects.Select(t => t.Text)))
                return;

            OcrTiming.Log("3a. Waiting for the prefetch of this text");
            try
            {
                await task;
            }
            catch (Exception)
            {
                // A failed prefetch just means the normal request goes out
            }
        }

        private static readonly HashSet<string> RtlLanguages = new HashSet<string> {
            "ar", "arabic", "fa", "farsi", "persian", "he", "hebrew", "ur", "urdu"
        };

        // The same post-processing the normal path applies before it stores a translation, so a
        // prefetched translation is identical to one produced by ProcessTranslatedJSON
        private static string FinalizeTranslation(string translated, bool isCombinedPart)
        {
            if (isCombinedPart)
            {
                translated = System.Text.RegularExpressions.Regex.Replace(translated, @"\|{3}\s*RST\s*\|{3}|#{0,2}\s*\|{3}\s*#{0,2}", "");
            }
            if (RtlLanguages.Contains(ConfigManager.Instance.GetTargetLanguage().ToLower()) && !translated.StartsWith('\u200F'))
            {
                translated = "\u200F" + translated;
            }
            return translated;
        }

        // id -> translated text from a service response, in either response format
        // ("text_blocks" from LLMs, "translations" from Google Translate / Yandex / Microsoft)
        private Dictionary<string, string> ParseTranslationsById(string translationResponse)
        {
            var result = new Dictionary<string, string>();
            try
            {
                var (innerContent, isTranslationsFormat) = ExtractInnerContentFromResponse(translationResponse);
                if (string.IsNullOrEmpty(innerContent))
                    return result;

                using JsonDocument doc = JsonDocument.Parse(innerContent);
                string arrayName = isTranslationsFormat ? "translations" : "text_blocks";
                string textName = isTranslationsFormat ? "translated_text" : "text";
                if (!doc.RootElement.TryGetProperty(arrayName, out JsonElement items) || items.ValueKind != JsonValueKind.Array)
                    return result;

                foreach (JsonElement item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out JsonElement idElement) &&
                        item.TryGetProperty(textName, out JsonElement textElement))
                    {
                        string id = idElement.ValueKind == JsonValueKind.String ? idElement.GetString() ?? "" : idElement.ToString();
                        string text = textElement.GetString() ?? "";
                        if (id.Length > 0 && text.Length > 0)
                            result[id] = text;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not read prefetch response: {ex.Message}");
            }
            return result;
        }

        // Fill text objects from the translation cache; returns the ones that were found
        private HashSet<TextObject> ApplyCachedTranslations()
        {
            var found = new HashSet<TextObject>();
            foreach (var textObject in _textObjects)
            {
                if (TranslationCache.Instance.TryGet(textObject.Text, out string translation))
                {
                    // Cached RTL translations keep the right-to-left mark added when they were first shown.
                    // The char overload is ordinal: StartsWith(string) is culture-sensitive and ignores the
                    // zero-width mark, so it matched every translation and turned all of them right-to-left.
                    textObject.FlowDirection = translation.StartsWith('\u200F') ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                    textObject.TextTranslated = translation;
                    textObject.UpdateUIElement();
                    found.Add(textObject);
                }
            }
            return found;
        }

        private static void LogTranslationRequest(string service, string model, IEnumerable<TextObject> blocks, int requests, int fromCache)
        {
            int chars = blocks.Sum(t => t.Text?.Length ?? 0);
            string modelPart = string.IsNullOrEmpty(model) ? "" : $" ({model})";
            OcrTiming.Log("3. Translation request", $"{service}{modelPart}, {requests} request(s), {chars} chars{(fromCache > 0 ? $", {fromCache} block(s) from cache" : "")}");
        }

        private string BuildTranslationRequestJson(List<object> textBlocks, List<string> previousContext, string gameInfo)
        {
            var ocrData = new
            {
                source_language = MapLanguageCode(GetSourceLanguage()),
                target_language = MapLanguageCode(GetTargetLanguage()),
                text_blocks = textBlocks,
                previous_context = previousContext,
                game_info = gameInfo
            };

            // Compact: indentation only adds input tokens
            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            return JsonSerializer.Serialize(ocrData, jsonOptions);
        }

        public string GetGeminiApiKey()
        {
            return ConfigManager.Instance.GetGeminiApiKey();
        }

        public string GetLlmPrompt()
        {
            return ConfigManager.Instance.GetLlmPrompt();
        }

        private string GetSourceLanguage()
        {
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                return Application.Current.Dispatcher.Invoke(() => GetSourceLanguage());
            }
            // return (MainWindow.Instance.sourceLanguageComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            // Find the MainWindow instance
            // var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            // // Get the selected ComboBoxItem
            // if (mainWindow!.sourceLanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
            // {
            //     // Return the content as string
            //     return selectedItem.Content?.ToString()!;
            // }

            return ConfigManager.Instance.GetSourceLanguage();
        }

        // Get target language from MainWindow (for future implementation)
        private string GetTargetLanguage()
        {
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                return Application.Current.Dispatcher.Invoke(() => GetTargetLanguage());
            }

            // // Find the MainWindow instance
            // var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            // // Get the selected ComboBoxItem
            // if (mainWindow!.targetLanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
            // {
            //     // Return the content as string
            //     return selectedItem.Content?.ToString()!;
            // }
            return ConfigManager.Instance.GetTargetLanguage();
        }

        // Get previous context based on configuration settings
        private List<string> GetPreviousContext()
        {
            // Check if context is enabled
            int maxContextPieces = ConfigManager.Instance.GetMaxContextPieces();
            if (maxContextPieces <= 0)
            {
                return new List<string>(); // Empty list if context is disabled
            }

            int minContextSize = ConfigManager.Instance.GetMinContextSize();

            // Get context from ChatBoxWindow's history
            if (ChatBoxWindow.Instance != null)
            {
                return ChatBoxWindow.Instance.GetRecentOriginalTexts(maxContextPieces, minContextSize);

            }

            return new List<string>();
        }

        /// <summary>
        /// Translates a single text string immediately without OCR pipeline.
        /// Used for clipboard auto-translation feature.
        /// </summary>
        /// <param name="text">Text to translate</param>
        /// <returns>Translated text, or null if translation failed</returns>
        public async Task<string?> TranslateTextImmediateAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                Console.WriteLine($"[ClipboardTranslate] Starting translation of {text.Length} chars");

                // Create the JSON structure for translation
                var textsToTranslate = new List<object>
                {
                    new
                    {
                        id = "clipboard",
                        text = text
                    }
                };

                // Get previous context if enabled
                var previousContext = GetPreviousContext();

                // Get game info if available
                string gameInfo = ConfigManager.Instance.GetGameInfo();

                // Create the full JSON object
                var ocrData = new
                {
                    source_language = MapLanguageCode(GetSourceLanguage()),
                    target_language = MapLanguageCode(GetTargetLanguage()),
                    text_blocks = textsToTranslate,
                    previous_context = previousContext,
                    game_info = gameInfo
                };

                var jsonOptions = new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string jsonToTranslate = JsonSerializer.Serialize(ocrData, jsonOptions);

                // Get the prompt template
                string prompt = GetLlmPrompt();
                prompt = prompt.Replace("source_language", MapLanguageCode(GetSourceLanguage())).Replace("target_language", MapLanguageCode(GetTargetLanguage()));

                // Log the LLM request
                LogManager.Instance.LogLlmRequest(prompt, jsonToTranslate);

                // Create translation service based on current configuration
                ITranslationService translationService = TranslationServiceFactory.CreateService();
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();

                // Call the translation API
                string? translationResponse = await translationService.TranslateAsync(jsonToTranslate, prompt);

                if (string.IsNullOrEmpty(translationResponse))
                {
                    Console.WriteLine($"[ClipboardTranslate] Translation failed with {currentService} - empty response");
                    return null;
                }

                Console.WriteLine($"[ClipboardTranslate] Got response from {currentService}");

                // Parse the translated text from response
                string? translatedText = ExtractTranslatedTextFromResponse(translationResponse);

                return translatedText;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClipboardTranslate] Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts the inner JSON content (text_blocks) from various API response formats.
        /// Reuses the same parsing logic as ProcessTranslatedJSON.
        /// </summary>
        /// <param name="response">Raw API response</param>
        /// <param name="currentService">Current translation service name (optional, will auto-detect if null)</param>
        /// <returns>Tuple of (innerJsonContent, isGoogleTranslate) or (null, false) if extraction failed</returns>
        private (string? content, bool isGoogleTranslate) ExtractInnerContentFromResponse(string response, string? currentService = null)
        {
            try
            {
                currentService ??= ConfigManager.Instance.GetCurrentTranslationService();

                using JsonDocument doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                // ChatGPT format: {"translated_text": "...", ...}
                if (currentService == "ChatGPT")
                {
                    if (root.TryGetProperty("translated_text", out JsonElement translatedTextElement))
                    {
                        string? translatedTextJson = translatedTextElement.GetString();
                        if (!string.IsNullOrEmpty(translatedTextJson) &&
                            translatedTextJson.StartsWith("{") &&
                            translatedTextJson.EndsWith("}"))
                        {
                            return (translatedTextJson, false);
                        }
                    }
                }
                // Gemini/Ollama/LM Studio format: candidates[0].content.parts[0].text
                else if (currentService == "Gemini" || currentService == "Ollama" || currentService == "LM Studio")
                {
                    if (root.TryGetProperty("candidates", out JsonElement candidates) &&
                        candidates.GetArrayLength() > 0)
                    {
                        var firstCandidate = candidates[0];
                        if (firstCandidate.TryGetProperty("content", out var content) &&
                            content.TryGetProperty("parts", out var parts) &&
                            parts.GetArrayLength() > 0)
                        {
                            var text = parts[0].GetProperty("text").GetString();
                            if (text != null && text.Contains("\"text_blocks\""))
                            {
                                int jsonStart = text.IndexOf('{');
                                int jsonEnd = text.LastIndexOf('}');
                                if (jsonStart >= 0 && jsonEnd > jsonStart)
                                {
                                    return (text.Substring(jsonStart, jsonEnd - jsonStart + 1), false);
                                }
                            }
                            // Return raw text if no JSON found
                            return (text, false);
                        }
                    }
                }
                // Mistral/Groq/Custom API format: choices[0].message.content
                else if (currentService == "Mistral" || currentService == "Groq" || currentService == "Custom API")
                {
                    if (root.TryGetProperty("choices", out JsonElement choices) &&
                        choices.GetArrayLength() > 0)
                    {
                        var firstChoice = choices[0];
                        if (firstChoice.TryGetProperty("message", out JsonElement message) &&
                            message.TryGetProperty("content", out JsonElement contentElement))
                        {
                            var content = contentElement.GetString();
                            if (content != null && content.Contains("\"text_blocks\""))
                            {
                                int jsonStart = content.IndexOf('{');
                                int jsonEnd = content.LastIndexOf('}');
                                if (jsonStart >= 0 && jsonEnd > jsonStart)
                                {
                                    return (content.Substring(jsonStart, jsonEnd - jsonStart + 1), false);
                                }
                            }
                            // Return raw content if no JSON found
                            return (content, false);
                        }
                    }
                }
                // Google Translate / Yandex format: {"translations": [...]}
                else if (currentService == "Google Translate" || currentService == "Yandex")
                {
                    if (root.TryGetProperty("translations", out JsonElement _))
                    {
                        return (response, true);
                    }
                }

                // Fallback: check for direct text_blocks or translations in root
                if (root.TryGetProperty("text_blocks", out _))
                {
                    return (response, false);
                }
                if (root.TryGetProperty("translations", out _))
                {
                    return (response, true);
                }

                return (null, false);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error parsing response: {ex.Message}");
                return (null, false);
            }
        }

        /// <summary>
        /// Extracts the translated text from text_blocks JSON.
        /// </summary>
        private string? ExtractTextFromTextBlocks(string jsonContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;

                if (root.TryGetProperty("text_blocks", out var textBlocks) && textBlocks.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in textBlocks.EnumerateArray())
                    {
                        if (block.TryGetProperty("text", out var textElement))
                        {
                            return textElement.GetString();
                        }
                    }
                }
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts the translated text from Google Translate response.
        /// </summary>
        private string? ExtractTextFromGoogleTranslate(string jsonContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;

                if (root.TryGetProperty("translations", out var translations) && translations.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in translations.EnumerateArray())
                    {
                        if (block.TryGetProperty("translated_text", out var translatedTextElement))
                        {
                            return translatedTextElement.GetString();
                        }
                    }
                }
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts the translated text from the JSON response.
        /// Reuses parsing logic from ProcessTranslatedJSON.
        /// </summary>
        private string? ExtractTranslatedTextFromResponse(string response)
        {
            try
            {
                var (content, isGoogleTranslate) = ExtractInnerContentFromResponse(response);

                if (string.IsNullOrEmpty(content))
                {
                    Console.WriteLine("[ClipboardTranslate] Could not extract content from response");
                    return null;
                }

                if (isGoogleTranslate)
                {
                    return ExtractTextFromGoogleTranslate(content);
                }

                // Try to extract from text_blocks
                var result = ExtractTextFromTextBlocks(content);
                if (result != null)
                {
                    return result;
                }

                // If no text_blocks found, return the raw content (might be plain text from LLM)
                return content.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClipboardTranslate] Error: {ex.Message}");
                // Response might be plain text, return as-is
                return response.Trim();
            }
        }

        #region Auto Merge Overlapping Text

        /// <summary>
        /// Temporary class to hold text block data for merging
        /// </summary>
        private class TempTextBlock
        {
            public string Text { get; set; } = "";
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public double Confidence { get; set; }
            // Manga speech bubble the block sits in, null when unknown
            public int? BubbleId { get; set; }
        }

        /// <summary>
        /// Holds original bounding box info for merged blocks
        /// </summary>
        private class OriginalBounds
        {
            public double MinX { get; set; }
            public double MinY { get; set; }
            public double MaxX { get; set; }
            public double MaxY { get; set; }
        }

        /// <summary>
        /// Returns blocks sorted in natural reading order.
        /// Detects vertical (tategaki) CJK text and orders columns right-to-left,
        /// lines top-to-bottom within a column. Otherwise orders top-to-bottom.
        /// </summary>
        private List<TempTextBlock> GetBlocksInReadingOrder(List<TempTextBlock> blocks)
        {
            if (blocks.Count <= 1)
                return new List<TempTextBlock>(blocks);

            // Detect vertical text orientation:
            //  - Source language is East Asian (ja / zh_sim / zh_tra / ko)
            //  - Majority of blocks are taller than wide (Height > Width)
            string sourceLang = ConfigManager.Instance.GetSourceLanguage();
            bool isEastAsian = sourceLang == "ja" ||
                               sourceLang == "ch_sim" ||
                               sourceLang == "ch_tra" ||
                               sourceLang == "ko";

            bool isVertical = false;
            if (isEastAsian)
            {
                int verticalCount = blocks.Count(b => b.Height > b.Width && b.Height > 0);
                isVertical = verticalCount * 2 >= blocks.Count; // >= 50% of blocks
            }

            if (!isVertical)
            {
                // Horizontal text: top-to-bottom by vertical center.
                return blocks
                    .OrderBy(b => b.Y + (b.Height / 2.0))
                    .ToList();
            }

            // Vertical text (tategaki): group blocks into columns by horizontal overlap,
            // order columns right-to-left, and within each column top-to-bottom.
            // Each block is treated as belonging to one column (typical for vertical CJK OCR).
            var byY = blocks
                .OrderBy(b => b.Y + (b.Height / 2.0))
                .ToList();

            var columns = new List<List<TempTextBlock>>();
            foreach (var block in byY)
            {
                double blockCenterX = block.X + (block.Width / 2.0);
                double blockLeft = block.X;
                double blockRight = block.X + block.Width;

                // Find an existing column whose X range overlaps this block.
                List<TempTextBlock>? targetColumn = null;
                foreach (var col in columns)
                {
                    var rep = col[0];
                    double repLeft = rep.X;
                    double repRight = rep.X + rep.Width;
                    // Overlap test on X axis (with small tolerance)
                    bool xOverlap = (blockRight + 5.0 >= repLeft) && (blockLeft - 5.0 <= repRight);
                    if (xOverlap)
                    {
                        targetColumn = col;
                        break;
                    }
                }

                if (targetColumn == null)
                {
                    targetColumn = new List<TempTextBlock>();
                    columns.Add(targetColumn);
                }
                targetColumn.Add(block);
            }

            // Order columns right-to-left (descending by max X), within column top-to-bottom.
            var ordered = new List<TempTextBlock>();
            foreach (var col in columns.OrderByDescending(c => c.Max(b => b.X + b.Width)))
            {
                // Column already sorted by Y above; preserve that order.
                ordered.AddRange(col);
            }
            return ordered;
        }

        /// <summary>
        /// Merges text blocks that physically overlap with each other
        /// Returns tuple of merged blocks and their original bounding boxes
        /// </summary>
        private (List<TempTextBlock> blocks, List<OriginalBounds> originalBounds) MergeOverlappingTextBlocks(List<TempTextBlock> blocks)
        {
            if (blocks.Count <= 1)
            {
                var bounds = blocks.Select(b => new OriginalBounds
                {
                    MinX = b.X,
                    MinY = b.Y,
                    MaxX = b.X + b.Width,
                    MaxY = b.Y + b.Height
                }).ToList();
                return (blocks, bounds);
            }

            // Sort blocks to ensure correct reading order before merging.
            // Two orientation modes are supported:
            //  - Horizontal text (LTR, top-to-bottom lines): sort by CenterY ascending.
            //  - Vertical text (Japanese tategaki / vertical CJK): columns are read
            //    right-to-left, and within a column top-to-bottom.
            //
            // Vertical orientation is detected when the source language is East Asian
            // (ja/zh/ko) AND most blocks are taller than they are wide (Height > Width).
            // This avoids mis-detecting horizontal CJK dialogue boxes as vertical.
            //
            // Using CenterY (instead of Bounds.Y / top) is more robust against OCR jitter
            // and skewed/curved text where the top edge doesn't reflect the true vertical
            // position. This fixes the issue where merged multi-line text came out in the
            // wrong order (e.g. 2-1-3) because the merge order depended on the OCR array order.
            var sortedBlocks = GetBlocksInReadingOrder(blocks);

            var result = new List<TempTextBlock>(sortedBlocks);
            var boundsResult = sortedBlocks.Select(b => new OriginalBounds
            {
                MinX = b.X,
                MinY = b.Y,
                MaxX = b.X + b.Width,
                MaxY = b.Y + b.Height
            }).ToList();
            bool merged;

            do
            {
                merged = false;
                for (int i = 0; i < result.Count; i++)
                {
                    for (int j = i + 1; j < result.Count; j++)
                    {
                        if (DoBlocksOverlap(result[i], result[j]))
                        {
                            // Merge the two blocks
                            var mergedBlock = MergeTwoBlocks(result[i], result[j]);
                            // Combine original bounding boxes
                            var combinedBounds = new OriginalBounds
                            {
                                MinX = Math.Min(boundsResult[i].MinX, boundsResult[j].MinX),
                                MinY = Math.Min(boundsResult[i].MinY, boundsResult[j].MinY),
                                MaxX = Math.Max(boundsResult[i].MaxX, boundsResult[j].MaxX),
                                MaxY = Math.Max(boundsResult[i].MaxY, boundsResult[j].MaxY)
                            };

                            // Remove higher index first to keep lower index valid
                            result.RemoveAt(j);
                            boundsResult.RemoveAt(j);
                            result.RemoveAt(i);
                            boundsResult.RemoveAt(i);

                            result.Add(mergedBlock);
                            boundsResult.Add(combinedBounds);
                            merged = true;
                            break;
                        }
                    }
                    if (merged) break;
                }
            } while (merged);

            return (result, boundsResult);
        }

        /// <summary>
        /// Checks if two text blocks physically overlap (intersect) with each other
        /// </summary>
        private bool DoBlocksOverlap(TempTextBlock a, TempTextBlock b)
        {
            // Skip if either block has zero dimensions
            if (a.Width <= 0 || a.Height <= 0 || b.Width <= 0 || b.Height <= 0)
                return false;

            // Text in two different manga speech bubbles never merges, however close the bubbles are
            if (a.BubbleId != null && b.BubbleId != null && a.BubbleId != b.BubbleId)
                return false;

            // Larger threshold to better detect adjacent/overlapping text blocks
            double overlapThreshold = 5.0; // pixels

            // Calculate the intersection rectangle
            double leftA = a.X;
            double rightA = a.X + a.Width;
            double topA = a.Y;
            double bottomA = a.Y + a.Height;

            double leftB = b.X;
            double rightB = b.X + b.Width;
            double topB = b.Y;
            double bottomB = b.Y + b.Height;

            // Check if rectangles overlap (with threshold for adjacent blocks)
            bool xOverlap = (rightA + overlapThreshold >= leftB) && (leftA - overlapThreshold <= rightB);
            bool yOverlap = (bottomA + overlapThreshold >= topB) && (topA - overlapThreshold <= bottomB);

            return xOverlap && yOverlap;
        }

        /// <summary>
        /// Merges two text blocks into one
        /// </summary>
        private TempTextBlock MergeTwoBlocks(TempTextBlock a, TempTextBlock b)
        {
            // Determine merge order based on vertical center position.
            // CenterY is more stable than the top edge (Y) for skewed/curved text
            // and avoids wrong line ordering when OCR bounding boxes overlap vertically.
            double centerA = a.Y + (a.Height / 2.0);
            double centerB = b.Y + (b.Height / 2.0);
            if (centerA > centerB)
            {
                var temp = a;
                a = b;
                b = temp;
            }

            return new TempTextBlock
            {
                Text = a.Text + " " + b.Text,
                X = Math.Min(a.X, b.X),
                Y = Math.Min(a.Y, b.Y),
                Width = Math.Max(a.X + a.Width, b.X + b.Width) - Math.Min(a.X, b.X),
                Height = Math.Max(a.Y + a.Height, b.Y + b.Height) - Math.Min(a.Y, b.Y),
                Confidence = Math.Min(a.Confidence, b.Confidence),
                BubbleId = a.BubbleId ?? b.BubbleId
            };
        }

        #endregion
    }
}