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
        private string outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MainWindow.DEFAULT_OUTPUT_PATH);
        private Random _random;
        private Grid? _overlayContainer;
        private int _textIDCounter = 0;
        private DateTime _lastOcrRequestTime = DateTime.MinValue;
        private readonly TimeSpan _minOcrInterval = TimeSpan.FromSeconds(0.2);

        private readonly List<string> _audioBatch = new List<string>();
        private readonly object _audioBatchLock = new object();
        private bool _isProcessingAudioBatch = false;
        private bool _hasNewAudioSinceLastTranslation = false;

        private DispatcherTimer _reconnectTimer;
        private string _lastOcrHash = string.Empty;
        private string _lastTextContent = string.Empty;
        // Last OCR text logged as skipped, so a static screen is logged once instead of every frame
        private string _lastSkipLoggedText = string.Empty;

        // Track the current capture position
        private int _currentCaptureX;
        private int _currentCaptureY;
        private DateTime _lastChangeTime = DateTime.MinValue;

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

            // Initialize reconnect timer with 3-second interval
            _reconnectTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _reconnectTimer.Tick += ReconnectTimer_Tick;

            // Subscribe to SocketManager events
            SocketManager.Instance.DataReceived += OnSocketDataReceived;
            SocketManager.Instance.ConnectionChanged += OnSocketConnectionChanged;
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

                // Only connect to socket server if using a server-based OCR (third-party engines removed)
                // Built-in OCR (OneOCR, Windows OCR) doesn't need a socket connection
                Console.WriteLine($"Using {MainWindow.Instance.GetSelectedOcrMethod()} (built-in) - socket connection not needed");

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

        // Connect to socket server
        private async Task ConnectToSocketServerAsync()
        {
            try
            {
                Console.WriteLine("Attempting to connect to socket server...");

                // Check if already connected
                if (SocketManager.Instance.IsConnected)
                {
                    Console.WriteLine("Already connected to socket server");
                    return;
                }

                await SocketManager.Instance.ConnectAsync();

                // Start the reconnect timer if connection failed
                if (!SocketManager.Instance.IsConnected)
                {
                    Console.WriteLine("Connection failed, starting reconnect timer");
                    _reconnectTimer.Start();
                }
                else
                {
                    Console.WriteLine("Successfully connected to socket server");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Socket connection error: {ex.Message}");

                // Start the reconnect timer
                _reconnectTimer.Start();
            }
        }

        // Reconnect timer tick event
        private async void ReconnectTimer_Tick(object? sender, EventArgs e)
        {
            // Third-party Python OCR engines removed; no socket reconnection needed.
            // Keep the timer stopped for built-in OCR (OneOCR, Windows OCR).
            _reconnectTimer.Stop();
            await Task.CompletedTask;
        }

        // Socket data received event handler
        private void OnSocketDataReceived(object? sender, string data)
        {
            LogManager.Instance.LogOcrResponse(data);

            // Process the received data
            ProcessReceivedTextJsonData(data);
        }

        // Socket connection changed event handler
        private void OnSocketConnectionChanged(object? sender, bool isConnected)
        {
            // Third-party Python OCR engines removed; socket connection events are no longer relevant.
            // Built-in OCR (OneOCR, Windows OCR) doesn't use socket connections.
            if (!isConnected)
            {
                SocketManager.Instance._isConnected = false;
            }
            else
            {
                _reconnectTimer.Stop();
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

                    Console.WriteLine("Retrying current translation (bypassing similarity checks).");

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
                                            if (!cleanTranslatedText.StartsWith("\u200F"))
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
                                            if (!translatedText.StartsWith("\u200F"))
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
                                                if (!translatedText.StartsWith("\u200F"))
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
                                double similarity = 0.0;
                                if (settleTime > 0)
                                {
                                    if (hashMatch || IsTextSimilar(textContent, _lastTextContent, similarThreshold, out similarity))
                                    {
                                        if (_lastChangeTime == DateTime.MinValue)
                                        {
                                            LogSimilaritySkip(hashMatch, similarity, similarThreshold, textContent);
                                            OnFinishedThings(true);
                                            return; // Already rendered it, just ignore until it changes again
                                        }
                                        else
                                        {
                                            // Check if we are within the settling time
                                            if ((DateTime.Now - _lastChangeTime).TotalSeconds < settleTime)
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
                                        _lastChangeTime = DateTime.Now;
                                        _lastOcrHash = contentHash;
                                        _lastTextContent = textContent;
                                        _lastSkipLoggedText = string.Empty;

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
                                else if (IsTextSimilar(textContent, _lastTextContent, similarThreshold, out similarity))
                                {
                                    LogSimilaritySkip(false, similarity, similarThreshold, textContent);
                                    OnFinishedThings(true);
                                    return;
                                }
                                // Looks like new stuff
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

        // Check if a text should be ignored based on ignore phrases
        private (bool ShouldIgnore, string FilteredText) ShouldIgnoreText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (true, string.Empty);

            // Get all ignore phrases from ConfigManager
            var ignorePhrases = ConfigManager.Instance.GetIgnorePhrases();

            if (ignorePhrases.Count == 0)
                return (false, text); // No phrases to check, keep the text as is

            string filteredText = text;

            foreach (var (phrase, matchType) in ignorePhrases)
            {
                if (string.IsNullOrEmpty(phrase))
                    continue;

                switch (matchType)
                {
                    case IgnorePhraseMatchType.ExactMatch:
                        if (text.Equals(phrase, StringComparison.OrdinalIgnoreCase))
                            return (true, string.Empty);
                        break;

                    case IgnorePhraseMatchType.Contains:
                        string before = filteredText;
                        filteredText = filteredText.Replace(phrase, "", StringComparison.OrdinalIgnoreCase);
                        break;

                    case IgnorePhraseMatchType.RegularExpression:
                        if (ConfigManager.Instance.TryGetCompiledRegex(phrase, out var regex) && regex != null)
                        {
                            try
                            {
                                if (regex.IsMatch(text))
                                    return (true, string.Empty);
                            }
                            catch (RegexMatchTimeoutException)
                            {
                                Console.WriteLine($"Regex timed out for phrase '{phrase}' on text '{text}'");
                            }
                        }
                        break;
                }
            }

            // Check if after removing non-exact-match phrases, the text is empty or whitespace
            if (string.IsNullOrWhiteSpace(filteredText))
            {
                Console.WriteLine("Ignoring text because it's empty after filtering");
                return (true, string.Empty);
            }

            // Return the filtered text if it changed
            if (filteredText != text)
            {
                //Console.WriteLine($"Text filtered: '{text}' -> '{filteredText}'");
                return (false, filteredText);
            }

            return (false, text);
        }

        // Display OCR results from JSON - processes character-level blocks
        private void DisplayOcrResults(JsonElement root)
        {
            Bitmap? sourceBitmap = null;
            if (ConfigManager.Instance.IsAutoSetOverlayBackground())
            {
                try
                {
                    byte[] imageBytes = File.ReadAllBytes(outputPath);
                    using (var ms = new MemoryStream(imageBytes))
                    {
                        sourceBitmap = new Bitmap(ms);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading background image: {ex.Message}");
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
                            double x = 0, y = 0, width = 0, height = 0;

                            if (item.TryGetProperty("rect", out JsonElement boxElement) &&
                                boxElement.ValueKind == JsonValueKind.Array)
                            {
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

                                    x = minX;
                                    y = minY;
                                    width = maxX - minX;
                                    height = maxY - minY;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error parsing rect: {ex.Message}");
                                }
                            }

                            if (autoMergeEnabled)
                            {
                                tempBlocks.Add(new TempTextBlock { Text = text, X = x, Y = y, Width = width, Height = height, Confidence = confidence });
                            }
                            else
                            {
                                // Get fresh DPI scale from the screen where MonitorWindow is currently displayed
                                // Don't use cached MonitorWindow.Instance.dpiScale as it may be stale
                                DpiHelper.GetCurrentScreenDpi(out double dpiScaleX, out double dpiScaleY);
                                CreateTextObjectAtPosition(text, x / dpiScaleX, y / dpiScaleY, width / dpiScaleX, height / dpiScaleY, confidence, sourceBitmap);
                            }
                        }
                    }

                    // If auto merge is enabled, merge overlapping text blocks
                    if (autoMergeEnabled && tempBlocks.Count > 0)
                    {
                        var (mergedBlocks, blockOriginalBounds) = MergeOverlappingTextBlocks(tempBlocks);
                        // Get fresh DPI scale from the screen where MonitorWindow is currently displayed
                        DpiHelper.GetCurrentScreenDpi(out double dpiScaleX, out double dpiScaleY);

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

                // Create default font size based on height
                int fontSize = 18;  // Default
                if (height > 0)
                {
                    double fontSizeRatio = 0.9;


                    string sourceLanguage = GetSourceLanguage().ToLowerInvariant();
                    if (sourceLanguage == "ja" || sourceLanguage == "ch_sim" || sourceLanguage == "ko")
                    {

                        fontSizeRatio = 0.95;
                    }
                    else if (sourceLanguage == "vi" || sourceLanguage == "th")
                    {

                        fontSizeRatio = 0.85;
                    }


                    fontSize = Math.Max(10, (int)(height * fontSizeRatio));


                    if (width > 0 && text.Length > 0)
                    {
                        double charDensity = text.Length / width;
                        if (charDensity > 0.5)
                        {
                            fontSize = Math.Max(10, (int)(fontSize * 0.9));
                        }
                    }
                }
                Color textColor;
                Color bgColor;
                if (ConfigManager.Instance.IsAutoSetOverlayBackground() && sourceBitmap != null)
                {
                    try
                    {
                        // Get fresh DPI scale for color sampling
                        DpiHelper.GetCurrentScreenDpi(out double dpiScaleX, out double dpiScaleY);

                        int bitmapX = (int)(x * dpiScaleX);
                        int bitmapY = (int)(y * dpiScaleY);
                        int bitmapWidth = Math.Max(1, (int)(width * dpiScaleX));
                        int bitmapHeight = Math.Max(1, (int)(height * dpiScaleY));

                        if (bitmapWidth > 0 && bitmapHeight > 0)
                        {
                            // using sourceBitmap
                            System.Drawing.Color dominantColor = ColorUtils.GetDominantColor(
                                sourceBitmap,
                                bitmapX, bitmapY, bitmapWidth, bitmapHeight);

                            bgColor = ColorUtils.CreateBackgroundColor(dominantColor);
                            textColor = ColorUtils.GetContrastingTextColor(dominantColor);
                        }
                        else
                        {
                            // Fallback if size is invalid
                            textColor = new SolidColorBrush(ConfigManager.Instance.GetOverlayTextColor()).Color;
                            bgColor = new SolidColorBrush(ConfigManager.Instance.GetOverlayBackgroundColor()).Color;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error getting dominant color: {ex.Message}");
                        // Fallback to default colors
                        textColor = new SolidColorBrush(ConfigManager.Instance.GetOverlayTextColor()).Color;
                        bgColor = new SolidColorBrush(ConfigManager.Instance.GetOverlayBackgroundColor()).Color;
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

                // Adjust font size
                if (textObject.UIElement is Border border && border.Child is TextBlock textBlock)
                {
                    textBlock.FontSize = fontSize;
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

                await TranslateTextObjectsAsync();
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
                    await OneOCRManager.Instance.ProcessOneOcrResults(textLines, sourceLanguage);
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

        // Using Windows OCR integration with other OCR
        public async void ProcessWithWindowsOCRIntegration(System.Drawing.Bitmap bitmap, string sourceLanguage, string filePath)
        {
            try
            {
                try
                {
                    // Get the text lines from Windows OCR directly from the bitmap
                    var textLines = await WindowsOCRManager.Instance.GetOcrLinesFromBitmapAsync(bitmap, sourceLanguage);

                    if (textLines.Count > 0)
                    {
                        SendImageToServerOCR(filePath);
                    }
                    else
                    {
                        // Windows OCR didn't find any text
                        Console.WriteLine("Windows OCR integration: No text detected in the image");

                        await WindowsOCRManager.Instance.ProcessWindowsOcrResults(textLines, sourceLanguage);
                    }
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


        // Called when a screenshot is saved (for EasyOCR method)
        public async void SendImageToServerOCR(string filePath)
        {
            // Update Monitor Window with the screenshot

            try
            {
                // Only built-in OCR (OneOCR, Windows OCR) is supported now.
                // Third-party Python OCR engines (EasyOCR, PaddleOCR, RapidOCR) have been removed.
                string ocrMethod = MainWindow.Instance.GetSelectedOcrMethod();
                Console.WriteLine($"Using {ocrMethod} (built-in)");
                // ProcessScreenshot will handle the built-in OCR logic
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing screenshot: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_ErrorProcessingScreenshot"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            // We'll do this after we get a reply
            // MainWindow.Instance.SetOCRCheckIsWanted(true);
        }

        // Called when the application is closing
        public void Finish()
        {
            try
            {
                // Clean up resources
                Console.WriteLine("Logic finalized");

                // Disconnect from socket server
                SocketManager.Instance.Disconnect();

                // Stop the reconnect timer
                _reconnectTimer.Stop();

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

        // Send text data through socket
        public async Task<bool> SendTextDataAsync(string text)
        {
            if (!SocketManager.Instance.IsConnected)
            {
                Console.WriteLine("Cannot send data: Socket not connected");
                return false;
            }

            try
            {
                return await SocketManager.Instance.SendDataAsync(text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending text data: {ex.Message}");
                return false;
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
                                            if (!cleanTranslatedText.StartsWith("\u200F"))
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
                                            if (!translatedText.StartsWith("\u200F"))
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
                                                if (!translatedText.StartsWith("\u200F"))
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

                bool mangaModeEnabled = ConfigManager.Instance.IsMangaModeEnabled();
                bool hadSuccessfulTranslation = false;

                if (mangaModeEnabled)
                {
                    foreach (var textObject in _textObjects)
                    {
                        var singleBlockPayload = new List<object>
                        {
                            new
                            {
                                id = textObject.ID,
                                text = textObject.Text
                            }
                        };

                        string jsonToTranslate = BuildTranslationRequestJson(singleBlockPayload, previousContext, gameInfo);
                        LogManager.Instance.LogLlmRequest(prompt, jsonToTranslate);

                        string? translationResponse = await translationService.TranslateAsync(jsonToTranslate, prompt);
                        if (string.IsNullOrEmpty(translationResponse))
                        {
                            Console.WriteLine($"Translation failed with {currentService} for block {textObject.ID} - empty response");
                            continue;
                        }

                        ProcessTranslatedJSON(translationResponse, false);
                        hadSuccessfulTranslation = true;
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

                    string? translationResponse = await translationService.TranslateAsync(jsonToTranslate, prompt);

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

                _translationStopwatch.Stop();
                Console.WriteLine($"Translation took {_translationStopwatch.ElapsedMilliseconds} ms");
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

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
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
                    WriteIndented = true,
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
                Confidence = Math.Min(a.Confidence, b.Confidence)
            };
        }

        #endregion
    }
}