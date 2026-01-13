using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using System.Diagnostics;
using System.Collections.Generic;
using FlowDirection = System.Windows.FlowDirection;

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
        private System.Threading.Timer? _audioBatchTimer;
        private const int AudioBatchDelayMs = 500; 
        private bool _isProcessingAudioBatch = false;
        private bool _hasNewAudioSinceLastTranslation = false;

        private DispatcherTimer _reconnectTimer;
        private string _lastOcrHash = string.Empty;
        private string _lastTextContent = string.Empty;

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

                // Only connect to socket server if using EasyOCR or PaddleOCR or RapidOCR
                if (MainWindow.Instance.GetSelectedOcrMethod() == "EasyOCR" || MainWindow.Instance.GetSelectedOcrMethod() == "PaddleOCR" || MainWindow.Instance.GetSelectedOcrMethod() == "RapidOCR")
                {
                    await ConnectToSocketServerAsync();
                }
                else
                {
                    Console.WriteLine($"Using {MainWindow.Instance.GetSelectedOcrMethod()} - socket connection not needed");

                    // Update status message in the UI
                    MainWindow.Instance.SetStatus($"Using {MainWindow.Instance.GetSelectedOcrMethod()} (built-in)");
                }
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
                    _reconnectAttempts = 0;
                    _hasShownConnectionErrorMessage = false;
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
                _reconnectAttempts = 0;
                _hasShownConnectionErrorMessage = false;
                _reconnectTimer.Start();
            }
        }

        // Track reconnection attempts
        private int _reconnectAttempts = 0;
        private bool _hasShownConnectionErrorMessage = false;

        // Reconnect timer tick event
        private async void ReconnectTimer_Tick(object? sender, EventArgs e)
        {
            // Only try to reconnect if we're using EasyOCR or PaddleOCR or RapidOCR
            if (MainWindow.Instance.GetSelectedOcrMethod() != "EasyOCR" || MainWindow.Instance.GetSelectedOcrMethod() != "PaddleOCR" || MainWindow.Instance.GetSelectedOcrMethod() != "RapidOCR")
            {
                _reconnectTimer.Stop();
                _reconnectAttempts = 0;
                _hasShownConnectionErrorMessage = false;
                return;
            }

            if (!SocketManager.Instance.IsConnected)
            {
                _reconnectAttempts++;
                await SocketManager.Instance.TryReconnectAsync();

                // Stop the timer if connected
                if (SocketManager.Instance.IsConnected)
                {
                    _reconnectTimer.Stop();
                    _reconnectAttempts = 0;
                    _hasShownConnectionErrorMessage = false;
                }
                // Show error message after several failed attempts (approximately 15 seconds)
                else if (_reconnectAttempts >= 1 && !_hasShownConnectionErrorMessage)
                {
                    _hasShownConnectionErrorMessage = true;
                    string serverUrl = $"localhost:{SocketManager.Instance.GetPort()}";

                    string message = string.Format(
                        LocalizationManager.Instance.Strings["Msg_ServerConnectionError"],
                        serverUrl
                    );

                    MessageBox.Show(
                        message,
                        LocalizationManager.Instance.Strings["Title_ServerConnectionError"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            else
            {
                // Stop the timer if already connected
                _reconnectTimer.Stop();
                _reconnectAttempts = 0;
                _hasShownConnectionErrorMessage = false;
            }
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
            // If not connected and we're using EasyOCR or PaddleOCR, start the reconnect timer
            if (!isConnected && (MainWindow.Instance.GetSelectedOcrMethod() == "EasyOCR" || !isConnected && MainWindow.Instance.GetSelectedOcrMethod() == "PaddleOCR" || !isConnected && MainWindow.Instance.GetSelectedOcrMethod() == "RapidOCR"))
            {
                Console.WriteLine("Connection status changed to disconnected. Starting reconnect timer.");
                SocketManager.Instance._isConnected = false;

                _reconnectTimer.Start();
            }
            else if (isConnected)
            {
                Console.WriteLine("Connection status changed to connected. Stopping reconnect timer.");
                _reconnectTimer.Stop();
                _reconnectAttempts = 0;
                _hasShownConnectionErrorMessage = false;
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
            _lastChangeTime = DateTime.Now;
        }


        // Process Google Translate JSON response
        private void ProcessGoogleTranslateJson(JsonElement rootElement)
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
                                    string[] translatedParts = translatedText.Split("##RST##");

                                    // Assign each part to the corresponding text object
                                    for (int j = 0; j < Math.Min(translatedParts.Length, _textObjects.Count); j++)
                                    {
                                        // Clean up the translated text - remove any remaining separators that might have been part of the translation
                                        // Use regex to handle all variations of the separator with different spacing
                                        string cleanTranslatedText = System.Text.RegularExpressions.Regex.Replace(
                                            translatedParts[j],
                                            @"\#{2}\s*RST\s*\#{2}",
                                            ""
                                        );
                                        // Also clean up any potential fragments
                                        cleanTranslatedText = cleanTranslatedText.Replace("RST", "");
                                        cleanTranslatedText = cleanTranslatedText.Replace("##", "");

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

                    // Add translations to chatbox
                    // Sort text objects by Y coordinate
                    var sortedTextObjects = _textObjects.OrderBy(t => t.Y).ToList();

                    // Add each translated text to the ChatBox
                    foreach (var textObject in sortedTextObjects)
                    {
                        string originalText = textObject.Text;
                        string translatedText = textObject.TextTranslated;

                        // Only add to chatbox if we have both texts and translation is not empty
                        if (!string.IsNullOrEmpty(originalText) && !string.IsNullOrEmpty(translatedText))
                        {
                            // Add to TranslationCompleted, this will add it to the chatbox also
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

                                // Handle settle time if enabled
                                double settleTime = ConfigManager.Instance.GetBlockDetectionSettleTime();
                                if (settleTime > 0)
                                {
                                    if (contentHash == _lastOcrHash || IsTextSimilar(textContent, _lastTextContent, Convert.ToDouble(ConfigManager.Instance.GetTextSimilarThreshold())))
                                    {
                                        if (_lastChangeTime == DateTime.MinValue)
                                        {
                                            Console.WriteLine("Content is similar to previous, skipping translation");
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
                                else if (IsTextSimilar(textContent, _lastTextContent, Convert.ToDouble(ConfigManager.Instance.GetTextSimilarThreshold())))
                                {
                                    Console.WriteLine("Content is similar to previous, skipping translation");
                                    OnFinishedThings(true);
                                    return;
                                }
                                // Looks like new stuff
                                _lastOcrHash = contentHash;
                                _lastTextContent = textContent;
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
                                            // Only add to chat history if translation is disabled
                                            _lastChangeTime = DateTime.MinValue;
                                            MainWindow.Instance.AddTranslationToHistory(combinedText, "");

                                            if (ChatBoxWindow.Instance != null)
                                            {
                                                ChatBoxWindow.Instance.OnTranslationWasAdded(combinedText, "");
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
        /// Determines if two text strings are similar based on multiple similarity metrics
        /// </summary>
        /// <param name="s1">First text string to compare</param>
        /// <param name="s2">Second text string to compare</param>
        /// <param name="threshold">Similarity threshold (0.0-1.0) to consider texts as similar</param>
        /// <returns>True if texts are considered similar, false otherwise</returns>
        private bool IsTextSimilar(string s1, string s2, double threshold = 0.7)
        {
            // Handle special cases
            if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return true;
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return false;
            if (s1 == s2) return true;

            // For very short strings, use exact matching with trimming
            if (s1.Length < 5 || s2.Length < 5)
            {
                return s1.Trim().Equals(s2.Trim(), StringComparison.OrdinalIgnoreCase);
            }

            // Calculate similarity using different metrics
            double keywordSim = KeywordSimilarity(s1, s2);
            double diceSim = DiceCoefficient(s1, s2);
            double wordOverlapSim = WordOverlapSimilarity(s1, s2);

            // Use the maximum similarity score from all methods
            double maxSimilarity = Math.Max(Math.Max(keywordSim, diceSim), wordOverlapSim);

            // Debug similarity scores if needed
            // Console.WriteLine($"Similarity between '{s1}' and '{s2}': Keyword={keywordSim:F2}, Dice={diceSim:F2}, WordOverlap={wordOverlapSim:F2}, Max={maxSimilarity:F2}");

            // Return true if any similarity metric exceeds the threshold
            return maxSimilarity >= threshold;
        }

        /// <summary>
        /// Method to calculate the similarity between two strings using the Dice coefficient
        /// </summary>
        private double CombinedAsianLanguageSimilarity(string s1, string s2)
        {
            // Compare the strings using the Dice coefficient
            int commonChars = 0;
            HashSet<char> chars1 = new HashSet<char>(s1);
            HashSet<char> chars2 = new HashSet<char>(s2);

            foreach (char c in chars1)
            {
                if (chars2.Contains(c))
                {
                    commonChars++;
                }
            }

            double characterSimilarity = chars1.Count > 0 && chars2.Count > 0
                ? (double)commonChars / Math.Max(chars1.Count, chars2.Count)
                : 0;

            double ngramSimilarity = CalculateNgramSimilarity(s1, s2, 3);

            // Compare the strings base on length ratio
            double lengthRatio = Math.Min(s1.Length, s2.Length) / (double)Math.Max(s1.Length, s2.Length);

            // Combine the similarity scores
            double charWeight = 0.4;
            double ngramWeight = 0.5;
            double lengthWeight = 0.1;

            // Calculate the combined similarity score
            return (characterSimilarity * charWeight) +
                (ngramSimilarity * ngramWeight) +
                (lengthRatio * lengthWeight);
        }


        private double CalculateNgramSimilarity(string s1, string s2, int n)
        {
            // If the length of either string is less than n, reduce n to the length of the shorter string
            if (s1.Length < n || s2.Length < n)
            {
                n = Math.Min(s1.Length, s2.Length);
                if (n == 0) return 0;
            }

            // Create a HashSet to store the n-grams of each string
            var ngrams1 = new HashSet<string>();
            var ngrams2 = new HashSet<string>();

            // Create n-grams for the first string
            for (int i = 0; i <= s1.Length - n; i++)
            {
                ngrams1.Add(s1.Substring(i, n));
            }

            // Create n-grams for the second string
            for (int i = 0; i <= s2.Length - n; i++)
            {
                ngrams2.Add(s2.Substring(i, n));
            }

            // Count the number of common n-grams
            int intersectionCount = 0;
            foreach (var ngram in ngrams1)
            {
                if (ngrams2.Contains(ngram))
                {
                    intersectionCount++;
                }
            }

            // Calculate the Dice coefficient
            return ngrams1.Count > 0 && ngrams2.Count > 0
                ? (2.0 * intersectionCount) / (ngrams1.Count + ngrams2.Count)
                : 0;
        }

        /// <summary>
        /// Calculate the similarity between two strings based on their keywords
        /// </summary>
        private double KeywordSimilarity(string s1, string s2)
        {
            // Special case: if both strings are empty, they are considered similar
            if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 1.0;
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;
            if (s1 == s2) return 1.0;

            // Get stop words for the current language
            HashSet<string> stopWords = GetStopWordsForCurrentLanguage();

            // Separate the strings into words and filter out stop words in one pass
            var keywords1 = new HashSet<string>(
                s1.Split(new char[] { ' ', ',', '.', '!', '?', ';', ':', '-', '\n', '\r', '\t' },
                    StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.ToLowerInvariant())
                    .Where(w => !stopWords.Contains(w))
            );

            var keywords2 = new HashSet<string>(
                s2.Split(new char[] { ' ', ',', '.', '!', '?', ';', ':', '-', '\n', '\r', '\t' },
                    StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.ToLowerInvariant())
                    .Where(w => !stopWords.Contains(w))
            );

            // If either set is empty, use Dice coefficient on the original strings
            if (keywords1.Count == 0 || keywords2.Count == 0)
            {
                return DiceCoefficient(s1, s2);
            }

            // Calculate intersection size efficiently using LINQ
            int commonKeywords = keywords1.Count(keyword => keywords2.Contains(keyword));

            // Calculate the Dice coefficient: 2*|X∩Y|/(|X|+|Y|)
            return (2.0 * commonKeywords) / (keywords1.Count + keywords2.Count);
        }

        /// <summary>
        /// Get stop words for the current language
        /// </summary>
        private HashSet<string> GetStopWordsForCurrentLanguage()
        {
            // Get the current language code
            string language = GetSourceLanguage().ToLowerInvariant();

            // Return appropriate stop words based on language
            return language switch
            {
                "ja" => new HashSet<string> {
                    "の", "に", "は", "を", "た", "が", "で", "て", "と", "し", "れ", "さ", "ある", "いる",
                    "も", "する", "から", "な", "こと", "として", "い", "や", "れる", "など", "なっ", "ない",
                    "この", "ため", "その", "あっ", "よう", "また", "もの", "という", "あり", "まで", "られ",
                    "なる", "へ", "か", "だ", "これ", "によって", "により", "おり", "より", "による", "ず",
                    "なり", "られる", "において", "ば", "なかっ", "なく", "しかし", "について", "せ", "だっ",
                    "その後", "できる", "それ", "う", "ので", "なお", "のみ", "でき", "き", "つ", "における",
                    "および", "いう", "さらに", "でも", "ら", "たり", "その他", "に関する", "たち", "ます",
                    "ん", "なら", "に対して", "特に", "せる", "及び", "これら", "とき", "では", "にて", "ほか",
                    "ながら", "うち", "そして", "とともに", "ただし", "かつて", "それぞれ", "または", "お",
                    "ほど", "ものの", "に対する", "ほとんど", "と共に", "といった", "です", "とも", "ところ", "ここ"
                },
                "ch_sim" => new HashSet<string> {
                    "的", "了", "和", "是", "就", "都", "而", "及", "與", "著", "或", "一個", "沒有",
                    "我們", "你們", "他們", "她們", "自己", "其中", "之後", "什麼", "一些", "這個", "那個",
                    "這些", "那些", "每個", "各自", "的話", "一樣", "不同", "因此", "因為", "所以", "如果",
                    "但是", "不過", "只是", "除了", "以及", "然後", "現在", "曾經", "已經", "一直", "將來",
                    "一定", "可能", "應該", "需要", "不能", "可以", "不要", "不會", "那麼", "如何", "為何",
                    "怎樣", "哪裡", "誰", "什麼", "為什麼", "多少", "幾時", "如何", "怎樣", "哪裡", "從哪裡", "到哪裡"
                },
                "ko" => new HashSet<string> {
                    "이", "그", "저", "것", "수", "등", "들", "및", "에서", "그리고", "그러나", "그런데",
                    "그래서", "또는", "혹은", "그러므로", "따라서", "하지만", "또한", "에게", "의해", "때문에",
                    "을", "를", "이", "가", "에", "에게", "께", "한테", "더러", "에서", "에게서", "한테서",
                    "로", "으로", "와", "과", "랑", "이랑", "하고", "처럼", "만큼", "보다", "같이", "도",
                    "만", "부터", "까지", "마저", "조차", "커녕", "은", "는", "이", "가", "을", "를",
                    "의", "로서", "로써", "서", "에서", "께서"
                },
                "vi" => new HashSet<string> {
                    "và", "của", "cho", "trong", "là", "với", "có", "được", "tại", "những", "để",
                    "các", "đến", "về", "không", "này", "như", "từ", "một", "người", "ra", "thì",
                    "bị", "đã", "sẽ", "đang", "nên", "cần", "vì", "khi", "nếu", "cũng", "nhưng",
                    "mà", "còn", "phải", "trên", "dưới", "theo", "do", "vào", "lúc", "sau", "rồi",
                    "đó", "nào", "thế", "vậy", "tôi", "bạn", "anh", "chị", "ông", "bà", "họ",
                    "chúng", "ta", "mình", "làm", "biết", "đi", "thấy", "muốn", "nói", "nhìn",
                    "thích", "cảm", "yêu", "ghét", "sợ", "buồn", "vui", "giận", "mệt", "đói",
                    "khát", "ngủ", "dậy", "chạy", "đứng", "ngồi", "nằm"
                },
                _ => new HashSet<string> {
                    "a", "an", "the", "and", "or", "but", "is", "are", "was", "were", "be",
                    "been", "being", "in", "on", "at", "to", "for", "with", "by", "about",
                    "against", "between", "into", "through", "during", "before", "after",
                    "above", "below", "from", "up", "down", "of", "off", "over", "under",
                    "again", "further", "then", "once", "here", "there", "when", "where",
                    "why", "how", "all", "any", "both", "each", "few", "more", "most",
                    "other", "some", "such", "no", "nor", "not", "only", "own", "same",
                    "so", "than", "too", "very", "can", "will", "just", "should", "now",
                    "i", "me", "my", "myself", "we", "our", "ours", "ourselves", "you",
                    "your", "yours", "yourself", "yourselves", "he", "him", "his", "himself",
                    "she", "her", "hers", "herself", "it", "its", "itself", "they", "them",
                    "their", "theirs", "themselves", "what", "which", "who", "whom", "this",
                    "that", "these", "those", "am", "have", "has", "had", "do", "does",
                    "did", "doing", "would", "could", "should", "ought"
                }
            };
        }

        /// <summary>
        /// Calculate the similarity between two strings using the Dice coefficient
        /// </summary>
        private double DiceCoefficient(string s1, string s2)
        {
            // Special case: if both strings are empty, they are considered similar
            if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 1.0;
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;
            if (s1 == s2) return 1.0;

            // if string is too short to create bigrams, compare directly
            if (s1.Length < 2 || s2.Length < 2)
            {
                int sameChars = 0;
                for (int i = 0; i < s1.Length; i++)
                {
                    if (s2.Contains(s1[i])) sameChars++;
                }
                return (double)sameChars / Math.Max(s1.Length, s2.Length);
            }

            // Create sets of bigrams for each string
            var bigrams1 = new HashSet<string>();
            var bigrams2 = new HashSet<string>();


            for (int i = 0; i < s1.Length - 1; i++)
            {
                bigrams1.Add(s1.Substring(i, 2));
            }


            for (int i = 0; i < s2.Length - 1; i++)
            {
                bigrams2.Add(s2.Substring(i, 2));
            }

            // Count the number of common bigrams
            int intersectionCount = 0;
            foreach (var bigram in bigrams1)
            {
                if (bigrams2.Contains(bigram))
                {
                    intersectionCount++;
                }
            }

            // Calculate the Dice coefficient
            return (2.0 * intersectionCount) / (bigrams1.Count + bigrams2.Count);
        }

        /// <summary>
        /// Calculate the similarity between two strings using the Jaccard index
        /// </summary>
        private double WordOverlapSimilarity(string s1, string s2)
        {
            // Special case: if both strings are empty, they are considered similar
            if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 1.0;
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;
            if (s1 == s2) return 1.0;

            // Separate the strings into words
            string[] words1 = s1.Split(new char[] { ' ', ',', '.', '!', '?', ';', ':', '-', '\n', '\r', '\t' },
                StringSplitOptions.RemoveEmptyEntries);
            string[] words2 = s2.Split(new char[] { ' ', ',', '.', '!', '?', ';', ':', '-', '\n', '\r', '\t' },
                StringSplitOptions.RemoveEmptyEntries);


            if (words1.Length == 0 || words2.Length == 0) return 0.0;

            // Transform words to lowercase and create sets of unique words
            var wordSet1 = new HashSet<string>(words1.Select(w => w.ToLowerInvariant()));
            var wordSet2 = new HashSet<string>(words2.Select(w => w.ToLowerInvariant()));

            // Count the number of common words
            int commonWords = 0;
            foreach (var word in wordSet1)
            {
                if (wordSet2.Contains(word))
                {
                    commonWords++;
                }
            }

            // Calculate the Jaccard index
            return (double)commonWords / (wordSet1.Count + wordSet2.Count - commonWords);
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

            //Console.WriteLine($"Checking text '{text}' against {ignorePhrases.Count} ignore phrases");

            foreach (var (phrase, exactMatch) in ignorePhrases)
            {
                if (string.IsNullOrEmpty(phrase))
                    continue;

                if (exactMatch)
                {
                    // Check for exact match
                    if (text.Equals(phrase, StringComparison.OrdinalIgnoreCase))
                    {
                        //Console.WriteLine($"Ignoring text due to exact match: '{phrase}'");
                        return (true, string.Empty);
                    }
                }
                else
                {
                    // Remove the phrase from the text
                    string before = filteredText;
                    filteredText = filteredText.Replace(phrase, "", StringComparison.OrdinalIgnoreCase);

                    if (before != filteredText)
                    {
                        //Console.WriteLine($"Applied non-exact match filter: '{phrase}' removed from text");
                    }
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

                    // Process text blocks that have already been grouped by CharacterBlockDetectionManager
                    int resultCount = resultsElement.GetArrayLength();

                    for (int i = 0; i < resultCount; i++)
                    {
                        JsonElement item = resultsElement[i];

                        if (item.TryGetProperty("text", out JsonElement textElement) &&
                            item.TryGetProperty("confidence", out JsonElement confElement))
                        {
                            // Get the text and ensure it's properly decoded from Unicode
                            string text = textElement.GetString() ?? "";

                            // Skip if text is smaller than minimum fragment size
                            if (text.Length < minTextFragmentSize)
                            {
                                continue;
                            }

                            // Note: We no longer need to filter ignore phrases here
                            // as it's now done earlier in ProcessReceivedTextJsonData before hash generation


                            double confidence = confElement.GetDouble();

                            // Extract bounding box coordinates if available
                            double x = 0, y = 0, width = 0, height = 0;

                            // Check for "rect" property (polygon points format)
                            if (item.TryGetProperty("rect", out JsonElement boxElement) &&
                                boxElement.ValueKind == JsonValueKind.Array)
                            {
                                try
                                {
                                    // Format: [[x1,y1], [x2,y2], [x3,y3], [x4,y4]]
                                    // Calculate bounding box from polygon points
                                    double minX = double.MaxValue, minY = double.MaxValue;
                                    double maxX = double.MinValue, maxY = double.MinValue;

                                    // Iterate through each point
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

                                    // Set coordinates to the calculated bounding box
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

                            // Handle dpiscale for multi monitor
                            double dpiScale = MonitorWindow.Instance.dpiScale;
                            CreateTextObjectAtPosition(text, x, y, width / dpiScale, height / dpiScale, confidence, sourceBitmap);

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


                    fontSize = Math.Max(10, Math.Min(36, (int)(height * fontSizeRatio)));


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
                        double dpiScale = MonitorWindow.Instance.dpiScale;

                        int bitmapX = (int)(x * dpiScale);
                        int bitmapY = (int)(y * dpiScale);
                        int bitmapWidth = Math.Max(1, (int)(width * dpiScale));
                        int bitmapHeight = Math.Max(1, (int)(height * dpiScale));

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
            int[,] d = new int[s1.Length + 1, s2.Length + 1];
            
            for (int i = 0; i <= s1.Length; i++)
                d[i, 0] = i;
            for (int j = 0; j <= s2.Length; j++)
                d[0, j] = j;
            
            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost
                    );
                }
            }
            
            return d[s1.Length, s2.Length];
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

                if (_audioBatch.Count >= 5)
                {
                    Console.WriteLine("[FORCE] Batch size reached 10, processing immediately");
                    _audioBatchTimer?.Dispose();
                    ProcessAudioBatchCallback(null);
                    return; 
                }
                
                _audioBatchTimer?.Dispose();
                _audioBatchTimer = new System.Threading.Timer(
                    ProcessAudioBatchCallback,
                    null,
                    AudioBatchDelayMs,
                    System.Threading.Timeout.Infinite
                );
            }
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
                lock (_audioBatchLock)
                {
                    _isProcessingAudioBatch = false;
                }
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
                // Check if we're using Windows OCR or EasyOCR or PaddleOCR
                string ocrMethod = MainWindow.Instance.GetSelectedOcrMethod();

                if (ocrMethod == "Windows OCR" || ocrMethod == "OneOCR")
                {
                    // Windows OCR doesn't require socket connection
                    Console.WriteLine($"Using {ocrMethod} (built-in)");
                    // ProcessScreenshot will handle the Windows OCR logic
                }
                else
                {
                    if (SocketManager.Instance.IsWaitingForSomething())
                    {
                        Console.WriteLine("Waiting for socket to connect to backend...");
                        MainWindow.Instance.SetOCRCheckIsWanted(true);
                        return;
                    }

                    // Get the source language from MainWindow
                    string sourceLanguage = GetSourceLanguage()!;

                    Console.WriteLine($"Processing screenshot with {ocrMethod} character-level OCR, language: {sourceLanguage}");

                    // Check socket connection for EasyOCR or PaddleOCR
                    if (!SocketManager.Instance.IsConnected)
                    {
                        Console.WriteLine("Socket not connected, attempting to reconnect...");

                        // Try to reconnect
                        bool reconnected = await SocketManager.Instance.TryReconnectAsync();

                        // Wait 300 ms
                        await Task.Delay(300);

                        // Check if reconnection succeeded
                        if (!reconnected || !SocketManager.Instance.IsConnected)
                        {
                            Console.WriteLine($"Reconnection failed, cannot perform OCR with {ocrMethod}");

                            // Make sure the reconnect timer is running to keep trying
                            if (!_reconnectTimer.IsEnabled)
                            {
                                Console.WriteLine("Starting reconnect timer after failed immediate reconnection");
                                _reconnectAttempts = 0;
                                _hasShownConnectionErrorMessage = false;
                                _reconnectTimer.Start();
                            }

                            MainWindow.Instance.SetOCRCheckIsWanted(true);
                            return;
                        }
                        else
                        {
                            Console.WriteLine("Successfully reconnected to socket server");
                        }
                    }
                    if (DateTime.Now - _lastOcrRequestTime < _minOcrInterval)
                    {
                        Console.WriteLine($"Throttling OCR request, too soon after last request");
                        MainWindow.Instance.SetOCRCheckIsWanted(true);
                        return;
                    }

                    _lastOcrRequestTime = DateTime.Now;
                    bool charLevel = ConfigManager.Instance.IsCharLevelEnabled();
                    // If we got here, socket is connected - explicitly request character-level OCR
                    await SocketManager.Instance.SendDataAsync($"read_image|{sourceLanguage}|{ocrMethod}|{charLevel}|{ConfigManager.Instance.IsHDRSupportEnabled()}");
                }
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
                // Cleanup audio batch timer
                _audioBatchTimer?.Dispose();
                _audioBatchTimer = null;
                
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
        private void ProcessStructuredJsonTranslation(JsonElement translatedRoot)
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
                                    string[] translatedParts = translatedText.Split("##RST##");

                                    // Assign each part to the corresponding text object
                                    for (int j = 0; j < Math.Min(translatedParts.Length, _textObjects.Count); j++)
                                    {
                                        // Clean up the translated text - remove any remaining separators that might have been part of the translation
                                        // Use regex to handle all variations of the separator with different spacing
                                        string cleanTranslatedText = System.Text.RegularExpressions.Regex.Replace(
                                            translatedParts[j],
                                            @"\|{3}\s*RST\s*\|{3}",
                                            ""
                                        );
                                        // Also clean up any potential fragments
                                        cleanTranslatedText = cleanTranslatedText.Replace("RST", "");
                                        cleanTranslatedText = cleanTranslatedText.Replace("##", "");

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

            // Sort text objects by Y coordinate
            var sortedTextObjects = _textObjects.OrderBy(t => t.Y).ToList();
            // Add each translated text to the ChatBox
            foreach (var textObject in sortedTextObjects)
            {
                string originalText = textObject.Text;
                string translatedText = textObject.TextTranslated; // Assuming translation is done in-place

                // Only add to chatbox if we have both texts and translation is not empty
                if (!string.IsNullOrEmpty(originalText) && !string.IsNullOrEmpty(translatedText))
                {
                    // Add to TranslationCompleted, this will add it to the chatbox also
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

        //!Process the finished translation into text blocks and the chatbox
        void ProcessTranslatedJSON(string translationResponse)
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
                    Console.WriteLine("Google Translate response detected");
                    using JsonDocument doc = JsonDocument.Parse(innerContent);
                    ProcessGoogleTranslateJson(doc.RootElement);
                    return;
                }

                // Parse inner content and process structured JSON
                try
                {
                    using JsonDocument translatedDoc = JsonDocument.Parse(innerContent);
                    ProcessStructuredJsonTranslation(translatedDoc.RootElement);
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
                "si" => "Sinhala",
                "da" => "Danish",
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

                // Get API key
                // string apiKey = GetGeminiApiKey();
                // if (string.IsNullOrEmpty(apiKey))
                // {
                //     Console.WriteLine("Gemini API key not set, cannot translate");
                //     return;
                // }


                // Combine all texts into one string with a separator
                var combinedText = string.Join("##RST##", _textObjects.Select(obj => obj.Text));
                var textsToTranslate = new List<object>();
                // if(!ConfigManager.Instance.IsMangaModeEnabled())
                // {
                // Create a single text block with ID=999 and the combined text
                textsToTranslate = new List<object>
                {
                    new
                    {
                        id = "999",
                        text = combinedText
                    }
                };
                // }
                // else
                // {
                //     // textsToTranslate = new List<object>();
                //     for (int i = 0; i < _textObjects.Count; i++)
                //     {
                //         var textObj = _textObjects[i];
                //         textsToTranslate.Add(new
                //         {
                //             id = textObj.ID,
                //             text = textObj.Text
                //         });
                //     }
                // }

                // Get previous context if enabled
                var previousContext = GetPreviousContext();

                // Get game info if available
                string gameInfo = ConfigManager.Instance.GetGameInfo();

                // Create the full JSON object with OCR results, context and game info
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

                _translationStopwatch.Restart();

                SetWaitingForTranslationToFinish(true);

                // Create translation service based on current configuration
                ITranslationService translationService = TranslationServiceFactory.CreateService();
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();

                // Call the translation API with the modified prompt if context exists
                string? translationResponse = await translationService.TranslateAsync(jsonToTranslate, prompt);

                if (string.IsNullOrEmpty(translationResponse))
                {
                    Console.WriteLine($"Translation failed with {currentService} - empty response");
                    OnFinishedThings(true);
                    return;
                }

                _translationStopwatch.Stop();
                Console.WriteLine($"Translation took {_translationStopwatch.ElapsedMilliseconds} ms");

                // We've already logged the raw LLM response in the respective service
                // This would log the post-processed response, which we don't need
                // LogManager.Instance.LogLlmReply(translationResponse);

                ProcessTranslatedJSON(translationResponse);
                if (!ConfigManager.Instance.IsAutoOCREnabled())
                {
                    MainWindow.Instance.isStopOCR = true;
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error translating text objects: {ex.Message}");
                OnFinishedThings(true);
            }

            //all done
            OnFinishedThings(true);
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
                // Google Translate format: {"translations": [...]}
                else if (currentService == "Google Translate")
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
    }
}