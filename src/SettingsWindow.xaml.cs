using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Navigation;
using System.Windows.Media.Animation;
using System.Diagnostics;
using System.Collections.ObjectModel;
using ComboBox = System.Windows.Controls.ComboBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using MessageBox = System.Windows.MessageBox;

namespace RSTGameTranslation
{
    // Class to represent an ignore phrase
    public class IgnorePhrase
    {
        public string Phrase { get; set; } = string.Empty;
        public bool ExactMatch { get; set; } = true;
        
        public IgnorePhrase(string phrase, bool exactMatch)
        {
            Phrase = phrase;
            ExactMatch = exactMatch;
        }
    }

    public partial class SettingsWindow : Window
    {
        private static SettingsWindow? _instance;
        
        public static bool _isLanguagePackInstall = false;

        public static SettingsWindow Instance
        {
            get
            {
                if (_instance == null || !IsWindowValid(_instance))
                {
                    _instance = new SettingsWindow();
                }
                return _instance;
            }
        }

        public SettingsWindow()
        {
            // Make sure the initialization flag is set before anything else
            _isInitializing = true;
            Console.WriteLine("SettingsWindow constructor: Setting _isInitializing to true");

            InitializeComponent();
            _instance = this;

            // Add Loaded event handler to ensure controls are initialized
            this.Loaded += SettingsWindow_Loaded;

            // Set up closing behavior (hide instead of close)
            this.Closing += (s, e) =>
            {
                e.Cancel = true;  // Cancel the close
                this.Hide();      // Just hide the window
                MainWindow.Instance.settingsButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(108, 117, 125));
            };
        }

        // Flag to prevent saving during initialization
        private static bool _isInitializing = true;

        // Collection to hold the ignore phrases
        private ObservableCollection<IgnorePhrase> _ignorePhrases = new ObservableCollection<IgnorePhrase>();

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Console.WriteLine("SettingsWindow_Loaded: Starting initialization");

                // Set initialization flag to prevent saving during setup
                _isInitializing = true;

                // Make sure keyboard shortcuts work from this window too
                PreviewKeyDown -= Application_KeyDown;
                PreviewKeyDown += Application_KeyDown;

                // Set initial values only after the window is fully loaded
                LoadSettingsFromMainWindow();

                // Make sure service-specific settings are properly initialized
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();
                UpdateServiceSpecificSettings(currentService);

                // Make sure button check language package are properly initialize
                string currentOcr = ConfigManager.Instance.GetOcrMethod();
                if (currentOcr != "Windows OCR")
                {
                    checkLanguagePack.Visibility = Visibility.Collapsed;
                    checkLanguagePackButton.Visibility = Visibility.Collapsed;
                }

                // Now that initialization is complete, allow saving changes
                _isInitializing = false;

                // Force the OCR method and translation service to match the config again
                // This ensures the config values are preserved and not overwritten
                string configOcrMethod = ConfigManager.Instance.GetOcrMethod();
                string configTransService = ConfigManager.Instance.GetCurrentTranslationService();
                Console.WriteLine($"Ensuring config values are preserved: OCR={configOcrMethod}, Translation={configTransService}");

                ConfigManager.Instance.SetOcrMethod(configOcrMethod);
                ConfigManager.Instance.SetTranslationService(configTransService);

                Console.WriteLine("Settings window fully loaded and initialized. Changes will now be saved.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing Settings window: {ex.Message}");
                _isInitializing = false; // Ensure we don't get stuck in initialization mode
            }
        }

        private void ApiKeyPasswordBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (sender is PasswordBox passwordBox)
                {
                    string apiKey = passwordBox.Password.Trim();
                    string serviceType = ConfigManager.Instance.GetCurrentTranslationService();

                    if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(serviceType))
                    {
                        // Add Api key to list
                        ConfigManager.Instance.AddApiKey(serviceType, apiKey);

                        // Clear textbox content
                        passwordBox.Password = "";

                        Console.WriteLine($"Added new API key for {serviceType}");


                        MessageBox.Show($"API key added for {serviceType}.", "API Key Added",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        private void ViewApiKeysButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button)
            {
                string serviceType = ConfigManager.Instance.GetCurrentTranslationService();

                if (!string.IsNullOrEmpty(serviceType))
                {
                    // Get list api key
                    List<string> apiKeys = ConfigManager.Instance.GetApiKeysList(serviceType);

                    // Show API keys management window
                    ApiKeysWindow apiKeysWindow = new ApiKeysWindow(serviceType, apiKeys);
                    apiKeysWindow.Owner = this;
                    apiKeysWindow.ShowDialog();
                }
            }
        }
        // Handler for application-level keyboard shortcuts
        private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Forward to the central keyboard shortcuts handler
            KeyboardShortcuts.HandleKeyDown(e);
        }

        // Google Translate API Key changed
        private void GoogleTranslateApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                // string apiKey = googleTranslateApiKeyPasswordBox.Password.Trim();

                // // Update the config
                // ConfigManager.Instance.SetGoogleTranslateApiKey(apiKey);
                Console.WriteLine("Google Translate API key updated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Google Translate API key: {ex.Message}");
            }
        }

        // Google Translate Service Type changed
        private void GoogleTranslateServiceTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (googleTranslateServiceTypeComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    bool isCloudApi = selectedItem.Content.ToString() == "Cloud API (paid)";

                    // Show/hide API key field based on selection
                    googleTranslateApiKeyLabel.Visibility = isCloudApi ? Visibility.Visible : Visibility.Collapsed;
                    googleTranslateApiKeyGrid.Visibility = isCloudApi ? Visibility.Visible : Visibility.Collapsed;
                    // viewGoogleTranslateKeysButton.Visibility = isCloudApi ? Visibility.Visible : Visibility.Collapsed;

                    // Save to config
                    ConfigManager.Instance.SetGoogleTranslateUseCloudApi(isCloudApi);
                    Console.WriteLine($"Google Translate service type set to: {(isCloudApi ? "Cloud API" : "Free Web Service")}");

                    // Trigger retranslation if the current service is Google Translate
                    if (ConfigManager.Instance.GetCurrentTranslationService() == "Google Translate")
                    {
                        Console.WriteLine("Google Translate service type changed. Triggering retranslation...");

                        // Reset the hash to force a retranslation
                        Logic.Instance.ResetHash();

                        // Clear any existing text objects to refresh the display
                        Logic.Instance.ClearAllTextObjects();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Google Translate service type: {ex.Message}");
            }
        }

        // Google Translate language mapping checkbox changed
        private void GoogleTranslateMappingCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                bool isEnabled = googleTranslateMappingCheckBox.IsChecked ?? true;

                // Save to config
                ConfigManager.Instance.SetGoogleTranslateAutoMapLanguages(isEnabled);
                Console.WriteLine($"Google Translate auto language mapping set to: {isEnabled}");

                // Trigger retranslation if the current service is Google Translate
                if (ConfigManager.Instance.GetCurrentTranslationService() == "Google Translate")
                {
                    Console.WriteLine("Google Translate language mapping changed. Triggering retranslation...");

                    // Reset the hash to force a retranslation
                    Logic.Instance.ResetHash();

                    // Clear any existing text objects to refresh the display
                    Logic.Instance.ClearAllTextObjects();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Google Translate language mapping: {ex.Message}");
            }
        }

        // Google Translate API link click
        private void GoogleTranslateApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://cloud.google.com/translate/docs/setup");
        }

        // Helper method to check if a window instance is still valid
        private static bool IsWindowValid(Window window)
        {
            // Check if the window still exists in the application's window collection
            var windowCollection = System.Windows.Application.Current.Windows;
            for (int i = 0; i < windowCollection.Count; i++)
            {
                if (windowCollection[i] == window)
                {
                    return true;
                }
            }
            return false;
        }

        private void LoadSettingsFromMainWindow()
        {
            // Temporarily remove event handlers to prevent triggering changes during initialization
            sourceLanguageComboBox.SelectionChanged -= SourceLanguageComboBox_SelectionChanged;
            targetLanguageComboBox.SelectionChanged -= TargetLanguageComboBox_SelectionChanged;

            // Remove focus event handlers
            maxContextPiecesTextBox.LostFocus -= MaxContextPiecesTextBox_LostFocus;
            minContextSizeTextBox.LostFocus -= MinContextSizeTextBox_LostFocus;
            minChatBoxTextSizeTextBox.LostFocus -= MinChatBoxTextSizeTextBox_LostFocus;
            gameInfoTextBox.TextChanged -= GameInfoTextBox_TextChanged;
            minTextFragmentSizeTextBox.LostFocus -= MinTextFragmentSizeTextBox_LostFocus;
            minLetterConfidenceTextBox.LostFocus -= MinLetterConfidenceTextBox_LostFocus;
            minLineConfidenceTextBox.LostFocus -= MinLineConfidenceTextBox_LostFocus;
            blockDetectionPowerTextBox.LostFocus -= BlockDetectionPowerTextBox_LostFocus;
            settleTimeTextBox.LostFocus -= SettleTimeTextBox_LostFocus;

            // Set context settings
            maxContextPiecesTextBox.Text = ConfigManager.Instance.GetMaxContextPieces().ToString();
            minContextSizeTextBox.Text = ConfigManager.Instance.GetMinContextSize().ToString();
            minChatBoxTextSizeTextBox.Text = ConfigManager.Instance.GetChatBoxMinTextSize().ToString();
            gameInfoTextBox.Text = ConfigManager.Instance.GetGameInfo();
            minTextFragmentSizeTextBox.Text = ConfigManager.Instance.GetMinTextFragmentSize().ToString();
            minLetterConfidenceTextBox.Text = ConfigManager.Instance.GetMinLetterConfidence().ToString();
            minLineConfidenceTextBox.Text = ConfigManager.Instance.GetMinLineConfidence().ToString();

            // Reattach focus event handlers
            maxContextPiecesTextBox.LostFocus += MaxContextPiecesTextBox_LostFocus;
            minContextSizeTextBox.LostFocus += MinContextSizeTextBox_LostFocus;
            minChatBoxTextSizeTextBox.LostFocus += MinChatBoxTextSizeTextBox_LostFocus;
            gameInfoTextBox.TextChanged += GameInfoTextBox_TextChanged;
            minTextFragmentSizeTextBox.LostFocus += MinTextFragmentSizeTextBox_LostFocus;
            minLetterConfidenceTextBox.LostFocus += MinLetterConfidenceTextBox_LostFocus;
            minLineConfidenceTextBox.LostFocus += MinLineConfidenceTextBox_LostFocus;

            textSimilarThresholdTextBox.LostFocus += TextSimilarThresholdTextBox_LostFocus;
            // Load source language either from config or MainWindow as fallback
            string configSourceLanguage = ConfigManager.Instance.GetSourceLanguage();
            if (!string.IsNullOrEmpty(configSourceLanguage))
            {
                // First try to load from config
                foreach (ComboBoxItem item in sourceLanguageComboBox.Items)
                {
                    if (string.Equals(item.Content.ToString(), configSourceLanguage, StringComparison.OrdinalIgnoreCase))
                    {
                        sourceLanguageComboBox.SelectedItem = item;
                        Console.WriteLine($"Settings window: Set source language from config to {configSourceLanguage}");
                        break;
                    }
                }
            }
            else if (MainWindow.Instance.sourceLanguageComboBox != null &&
                     MainWindow.Instance.sourceLanguageComboBox.SelectedIndex >= 0)
            {
                // Fallback to MainWindow if config doesn't have a value
                sourceLanguageComboBox.SelectedIndex = MainWindow.Instance.sourceLanguageComboBox.SelectedIndex;
            }

            // Load target language either from config or MainWindow as fallback
            string configTargetLanguage = ConfigManager.Instance.GetTargetLanguage();
            if (!string.IsNullOrEmpty(configTargetLanguage))
            {
                // First try to load from config
                foreach (ComboBoxItem item in targetLanguageComboBox.Items)
                {
                    if (string.Equals(item.Content.ToString(), configTargetLanguage, StringComparison.OrdinalIgnoreCase))
                    {
                        targetLanguageComboBox.SelectedItem = item;
                        Console.WriteLine($"Settings window: Set target language from config to {configTargetLanguage}");
                        break;
                    }
                }
            }
            else if (MainWindow.Instance.targetLanguageComboBox != null &&
                     MainWindow.Instance.targetLanguageComboBox.SelectedIndex >= 0)
            {
                // Fallback to MainWindow if config doesn't have a value
                targetLanguageComboBox.SelectedIndex = MainWindow.Instance.targetLanguageComboBox.SelectedIndex;
            }

            // Reattach event handlers
            sourceLanguageComboBox.SelectionChanged += SourceLanguageComboBox_SelectionChanged;
            targetLanguageComboBox.SelectionChanged += TargetLanguageComboBox_SelectionChanged;

            // Set text similar threshold from config
            textSimilarThresholdTextBox.Text = Convert.ToString(ConfigManager.Instance.GetTextSimilarThreshold());

            // Set char level from config
            charLevelCheckBox.IsChecked = ConfigManager.Instance.IsCharLevelEnabled();

            // Set OCR settings from config
            string savedOcrMethod = ConfigManager.Instance.GetOcrMethod();
            Console.WriteLine($"SettingsWindow: Loading OCR method '{savedOcrMethod}'");

            // Temporarily remove event handler to prevent triggering during initialization
            ocrMethodComboBox.SelectionChanged -= OcrMethodComboBox_SelectionChanged;

            // Find matching ComboBoxItem
            foreach (ComboBoxItem item in ocrMethodComboBox.Items)
            {
                string itemText = item.Content.ToString() ?? "";
                if (string.Equals(itemText, savedOcrMethod, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Found matching OCR method: '{itemText}'");
                    ocrMethodComboBox.SelectedItem = item;
                    break;
                }
            }

            // Re-attach event handler
            ocrMethodComboBox.SelectionChanged += OcrMethodComboBox_SelectionChanged;

            // Get auto-translate setting from config instead of MainWindow
            // This ensures the setting persists across application restarts
            autoTranslateCheckBox.IsChecked = ConfigManager.Instance.IsAutoTranslateEnabled();
            Console.WriteLine($"Settings window: Loading auto-translate from config: {ConfigManager.Instance.IsAutoTranslateEnabled()}");

            // Set leave translation onscreen setting
            leaveTranslationOnscreenCheckBox.IsChecked = ConfigManager.Instance.IsLeaveTranslationOnscreenEnabled();

            // Set block detection settings directly from BlockDetectionManager
            // Temporarily remove event handlers to prevent triggering changes
            blockDetectionPowerTextBox.LostFocus -= BlockDetectionPowerTextBox_LostFocus;
            settleTimeTextBox.LostFocus -= SettleTimeTextBox_LostFocus;


            blockDetectionPowerTextBox.Text = BlockDetectionManager.Instance.GetBlockDetectionScale().ToString("F2");
            settleTimeTextBox.Text = ConfigManager.Instance.GetBlockDetectionSettleTime().ToString("F2");

            Console.WriteLine($"SettingsWindow: Loaded block detection power: {blockDetectionPowerTextBox.Text}");
            Console.WriteLine($"SettingsWindow: Loaded settle time: {settleTimeTextBox.Text}");

            // Reattach event handlers
            blockDetectionPowerTextBox.LostFocus += BlockDetectionPowerTextBox_LostFocus;
            settleTimeTextBox.LostFocus += SettleTimeTextBox_LostFocus;

            // Set translation service from config
            string currentService = ConfigManager.Instance.GetCurrentTranslationService();

            // Temporarily remove event handler
            translationServiceComboBox.SelectionChanged -= TranslationServiceComboBox_SelectionChanged;

            foreach (ComboBoxItem item in translationServiceComboBox.Items)
            {
                if (string.Equals(item.Content.ToString(), currentService, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Found matching translation service: '{item.Content}'");
                    translationServiceComboBox.SelectedItem = item;
                    break;
                }
            }

            // Re-attach event handler
            translationServiceComboBox.SelectionChanged += TranslationServiceComboBox_SelectionChanged;

            // Initialize API key for Gemini
            geminiApiKeyPasswordBox.Password = ConfigManager.Instance.GetGeminiApiKey();

            // Initialize Ollama settings
            ollamaUrlTextBox.Text = ConfigManager.Instance.GetOllamaUrl();
            ollamaPortTextBox.Text = ConfigManager.Instance.GetOllamaPort();
            ollamaModelTextBox.Text = ConfigManager.Instance.GetOllamaModel();

            // Update service-specific settings visibility based on selected service
            UpdateServiceSpecificSettings(currentService);

            // Load the current service's prompt
            LoadCurrentServicePrompt();

            // Load TTS settings

            // Temporarily remove TTS event handlers
            ttsEnabledCheckBox.Checked -= TtsEnabledCheckBox_CheckedChanged;
            ttsEnabledCheckBox.Unchecked -= TtsEnabledCheckBox_CheckedChanged;
            ttsServiceComboBox.SelectionChanged -= TtsServiceComboBox_SelectionChanged;
            elevenLabsVoiceComboBox.SelectionChanged -= ElevenLabsVoiceComboBox_SelectionChanged;
            googleTtsVoiceComboBox.SelectionChanged -= GoogleTtsVoiceComboBox_SelectionChanged;

            // Set TTS enabled state
            ttsEnabledCheckBox.IsChecked = ConfigManager.Instance.IsTtsEnabled();

            // Set TTS service
            string ttsService = ConfigManager.Instance.GetTtsService();
            foreach (ComboBoxItem item in ttsServiceComboBox.Items)
            {
                if (string.Equals(item.Content.ToString(), ttsService, StringComparison.OrdinalIgnoreCase))
                {
                    ttsServiceComboBox.SelectedItem = item;
                    break;
                }
            }

            // Update service-specific settings visibility
            UpdateTtsServiceSpecificSettings(ttsService);

            // Set ElevenLabs API key
            elevenLabsApiKeyPasswordBox.Password = ConfigManager.Instance.GetElevenLabsApiKey();

            // Set ElevenLabs voice
            string elevenLabsVoiceId = ConfigManager.Instance.GetElevenLabsVoice();
            foreach (ComboBoxItem item in elevenLabsVoiceComboBox.Items)
            {
                if (string.Equals(item.Tag?.ToString(), elevenLabsVoiceId, StringComparison.OrdinalIgnoreCase))
                {
                    elevenLabsVoiceComboBox.SelectedItem = item;
                    break;
                }
            }

            // Set Google TTS API key
            googleTtsApiKeyPasswordBox.Password = ConfigManager.Instance.GetGoogleTtsApiKey();

            // Set Google TTS voice
            string googleVoiceId = ConfigManager.Instance.GetGoogleTtsVoice();
            foreach (ComboBoxItem item in googleTtsVoiceComboBox.Items)
            {
                if (string.Equals(item.Tag?.ToString(), googleVoiceId, StringComparison.OrdinalIgnoreCase))
                {
                    googleTtsVoiceComboBox.SelectedItem = item;
                    break;
                }
            }

            // Re-attach TTS event handlers
            ttsEnabledCheckBox.Checked += TtsEnabledCheckBox_CheckedChanged;
            ttsEnabledCheckBox.Unchecked += TtsEnabledCheckBox_CheckedChanged;
            ttsServiceComboBox.SelectionChanged += TtsServiceComboBox_SelectionChanged;
            elevenLabsVoiceComboBox.SelectionChanged += ElevenLabsVoiceComboBox_SelectionChanged;
            googleTtsVoiceComboBox.SelectionChanged += GoogleTtsVoiceComboBox_SelectionChanged;

            // Load ignore phrases
            LoadIgnorePhrases();

            // Audio Processing settings
            audioProcessingProviderComboBox.SelectedIndex = 0; // Only one for now
            openAiRealtimeApiKeyPasswordBox.Password = ConfigManager.Instance.GetOpenAiRealtimeApiKey();
            // Load Auto-translate for audio service
            audioServiceAutoTranslateCheckBox.IsChecked = ConfigManager.Instance.IsAudioServiceAutoTranslateEnabled();
        }

        // Language settings
        private void SourceLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip event if we're initializing
            if (_isInitializing)
            {
                return;
            }

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string language = selectedItem.Content.ToString() ?? "ja";
                Console.WriteLine($"Settings: Source language changed to: {language}");

                // Save to config
                ConfigManager.Instance.SetSourceLanguage(language);

                // Update MainWindow source language
                if (MainWindow.Instance.sourceLanguageComboBox != null)
                {
                    // Find and select matching ComboBoxItem by content
                    foreach (ComboBoxItem item in MainWindow.Instance.sourceLanguageComboBox.Items)
                    {
                        if (string.Equals(item.Content.ToString(), language, StringComparison.OrdinalIgnoreCase))
                        {
                            MainWindow.Instance.sourceLanguageComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Reset the OCR hash to force a fresh comparison after changing source language
                Logic.Instance.ClearAllTextObjects();
            }
        }

        private void TargetLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip event if we're initializing
            if (_isInitializing)
            {
                return;
            }

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string language = selectedItem.Content.ToString() ?? "en";
                Console.WriteLine($"Settings: Target language changed to: {language}");

                // Save to config
                ConfigManager.Instance.SetTargetLanguage(language);

                // Update MainWindow target language
                if (MainWindow.Instance.targetLanguageComboBox != null)
                {
                    // Find and select matching ComboBoxItem by content
                    foreach (ComboBoxItem item in MainWindow.Instance.targetLanguageComboBox.Items)
                    {
                        if (string.Equals(item.Content.ToString(), language, StringComparison.OrdinalIgnoreCase))
                        {
                            MainWindow.Instance.targetLanguageComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Reset the OCR hash to force a fresh comparison after changing target language
                Logic.Instance.ClearAllTextObjects();
            }
        }

        private void OcrMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip event if we're initializing
            if (_isInitializing)
            {
                Console.WriteLine("Skipping OCR method change during initialization");
                return;
            }

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string? ocrMethod = selectedItem.Content?.ToString();

                if (!string.IsNullOrEmpty(ocrMethod))
                {
                    Console.WriteLine($"Setting OCR method to: {ocrMethod}");

                    // Save to config
                    ConfigManager.Instance.SetOcrMethod(ocrMethod);

                    // Update UI
                    MainWindow.Instance.SetOcrMethod(ocrMethod);
                    UpdateMonitorWindowOcrMethod(ocrMethod);
                    SocketManager.Instance.Disconnect();

                    // await SocketManager.Instance.SwitchOcrMethod(ocrMethod);
                    if (ocrMethod != "Windows OCR")
                    {
                        checkLanguagePack.Visibility = Visibility.Collapsed;
                        checkLanguagePackButton.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        checkLanguagePack.Visibility = Visibility.Visible;
                        checkLanguagePackButton.Visibility = Visibility.Visible;
                    }

                }
            }
        }

        private void UpdateMonitorWindowOcrMethod(string ocrMethod)
        {
            // Update MonitorWindow OCR method selection
            if (MonitorWindow.Instance.ocrMethodComboBox != null)
            {
                foreach (ComboBoxItem item in MonitorWindow.Instance.ocrMethodComboBox.Items)
                {
                    if (string.Equals(item.Content.ToString(), ocrMethod, StringComparison.OrdinalIgnoreCase))
                    {
                        MonitorWindow.Instance.ocrMethodComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void AutoTranslateCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Skip if initializing to prevent overriding values from config
            if (_isInitializing)
            {
                return;
            }

            bool isEnabled = autoTranslateCheckBox.IsChecked ?? false;
            Console.WriteLine($"Settings window: Auto-translate changed to {isEnabled}");

            // Update auto translate setting in MainWindow
            // This will also save to config and update the UI
            MainWindow.Instance.SetAutoTranslateEnabled(isEnabled);

            // Update MonitorWindow CheckBox if needed
            if (MonitorWindow.Instance.autoTranslateCheckBox != null)
            {
                MonitorWindow.Instance.autoTranslateCheckBox.IsChecked = autoTranslateCheckBox.IsChecked;
            }
        }

        private void CharLevelCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Skip if initializing to prevent overriding values from config
            if (_isInitializing)
            {
                return;
            }
            bool isEnabled = charLevelCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetCharLevelEnabled(isEnabled);
            // Clear text objects
            Logic.Instance.ClearAllTextObjects();
            Logic.Instance.ResetHash();
            // Force OCR to run again
            MainWindow.Instance.SetOCRCheckIsWanted(true);
            Console.WriteLine($"Settings window: Character level mode changed to {isEnabled}");
        }

        private void LeaveTranslationOnscreenCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Skip if initializing
            if (_isInitializing)
                return;

            bool isEnabled = leaveTranslationOnscreenCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetLeaveTranslationOnscreenEnabled(isEnabled);
            Console.WriteLine($"Leave translation onscreen enabled: {isEnabled}");
        }

        // Language swap button handler
        private void SwapLanguagesButton_Click(object sender, RoutedEventArgs e)
        {
            // Store the current selections
            int sourceIndex = sourceLanguageComboBox.SelectedIndex;
            int targetIndex = targetLanguageComboBox.SelectedIndex;

            // Swap the selections
            sourceLanguageComboBox.SelectedIndex = targetIndex;
            targetLanguageComboBox.SelectedIndex = sourceIndex;

            // The SelectionChanged events will handle updating the MainWindow
            Console.WriteLine($"Languages swapped: {GetLanguageCode(sourceLanguageComboBox)} ⇄ {GetLanguageCode(targetLanguageComboBox)}");
        }

        // Helper method to get language code from ComboBox
        private string GetLanguageCode(ComboBox comboBox)
        {
            return ((ComboBoxItem)comboBox.SelectedItem).Content.ToString() ?? "";
        }

        // Block detection settings
        private void BlockDetectionPowerTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Skip if initializing to prevent overriding values from config
            if (_isInitializing)
            {
                return;
            }

            // Update block detection power in MonitorWindow
            if (MonitorWindow.Instance.blockDetectionPowerTextBox != null)
            {
                MonitorWindow.Instance.blockDetectionPowerTextBox.Text = blockDetectionPowerTextBox.Text;
            }

            // Update BlockDetectionManager if applicable
            if (float.TryParse(blockDetectionPowerTextBox.Text, out float power))
            {
                // Note: SetBlockDetectionScale will save to config
                BlockDetectionManager.Instance.SetBlockDetectionScale(power);
                // Reset hash to force recalculation of text blocks
                Logic.Instance.ResetHash();
            }
            else
            {
                // If text is invalid, reset to the current value from BlockDetectionManager
                blockDetectionPowerTextBox.Text = BlockDetectionManager.Instance.GetBlockDetectionScale().ToString("F2");
            }
        }

        private void SettleTimeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Skip if initializing to prevent overriding values from config
            if (_isInitializing)
            {
                return;
            }

            // Update settle time in ConfigManager
            if (float.TryParse(settleTimeTextBox.Text, out float settleTime) && settleTime >= 0)
            {
                ConfigManager.Instance.SetBlockDetectionSettleTime(settleTime);
                Console.WriteLine($"Block detection settle time set to: {settleTime:F2} seconds");

                // Reset hash to force recalculation of text blocks
                Logic.Instance.ResetHash();
            }
            else
            {
                // If text is invalid, reset to the current value from ConfigManager
                settleTimeTextBox.Text = ConfigManager.Instance.GetBlockDetectionSettleTime().ToString("F2");
            }
        }

        // Translation service changed
        private void TranslationServiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip event if we're initializing
            Console.WriteLine($"SettingsWindow.TranslationServiceComboBox_SelectionChanged called (isInitializing: {_isInitializing})");
            if (_isInitializing)
            {
                Console.WriteLine("Skipping translation service change during initialization");
                return;
            }

            try
            {
                if (translationServiceComboBox == null)
                {
                    Console.WriteLine("Translation service combo box not initialized yet");
                    return;
                }

                if (translationServiceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string selectedService = selectedItem.Content.ToString() ?? "Gemini";

                    Console.WriteLine($"SettingsWindow translation service changed to: '{selectedService}'");

                    // Save the selected service to config
                    ConfigManager.Instance.SetTranslationService(selectedService);

                    // Update service-specific settings visibility
                    UpdateServiceSpecificSettings(selectedService);

                    // Load the prompt for the selected service
                    LoadCurrentServicePrompt();

                    // Only trigger retranslation if not initializing (i.e., user changed it manually)
                    if (!_isInitializing)
                    {
                        Console.WriteLine("Translation service changed. Triggering retranslation...");

                        // Reset the hash to force a retranslation
                        Logic.Instance.ResetHash();

                        // Clear any existing text objects to refresh the display
                        Logic.Instance.ClearAllTextObjects();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling translation service change: {ex.Message}");
            }
        }

        // Load prompt for the currently selected translation service
        private void LoadCurrentServicePrompt()
        {
            try
            {
                if (translationServiceComboBox == null || promptTemplateTextBox == null)
                {
                    Console.WriteLine("Translation service controls not initialized yet. Skipping prompt loading.");
                    return;
                }

                if (translationServiceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string selectedService = selectedItem.Content.ToString() ?? "Gemini";
                    string prompt = ConfigManager.Instance.GetServicePrompt(selectedService);

                    // Update the text box
                    promptTemplateTextBox.Text = prompt;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading prompt template: {ex.Message}");
            }
        }

        // Save prompt button clicked
        private void SavePromptButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentPrompt();
        }

        // Text box lost focus - save prompt
        private void PromptTemplateTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveCurrentPrompt();
        }

        // Save the current prompt to the selected service
        private void SaveCurrentPrompt()
        {
            if (translationServiceComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedService = selectedItem.Content.ToString() ?? "Gemini";
                string prompt = promptTemplateTextBox.Text;

                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    // Save to config
                    bool success = ConfigManager.Instance.SaveServicePrompt(selectedService, prompt);

                    if (success)
                    {
                        Console.WriteLine($"Prompt saved for {selectedService}");
                    }
                }
            }
        }

        private void RestoreDefaultPromptButton_Click(object sender, RoutedEventArgs e)
        {
            // Restore the default prompt for the selected service
            if (translationServiceComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedService = selectedItem.Content.ToString() ?? "Gemini";
                string defaultPrompt = ConfigManager.Instance.GetDefaultServicePrompt(selectedService);

                if (!string.IsNullOrWhiteSpace(defaultPrompt))
                {
                    promptTemplateTextBox.Text = defaultPrompt;
                }
            }
        }

        // Update service-specific settings visibility
        private void UpdateServiceSpecificSettings(string selectedService)
        {
            try
            {
                bool isOllamaSelected = selectedService == "Ollama";
                bool isGeminiSelected = selectedService == "Gemini";
                bool isChatGptSelected = selectedService == "ChatGPT";
                bool isGoogleTranslateSelected = selectedService == "Google Translate";

                // Make sure the window is fully loaded and controls are initialized
                if (ollamaUrlLabel == null || ollamaUrlTextBox == null ||
                    ollamaPortLabel == null || ollamaPortTextBox == null ||
                    ollamaModelLabel == null || ollamaModelGrid == null ||
                    geminiApiKeyLabel == null || geminiApiKeyPasswordBox == null ||
                    geminiModelLabel == null || geminiModelGrid == null ||
                    chatGptApiKeyLabel == null || chatGptApiKeyGrid == null ||
                    chatGptModelLabel == null || chatGptModelGrid == null ||
                    googleTranslateApiKeyLabel == null || googleTranslateApiKeyGrid == null ||
                    googleTranslateServiceTypeLabel == null || googleTranslateServiceTypeComboBox == null ||
                    googleTranslateMappingLabel == null || googleTranslateMappingCheckBox == null)
                {
                    Console.WriteLine("UI elements not initialized yet. Skipping visibility update.");
                    return;
                }

                // Show/hide Gemini-specific settings
                geminiApiKeyLabel.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                geminiApiKeyPasswordBox.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                geminiApiKeyHelpText.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                geminiModelLabel.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                geminiModelGrid.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                viewGeminiKeysButton.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                geminiNote.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Ollama-specific settings
                ollamaUrlLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaUrlGrid.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaPortLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaPortTextBox.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaModelLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaModelGrid.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide ChatGPT-specific settings
                chatGptApiKeyLabel.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                chatGptApiKeyGrid.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                chatGptModelLabel.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                chatGptModelGrid.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                viewChatGptKeysButton.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                chatGptNote.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Google Translate-specific settings
                googleTranslateServiceTypeLabel.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTranslateServiceTypeComboBox.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTranslateMappingLabel.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTranslateMappingCheckBox.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;

                // Hide prompt template for Google Translate
                bool showPromptTemplate = !isGoogleTranslateSelected;

                // API key is only visible for Google Translate if Cloud API is selected
                bool showGoogleTranslateApiKey = isGoogleTranslateSelected &&
                    (googleTranslateServiceTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() == "Cloud API (paid)";

                googleTranslateApiKeyLabel.Visibility = showGoogleTranslateApiKey ? Visibility.Visible : Visibility.Collapsed;
                googleTranslateApiKeyGrid.Visibility = showGoogleTranslateApiKey ? Visibility.Visible : Visibility.Collapsed;
                // viewGoogleTranslateKeysButton.Visibility = showGoogleTranslateApiKey ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide prompt template and related controls for Google Translate
                promptLabel.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;
                promptTemplateTextBox.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;
                savePromptButton.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;
                restoreDefaultPromptButton.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;

                // Load service-specific settings if they're being shown
                if (isGeminiSelected)
                {
                    geminiApiKeyPasswordBox.Password = ConfigManager.Instance.GetGeminiApiKey();

                    // Set selected Gemini model
                    string geminiModel = ConfigManager.Instance.GetGeminiModel();

                    // Temporarily remove event handlers to avoid triggering changes
                    geminiModelComboBox.SelectionChanged -= GeminiModelComboBox_SelectionChanged;

                    // First try to find exact match in dropdown items
                    bool found = false;
                    foreach (ComboBoxItem item in geminiModelComboBox.Items)
                    {
                        if (string.Equals(item.Content?.ToString(), geminiModel, StringComparison.OrdinalIgnoreCase))
                        {
                            geminiModelComboBox.SelectedItem = item;
                            found = true;
                            break;
                        }
                    }

                    // If not found in dropdown, set as custom text
                    if (!found)
                    {
                        geminiModelComboBox.Text = geminiModel;
                    }

                    // Reattach event handler
                    geminiModelComboBox.SelectionChanged += GeminiModelComboBox_SelectionChanged;
                }
                else if (isOllamaSelected)
                {
                    ollamaUrlTextBox.Text = ConfigManager.Instance.GetOllamaUrl();
                    ollamaPortTextBox.Text = ConfigManager.Instance.GetOllamaPort();
                    ollamaModelTextBox.Text = ConfigManager.Instance.GetOllamaModel();
                }
                else if (isChatGptSelected)
                {
                    chatGptApiKeyPasswordBox.Password = ConfigManager.Instance.GetChatGptApiKey();

                    // Set selected model
                    string model = ConfigManager.Instance.GetChatGptModel();
                    foreach (ComboBoxItem item in chatGptModelComboBox.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), model, StringComparison.OrdinalIgnoreCase))
                        {
                            chatGptModelComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
                else if (isGoogleTranslateSelected)
                {
                    // Set Google Translate service type
                    bool useCloudApi = ConfigManager.Instance.GetGoogleTranslateUseCloudApi();

                    // Temporarily remove event handler
                    googleTranslateServiceTypeComboBox.SelectionChanged -= GoogleTranslateServiceTypeComboBox_SelectionChanged;

                    googleTranslateServiceTypeComboBox.SelectedIndex = useCloudApi ? 1 : 0; // 0 = Free, 1 = Cloud API

                    // Reattach event handler
                    googleTranslateServiceTypeComboBox.SelectionChanged += GoogleTranslateServiceTypeComboBox_SelectionChanged;

                    // Set API key if using Cloud API
                    if (useCloudApi)
                    {
                        googleTranslateApiKeyPasswordBox.Password = ConfigManager.Instance.GetGoogleTranslateApiKey();
                    }

                    // Set language mapping checkbox
                    googleTranslateMappingCheckBox.IsChecked = ConfigManager.Instance.GetGoogleTranslateAutoMapLanguages();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating service-specific settings: {ex.Message}");
            }
        }

        private void AdjustOverlayConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create options window
                var optionsWindow = new OverlayOptionsWindow();

                // Set the owner to ensure it appears on top of this window
                optionsWindow.Owner = this;

                // Make this window appear in front
                this.Topmost = false;
                this.Topmost = true;

                // Show the dialog
                var result = optionsWindow.ShowDialog();

                // If user clicked OK, styling will already be applied by the options window
                if (result == true)
                {
                    Console.WriteLine("Chat box options updated");

                    // Create and start the flash animation for visual feedback
                    CreateFlashAnimation(overlayConfig);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error showing options dialog: {ex.Message}");
            }
        }

        private void CreateFlashAnimation(System.Windows.Controls.Button button)
        {
            try
            {
                // Get the current background brush
                SolidColorBrush? currentBrush = button.Background as SolidColorBrush;

                if (currentBrush != null)
                {
                    // Need to freeze the original brush to animate its clone
                    currentBrush = currentBrush.Clone();
                    System.Windows.Media.Color originalColor = currentBrush.Color;

                    // Create a new brush for animation
                    SolidColorBrush animBrush = new SolidColorBrush(originalColor);
                    button.Background = animBrush;

                    // Create color animation for the brush's Color property
                    var animation = new ColorAnimation
                    {
                        From = originalColor,
                        To = Colors.LightGreen,
                        Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                        AutoReverse = true,
                        FillBehavior = FillBehavior.Stop // Stop the animation when complete
                    };

                    // Apply the animation to the brush's Color property
                    animBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating flash animation: {ex.Message}");
            }
        }

        private void UpdateTtsServiceSpecificSettings(string selectedService)
        {
            try
            {
                bool isElevenLabsSelected = selectedService == "ElevenLabs";
                bool isGoogleTtsSelected = selectedService == "Google Cloud TTS";

                // Make sure the window is fully loaded and controls are initialized
                if (elevenLabsApiKeyLabel == null || elevenLabsApiKeyGrid == null ||
                    elevenLabsApiKeyHelpText == null || elevenLabsVoiceLabel == null ||
                    elevenLabsVoiceComboBox == null || googleTtsApiKeyLabel == null ||
                    googleTtsApiKeyGrid == null || googleTtsVoiceLabel == null ||
                    googleTtsVoiceComboBox == null)
                {
                    Console.WriteLine("TTS UI elements not initialized yet. Skipping visibility update.");
                    return;
                }

                // Show/hide ElevenLabs-specific settings
                elevenLabsApiKeyLabel.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsApiKeyGrid.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsApiKeyHelpText.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsVoiceLabel.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsVoiceComboBox.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Google TTS-specific settings
                googleTtsApiKeyLabel.Visibility = isGoogleTtsSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTtsApiKeyGrid.Visibility = isGoogleTtsSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTtsVoiceLabel.Visibility = isGoogleTtsSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTtsVoiceComboBox.Visibility = isGoogleTtsSelected ? Visibility.Visible : Visibility.Collapsed;

                // Load service-specific settings if they're being shown
                if (isElevenLabsSelected)
                {
                    elevenLabsApiKeyPasswordBox.Password = ConfigManager.Instance.GetElevenLabsApiKey();

                    // Set selected voice
                    string voiceId = ConfigManager.Instance.GetElevenLabsVoice();
                    foreach (ComboBoxItem item in elevenLabsVoiceComboBox.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), voiceId, StringComparison.OrdinalIgnoreCase))
                        {
                            elevenLabsVoiceComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
                else if (isGoogleTtsSelected)
                {
                    googleTtsApiKeyPasswordBox.Password = ConfigManager.Instance.GetGoogleTtsApiKey();

                    // Set selected voice
                    string voiceId = ConfigManager.Instance.GetGoogleTtsVoice();
                    foreach (ComboBoxItem item in googleTtsVoiceComboBox.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), voiceId, StringComparison.OrdinalIgnoreCase))
                        {
                            googleTtsVoiceComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS service-specific settings: {ex.Message}");
            }
        }

        // Gemini API Key changed
        private void GeminiApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // string apiKey = geminiApiKeyPasswordBox.Password.Trim();

                // // Update the config
                // ConfigManager.Instance.SetGeminiApiKey(apiKey);
                Console.WriteLine("Gemini API key updated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Gemini API key: {ex.Message}");
            }
        }

        // Ollama URL changed
        private void OllamaUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string url = ollamaUrlTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(url))
            {
                ConfigManager.Instance.SetOllamaUrl(url);
            }
        }

        // Ollama Port changed
        private void OllamaPortTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string port = ollamaPortTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(port))
            {
                // Validate that the port is a number
                if (int.TryParse(port, out _))
                {
                    ConfigManager.Instance.SetOllamaPort(port);
                }
                else
                {
                    // Reset to default if invalid
                    ollamaPortTextBox.Text = "11434";
                }
            }
        }

        // Ollama Model changed
        private void OllamaModelTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Skip if initializing or if the sender isn't the expected TextBox
            if (_isInitializing || sender != ollamaModelTextBox)
                return;

            string sanitizedModel = ollamaModelTextBox.Text.Trim();


            // Save valid model to config
            ConfigManager.Instance.SetOllamaModel(sanitizedModel);
            Console.WriteLine($"Ollama model set to: {sanitizedModel}");

            // Trigger retranslation if the current service is Ollama
            if (ConfigManager.Instance.GetCurrentTranslationService() == "Ollama")
            {
                Console.WriteLine("Ollama model changed. Triggering retranslation...");

                // Reset the hash to force a retranslation
                Logic.Instance.ResetHash();

                // Clear any existing text objects to refresh the display
                Logic.Instance.ClearAllTextObjects();
            }
        }

        // Model downloader instance
        private readonly OllamaModelDownloader _modelDownloader = new OllamaModelDownloader();

        private async void TestModelButton_Click(object sender, RoutedEventArgs e)
        {
            string model = ollamaModelTextBox.Text.Trim();
            await _modelDownloader.TestAndDownloadModel(model);
        }

        private void ViewModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ollama.com/search");
        }

        private void GeminiApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ai.google.dev/tutorials/setup");
        }

        private void GeminiModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string model;

                // Handle both dropdown selection and manually typed values
                if (geminiModelComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    model = selectedItem.Content?.ToString() ?? "gemini-2.0-flash-lite";
                }
                else
                {
                    // For manually entered text
                    model = geminiModelComboBox.Text?.Trim() ?? "gemini-2.0-flash-lite";
                }

                if (!string.IsNullOrWhiteSpace(model))
                {
                    // Save to config
                    ConfigManager.Instance.SetGeminiModel(model);
                    Console.WriteLine($"Gemini model set to: {model}");

                    // Trigger retranslation if the current service is Gemini
                    if (ConfigManager.Instance.GetCurrentTranslationService() == "Gemini")
                    {
                        Console.WriteLine("Gemini model changed. Triggering retranslation...");

                        // Reset the hash to force a retranslation
                        Logic.Instance.ResetHash();

                        // Clear any existing text objects to refresh the display
                        Logic.Instance.ClearAllTextObjects();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Gemini model: {ex.Message}");
            }
        }

        private void ViewGeminiModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ai.google.dev/gemini-api/docs/models");
        }

        private void GeminiModelComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string model = geminiModelComboBox.Text?.Trim() ?? "";

                if (!string.IsNullOrWhiteSpace(model))
                {
                    // Save to config
                    ConfigManager.Instance.SetGeminiModel(model);
                    Console.WriteLine($"Gemini model set from text input to: {model}");

                    // Trigger retranslation if the current service is Gemini
                    if (ConfigManager.Instance.GetCurrentTranslationService() == "Gemini")
                    {
                        // Reset the hash to force a retranslation
                        Logic.Instance.ResetHash();

                        // Clear any existing text objects to refresh the display
                        Logic.Instance.ClearAllTextObjects();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Gemini model from text input: {ex.Message}");
            }
        }

        private void OllamaDownloadLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ollama.com");
        }

        private void ChatGptApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://platform.openai.com/api-keys");
        }

        private void ViewChatGptModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://platform.openai.com/docs/models");
        }

        // ChatGPT API Key changed
        private void ChatGptApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                // string apiKey = chatGptApiKeyPasswordBox.Password.Trim();

                // // Update the config
                // ConfigManager.Instance.SetChatGptApiKey(apiKey);
                Console.WriteLine("ChatGPT API key updated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ChatGPT API key: {ex.Message}");
            }
        }

        // ChatGPT Model changed
        private void ChatGptModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (chatGptModelComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string model = selectedItem.Tag?.ToString() ?? "gpt-3.5-turbo";

                    // Save to config
                    ConfigManager.Instance.SetChatGptModel(model);
                    Console.WriteLine($"ChatGPT model set to: {model}");

                    // Trigger retranslation if the current service is ChatGPT
                    if (ConfigManager.Instance.GetCurrentTranslationService() == "ChatGPT")
                    {
                        Console.WriteLine("ChatGPT model changed. Triggering retranslation...");

                        // Reset the hash to force a retranslation
                        Logic.Instance.ResetHash();

                        // Clear any existing text objects to refresh the display
                        Logic.Instance.ClearAllTextObjects();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ChatGPT model: {ex.Message}");
            }
        }

        private void ElevenLabsApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://elevenlabs.io/app/api-key");
        }

        private void GoogleTtsApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://cloud.google.com/text-to-speech");
        }

        private void OpenUrl(string url)
        {
            try
            {
                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(processStartInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening URL: {ex.Message}");
                MessageBox.Show($"Unable to open URL: {url}\n\nError: {ex.Message}",
                    "Error Opening URL", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Text-to-Speech settings handlers

        private void TtsEnabledCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                bool isEnabled = ttsEnabledCheckBox.IsChecked ?? false;
                ConfigManager.Instance.SetTtsEnabled(isEnabled);
                Console.WriteLine($"TTS enabled: {isEnabled}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS enabled state: {ex.Message}");
            }
        }

        private void TtsServiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (ttsServiceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string service = selectedItem.Content.ToString() ?? "ElevenLabs";
                    ConfigManager.Instance.SetTtsService(service);
                    Console.WriteLine($"TTS service set to: {service}");

                    // Update UI for the selected service
                    UpdateTtsServiceSpecificSettings(service);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS service: {ex.Message}");
            }
        }

        private void GoogleTtsApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string apiKey = googleTtsApiKeyPasswordBox.Password.Trim();
                ConfigManager.Instance.SetGoogleTtsApiKey(apiKey);
                Console.WriteLine("Google TTS API key updated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Google TTS API key: {ex.Message}");
            }
        }

        private void GoogleTtsVoiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (googleTtsVoiceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string voiceId = selectedItem.Tag?.ToString() ?? "ja-JP-Neural2-B"; // Default to Female A
                    ConfigManager.Instance.SetGoogleTtsVoice(voiceId);
                    Console.WriteLine($"Google TTS voice set to: {selectedItem.Content} (ID: {voiceId})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Google TTS voice: {ex.Message}");
            }
        }

        private void ElevenLabsApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string apiKey = elevenLabsApiKeyPasswordBox.Password.Trim();
                ConfigManager.Instance.SetElevenLabsApiKey(apiKey);
                Console.WriteLine("ElevenLabs API key updated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ElevenLabs API key: {ex.Message}");
            }
        }

        private void ElevenLabsVoiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (elevenLabsVoiceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string voiceId = selectedItem.Tag?.ToString() ?? "21m00Tcm4TlvDq8ikWAM"; // Default to Rachel
                    ConfigManager.Instance.SetElevenLabsVoice(voiceId);
                    Console.WriteLine($"ElevenLabs voice set to: {selectedItem.Content} (ID: {voiceId})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ElevenLabs voice: {ex.Message}");
            }
        }

        // Context settings handlers
        private void MaxContextPiecesTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (int.TryParse(maxContextPiecesTextBox.Text, out int maxContextPieces) && maxContextPieces >= 0)
                {
                    ConfigManager.Instance.SetMaxContextPieces(maxContextPieces);
                    Console.WriteLine($"Max context pieces set to: {maxContextPieces}");
                }
                else
                {
                    // Reset to current value from config if invalid
                    maxContextPiecesTextBox.Text = ConfigManager.Instance.GetMaxContextPieces().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating max context pieces: {ex.Message}");
            }
        }

        private void MinContextSizeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (int.TryParse(minContextSizeTextBox.Text, out int minContextSize) && minContextSize >= 0)
                {
                    ConfigManager.Instance.SetMinContextSize(minContextSize);
                    Console.WriteLine($"Min context size set to: {minContextSize}");
                }
                else
                {
                    // Reset to current value from config if invalid
                    minContextSizeTextBox.Text = ConfigManager.Instance.GetMinContextSize().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating min context size: {ex.Message}");
            }
        }

        private void MinChatBoxTextSizeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (int.TryParse(minChatBoxTextSizeTextBox.Text, out int minChatBoxTextSize) && minChatBoxTextSize >= 0)
                {
                    ConfigManager.Instance.SetChatBoxMinTextSize(minChatBoxTextSize);
                    Console.WriteLine($"Min ChatBox text size set to: {minChatBoxTextSize}");
                }
                else
                {
                    // Reset to current value from config if invalid
                    minChatBoxTextSizeTextBox.Text = ConfigManager.Instance.GetChatBoxMinTextSize().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating min ChatBox text size: {ex.Message}");
            }
        }

        private void GameInfoTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string gameInfo = gameInfoTextBox.Text.Trim();
                ConfigManager.Instance.SetGameInfo(gameInfo);
                Console.WriteLine($"Game info updated: {gameInfo}");

                // Reset the hash to force a retranslation when game info changes
                Logic.Instance.ResetHash();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating game info: {ex.Message}");
            }
        }

        private void MinTextFragmentSizeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (int.TryParse(minTextFragmentSizeTextBox.Text, out int minSize) && minSize >= 0)
                {
                    ConfigManager.Instance.SetMinTextFragmentSize(minSize);
                    Console.WriteLine($"Minimum text fragment size set to: {minSize}");

                    // Reset the hash to force new OCR processing
                    Logic.Instance.ResetHash();
                }
                else
                {
                    // Reset to current value from config if invalid
                    minTextFragmentSizeTextBox.Text = ConfigManager.Instance.GetMinTextFragmentSize().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating minimum text fragment size: {ex.Message}");
            }
        }

        private void TextSimilarThresholdTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                // Get last threshold value from config
                string lastThreshold = Convert.ToString(ConfigManager.Instance.GetTextSimilarThreshold());

                // Validate input is a valid number
                if (!double.TryParse(textSimilarThresholdTextBox.Text, System.Globalization.CultureInfo.InvariantCulture, out double similarThreshold))
                {
                    MessageBox.Show("Please enter a valid number for the threshold.",
                                "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);

                    // Reset textbox to last valid value
                    textSimilarThresholdTextBox.Text = lastThreshold;
                    return;
                }

                // Check range
                if (similarThreshold > 1.0 || similarThreshold < 0.5)
                {
                    // Show warning message
                    MessageBox.Show("Please enter a value between 0.5 and 1.0",
                                "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);

                    // Reset to current value from config
                    textSimilarThresholdTextBox.Text = lastThreshold;
                    Console.WriteLine($"Text similar threshold reset to default value: {lastThreshold}");
                    return;
                }

                // If we get here, the value is valid, so save it
                ConfigManager.Instance.SetTextSimilarThreshold(similarThreshold.ToString());
                Console.WriteLine($"Text similar threshold updated to: {similarThreshold}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating text similar threshold: {ex}");

                // Restore last known good value in case of any error
                textSimilarThresholdTextBox.Text = Convert.ToString(ConfigManager.Instance.GetTextSimilarThreshold());
            }
        }

        private void MinLetterConfidenceTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (double.TryParse(minLetterConfidenceTextBox.Text, out double confidence) && confidence >= 0 && confidence <= 1)
                {
                    ConfigManager.Instance.SetMinLetterConfidence(confidence);
                    Console.WriteLine($"Minimum letter confidence set to: {confidence}");

                    // Reset the hash to force new OCR processing
                    Logic.Instance.ResetHash();
                }
                else
                {
                    // Reset to current value from config if invalid
                    minLetterConfidenceTextBox.Text = ConfigManager.Instance.GetMinLetterConfidence().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating minimum letter confidence: {ex.Message}");
            }
        }

        private void MinLineConfidenceTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (double.TryParse(minLineConfidenceTextBox.Text, out double confidence) && confidence >= 0 && confidence <= 1)
                {
                    ConfigManager.Instance.SetMinLineConfidence(confidence);
                    Console.WriteLine($"Minimum line confidence set to: {confidence}");

                    // Reset the hash to force new OCR processing
                    Logic.Instance.ResetHash();
                }
                else
                {
                    // Reset to current value from config if invalid
                    minLineConfidenceTextBox.Text = ConfigManager.Instance.GetMinLineConfidence().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating minimum line confidence: {ex.Message}");
            }
        }

        private void CheckLanguagePackButton_Click(object sender, RoutedEventArgs e)
        {
            string? sourceLanguage = null;
    
            if (sourceLanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                sourceLanguage = selectedItem.Content.ToString();
            }
            if (string.IsNullOrEmpty(sourceLanguage))
            {
                _isLanguagePackInstall = false;

                MessageBox.Show("No language selected.", "Language Pack Check", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                Console.WriteLine($"Checking language pack for: {sourceLanguage}");

                _isLanguagePackInstall = WindowsOCRManager.Instance.CheckLanguagePackInstall(sourceLanguage);

                if (_isLanguagePackInstall)
                {
                    MessageBox.Show("Language pack is installed.", "Language Pack Check", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    if (!string.IsNullOrEmpty(WindowsOCRManager.Instance._currentLanguageCode))
                    {
                        string message = "Language pack is not installed. \n\n" +
                                     "To install the corresponding language pack, please follow these steps:\n\n" +
                                     "Step 1: Press \"Windows + S\" button, type \"language settings\" and press Enter button.\n\n" +
                                     "Step 2: Click on \"Add a language\" button.\n\n" +
                                     $"Step 3: Type \"{WindowsOCRManager.Instance._currentLanguageCode}\" to add, click \"Next\" and click \"install\".\n\n" +
                                     "Wait for language package install complete and retry";

                        MessageBox.Show(message, "Language Pack Check", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show("This language is not supported for WindowsOCR", "Language Pack Check", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            
        }

        // Handle Clear Context button click
        private void ClearContextButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Console.WriteLine("Clearing translation context and history");

                // Clear translation history in MainWindow
                MainWindow.Instance.ClearTranslationHistory();

                // Reset hash to force new translation on next capture
                Logic.Instance.ResetHash();

                // Clear any existing text objects
                Logic.Instance.ClearAllTextObjects();

                // Show success message
                MessageBox.Show("Translation context and history have been cleared.",
                    "Context Cleared", MessageBoxButton.OK, MessageBoxImage.Information);

                Console.WriteLine("Translation context cleared successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing translation context: {ex.Message}");
                MessageBox.Show($"Error clearing context: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Ignore Phrases methods

        // Load ignore phrases from ConfigManager
        private void LoadIgnorePhrases()
        {
            try
            {
                _ignorePhrases.Clear();

                // Get phrases from ConfigManager
                var phrases = ConfigManager.Instance.GetIgnorePhrases();

                // Add each phrase to the collection
                foreach (var (phrase, exactMatch) in phrases)
                {
                    if (!string.IsNullOrEmpty(phrase))
                    {
                        _ignorePhrases.Add(new IgnorePhrase(phrase, exactMatch));
                    }
                }

                // Set the ListView's ItemsSource
                ignorePhraseListView.ItemsSource = _ignorePhrases;

                Console.WriteLine($"Loaded {_ignorePhrases.Count} ignore phrases");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading ignore phrases: {ex.Message}");
            }
        }

        // Save all ignore phrases to ConfigManager
        private void SaveIgnorePhrases()
        {
            try
            {
                if (_isInitializing)
                    return;

                // Convert collection to list of tuples
                var phrases = _ignorePhrases.Select(p => (p.Phrase, p.ExactMatch)).ToList();

                // Save to ConfigManager
                ConfigManager.Instance.SaveIgnorePhrases(phrases);

                // Force the Logic to refresh
                Logic.Instance.ResetHash();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving ignore phrases: {ex.Message}");
            }
        }

        // Add a new ignore phrase
        private void AddIgnorePhraseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string phrase = newIgnorePhraseTextBox.Text.Trim();

                if (string.IsNullOrEmpty(phrase))
                {
                    MessageBox.Show("Please enter a phrase to ignore.",
                        "Missing Phrase", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Check if the phrase already exists
                if (_ignorePhrases.Any(p => p.Phrase == phrase))
                {
                    MessageBox.Show($"The phrase '{phrase}' is already in the list.",
                        "Duplicate Phrase", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                bool exactMatch = newExactMatchCheckBox.IsChecked ?? true;

                // Add to the collection
                _ignorePhrases.Add(new IgnorePhrase(phrase, exactMatch));

                // Save to ConfigManager
                SaveIgnorePhrases();

                // Clear the input
                newIgnorePhraseTextBox.Text = "";

                Console.WriteLine($"Added ignore phrase: '{phrase}' (Exact Match: {exactMatch})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding ignore phrase: {ex.Message}");
                MessageBox.Show($"Error adding phrase: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Remove a selected ignore phrase
        private void RemoveIgnorePhraseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ignorePhraseListView.SelectedItem is IgnorePhrase selectedPhrase)
                {
                    string phrase = selectedPhrase.Phrase;

                    // Ask for confirmation
                    MessageBoxResult result = MessageBox.Show($"Are you sure you want to remove the phrase '{phrase}'?",
                        "Confirm Remove", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Remove from the collection
                        _ignorePhrases.Remove(selectedPhrase);

                        // Save to ConfigManager
                        SaveIgnorePhrases();

                        Console.WriteLine($"Removed ignore phrase: '{phrase}'");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing ignore phrase: {ex.Message}");
                MessageBox.Show($"Error removing phrase: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Handle selection changed event
        private void IgnorePhraseListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Enable or disable the Remove button based on selection
            removeIgnorePhraseButton.IsEnabled = ignorePhraseListView.SelectedItem != null;
        }

        // Handle checkbox changed event
        private void IgnorePhrase_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;

                if (sender is System.Windows.Controls.CheckBox checkbox && checkbox.Tag is string phrase)
                {
                    bool exactMatch = checkbox.IsChecked ?? false;

                    // Find and update the phrase in the collection
                    foreach (var ignorePhrase in _ignorePhrases)
                    {
                        if (ignorePhrase.Phrase == phrase)
                        {
                            ignorePhrase.ExactMatch = exactMatch;

                            // Save to ConfigManager
                            SaveIgnorePhrases();

                            Console.WriteLine($"Updated ignore phrase: '{phrase}' (Exact Match: {exactMatch})");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ignore phrase: {ex.Message}");
            }
        }

        private void AudioProcessingProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (audioProcessingProviderComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                ConfigManager.Instance.SetAudioProcessingProvider(selectedItem.Content.ToString() ?? "OpenAI Realtime API");
            }
        }

        private void OpenAiRealtimeApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ConfigManager.Instance.SetOpenAiRealtimeApiKey(openAiRealtimeApiKeyPasswordBox.Password.Trim());
        }

        // Handle Auto-translate checkbox change for audio service
        private void AudioServiceAutoTranslateCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool enabled = audioServiceAutoTranslateCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetAudioServiceAutoTranslateEnabled(enabled);
            Console.WriteLine($"Settings window: Audio service auto-translate set to {enabled}");
        }
    }
}