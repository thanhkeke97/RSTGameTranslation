using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;

namespace RSTGameTranslation
{
    /// <summary>
    /// Interaction logic for QuickstartWindow.xaml
    /// </summary>
    public partial class QuickstartWindow : Window
    {
        private int currentStep = 1;
        private const int TotalSteps = 6;
        private ConfigManager configManager;
        private bool LoadedLanguageSettings = false;
        private bool LoadedOcrSettings = false;
        private bool LoadedTranslationSettings = false;

        public QuickstartWindow()
        {
            InitializeComponent();
            configManager = ConfigManager.Instance;

            // Set initial step
            NavigateToStep(1);

            // Center window on screen
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        private void NavigateToStep(int step)
        {
            currentStep = step;

            // Update progress indicator
            UpdateProgressIndicator();

            // Hide all content panels
            WelcomePanel.Visibility = Visibility.Collapsed;
            LanguagePanel.Visibility = Visibility.Collapsed;
            OcrPanel.Visibility = Visibility.Collapsed;
            TranslationPanel.Visibility = Visibility.Collapsed;
            CompletePanel.Visibility = Visibility.Collapsed;
            CondaSetupPanel.Visibility = Visibility.Collapsed;

            // Show the appropriate panel based on the current step
            switch (step)
            {
                case 1:
                    WelcomePanel.Visibility = Visibility.Visible;
                    PrevButton.IsEnabled = false;
                    break;
                case 2:
                    CondaSetupPanel.Visibility = Visibility.Visible;
                    PrevButton.IsEnabled = true;
                    break;
                case 3:
                    LanguagePanel.Visibility = Visibility.Visible;
                    PrevButton.IsEnabled = true;
                    if (!LoadedLanguageSettings)
                    {
                        LoadLanguageSettings();
                    }
                    break;
                case 4:
                    OcrPanel.Visibility = Visibility.Visible;
                    PrevButton.IsEnabled = true;
                    if (!LoadedOcrSettings)
                    {
                        LoadOcrSettings();
                    }
                    break;
                case 5:
                    TranslationPanel.Visibility = Visibility.Visible;
                    PrevButton.IsEnabled = true;
                    if (!LoadedTranslationSettings)
                    {
                        LoadTranslationSettings();
                    }
                    break;
                case 6:
                    CompletePanel.Visibility = Visibility.Visible;
                    PrevButton.IsEnabled = true;
                    NextButton.Visibility = Visibility.Collapsed;
                    FinishButton.Visibility = Visibility.Visible;
                    LoadSummarySettings();
                    break;
            }
        }

        private void UpdateProgressIndicator()
        {
            // Reset all step indicators
            Step1Indicator.Background = new SolidColorBrush(Color.FromRgb(100, 100, 100));
            Step2Indicator.Background = new SolidColorBrush(Color.FromRgb(100, 100, 100));
            Step3Indicator.Background = new SolidColorBrush(Color.FromRgb(100, 100, 100));
            Step4Indicator.Background = new SolidColorBrush(Color.FromRgb(100, 100, 100));
            Step5Indicator.Background = new SolidColorBrush(Color.FromRgb(100, 100, 100));

            // Highlight current step
            switch (currentStep)
            {
                case 1:
                    Step1Indicator.Background = new SolidColorBrush(Color.FromRgb(52, 152, 219)); // Blue
                    break;
                case 2:
                    Step2Indicator.Background = new SolidColorBrush(Color.FromRgb(52, 152, 219)); // Blue
                    break;
                case 3:
                    Step3Indicator.Background = new SolidColorBrush(Color.FromRgb(52, 152, 219)); // Blue
                    break;
                case 4:
                    Step4Indicator.Background = new SolidColorBrush(Color.FromRgb(52, 152, 219)); // Blue
                    break;
                case 5:
                    Step5Indicator.Background = new SolidColorBrush(Color.FromRgb(52, 152, 219)); // Blue
                    break;
            }
        }

        #region Language Settings

        private void LoadLanguageSettings()
        {
            // Load source and target languages from config
            string sourceLanguage = configManager.GetSourceLanguage();
            string targetLanguage = configManager.GetTargetLanguage();

            // Populate language dropdowns
            PopulateLanguageComboBox(SourceLanguageComboBox);
            PopulateLanguageComboBox(TargetLanguageComboBox);

            // Set current selections
            SourceLanguageComboBox.SelectedItem = sourceLanguage;
            TargetLanguageComboBox.SelectedItem = targetLanguage;
            LoadedLanguageSettings = true;
        }

        private void PopulateLanguageComboBox(System.Windows.Controls.ComboBox comboBox)
        {
            comboBox.Items.Clear();

            // List of supported languages
            List<string> languages = new List<string>
            {
                "ja",
                "en",
                "ch_sim",
                "ch_tra",
                "ko",
                "vi",
                "fr",
                "ru",
                "de",
                "es",
                "it",
                "hi",
                "pt",
                "ar",
                "nl",
                "pl",
                "ro",
                "fa",
                "cs",
                "id",
                "th"
            };

            // Sort languages alphabetically
            languages.Sort();

            // Add to ComboBox
            foreach (string language in languages)
            {
                comboBox.Items.Add(language);
            }
        }


        private void CommonPair_Click(object sender, RoutedEventArgs e)
        {
            Button? button = sender as Button;
            if (button != null && button.Tag != null)
            {
                string[] languages = button.Tag.ToString().Split(',');
                if (languages.Length == 2)
                {
                    // Find and select the languages in the comboboxes
                    SourceLanguageComboBox.SelectedItem = languages[0];
                    TargetLanguageComboBox.SelectedItem = languages[1];
                }
                else
                {
                    // Default language select 
                    SourceLanguageComboBox.SelectedItem = "en";
                    TargetLanguageComboBox.SelectedItem = "vi";
                }
            }
        }

        #endregion

        #region OCR Settings

        private void LoadOcrSettings()
        {
            // Load OCR method from config
            string ocrMethod = configManager.GetOcrMethod();

            // Populate OCR method dropdown
            OcrMethodComboBox.Items.Clear();
            OcrMethodComboBox.Items.Add("Windows OCR");
            OcrMethodComboBox.Items.Add("EasyOCR");
            OcrMethodComboBox.Items.Add("PaddleOCR");
            OcrMethodComboBox.Items.Add("RapidOCR");

            // Set current selection
            switch (ocrMethod.ToLower())
            {
                case "windowsocr":
                    OcrMethodComboBox.SelectedItem = "Windows OCR";
                    break;
                case "easyocr":
                    OcrMethodComboBox.SelectedItem = "EasyOCR";
                    break;
                case "paddleocr":
                    OcrMethodComboBox.SelectedItem = "PaddleOCR";
                    break;
                case "rapidocr":
                    OcrMethodComboBox.SelectedItem = "RapidOCR";
                    break;
                default:
                    OcrMethodComboBox.SelectedItem = "Windows OCR";
                    break;
            }
            LoadedOcrSettings = true;
        }

        private async void SetupOcrServer_Click(object sender, RoutedEventArgs e)
        {
            Button? button = sender as Button;
            if (button != null)
            {
                string? ocrMethod = OcrMethodComboBox.SelectedItem.ToString();
                setupOCR.Content = $"Setup OCR Server for {ocrMethod}";
                if (string.IsNullOrEmpty(ocrMethod))
                {
                    ocrMethod = "Windows OCR";
                }

                if (ocrMethod == "Windows OCR")
                {
                    System.Windows.MessageBox.Show($"{ocrMethod} doesn't require installing a environment.", "Warning!!", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Show setup dialog
                MessageBoxResult result = System.Windows.MessageBox.Show(
                    $"Are you sure you want to install the environment for {ocrMethod}?\n\n" +
                    "This process may take a long time and requires an internet connection",
                    "Confirm installation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {

                    // Run setup
                    await Task.Run(() =>
                    {
                        OcrServerManager.Instance.SetupOcrEnvironment(ocrMethod);
                    });

                }
                // Select this OCR method in the ComboBox
                OcrMethodComboBox.SelectedItem = ocrMethod;
            }

        }



        #endregion

        #region Translation Settings

        private void LoadTranslationSettings()
        {
            // Load translation service from config
            string translationService = configManager.GetCurrentTranslationService();

            // Populate translation service dropdown
            TranslationServiceComboBox.Items.Clear();
            TranslationServiceComboBox.Items.Add("Google Translate");
            TranslationServiceComboBox.Items.Add("ChatGPT");
            TranslationServiceComboBox.Items.Add("Gemini");
            TranslationServiceComboBox.Items.Add("Mistral");
            TranslationServiceComboBox.Items.Add("Ollama");

            // Set current selection
            switch (translationService.ToLower())
            {
                case "googletranslate":
                    TranslationServiceComboBox.SelectedItem = "Google Translate";
                    break;
                case "chatgpt":
                    TranslationServiceComboBox.SelectedItem = "ChatGPT";
                    break;
                case "gemini":
                    TranslationServiceComboBox.SelectedItem = "Gemini";
                    break;
                case "mistral":
                    TranslationServiceComboBox.SelectedItem = "Mistral";
                    break;
                case "ollama":
                    TranslationServiceComboBox.SelectedItem = "Ollama";
                    break;
                default:
                    TranslationServiceComboBox.SelectedItem = "Google Translate";
                    break;
            }

            // Load API keys
            GoogleTranslateApiKeyPasswordBox.Password = configManager.GetGoogleTranslateApiKey() ?? "";
            ChatGptApiKeyPasswordBox.Password = configManager.GetChatGptApiKey() ?? "";
            GeminiApiKeyPasswordBox.Password = configManager.GetGeminiApiKey() ?? "";
            MistralApiKeyPasswordBox.Password = configManager.GetMistralApiKey() ?? "";

            // Load ChatGPT models
            ChatGptModelComboBox.Items.Clear();
            ChatGptModelComboBox.Items.Add("gpt-3.5-turbo");
            ChatGptModelComboBox.Items.Add("gpt-4o-mini");
            ChatGptModelComboBox.Items.Add("gpt-4.1-nano");
            ChatGptModelComboBox.Items.Add("gpt-4o");

            // Load Gemini models
            GeminiModelComboBox.Items.Clear();
            GeminiModelComboBox.Items.Add("gemma-3-12b-it");
            GeminiModelComboBox.Items.Add("gemini-2.0-flash-lite");
            GeminiModelComboBox.Items.Add("gemini-2.5-flash-lite");

            // Load Mistral models
            MistralModelComboBox.Items.Clear();
            MistralModelComboBox.Items.Add("mistral-medium");
            MistralModelComboBox.Items.Add("open-mistral-nemo");
            MistralModelComboBox.Items.Add("mistral-small-2503");

            // Set selected ChatGPT model
            string chatGptModel = configManager.GetChatGptModel();
            if (!string.IsNullOrEmpty(chatGptModel))
            {
                ChatGptModelComboBox.SelectedItem = chatGptModel;
            }
            else
            {
                ChatGptModelComboBox.SelectedItem = "gpt-3.5-turbo";
            }

            // Set selected Gemini model
            string geminiModel = configManager.GetGeminiModel();
            if (!string.IsNullOrEmpty(geminiModel))
            {
                GeminiModelComboBox.SelectedItem = geminiModel;
            }
            else
            {
                GeminiModelComboBox.SelectedItem = "gemini-2.0-flash-lite";
            }

            // Set selected Gemini model
            string mistralModel = configManager.GetMistralModel();
            if (!string.IsNullOrEmpty(mistralModel))
            {
                MistralModelComboBox.SelectedItem = mistralModel;
            }
            else
            {
                MistralModelComboBox.SelectedItem = "open-mistral-nemo";
            }

            // Load Ollama model
            string ollamaModel = configManager.GetOllamaModel();
            if (!string.IsNullOrEmpty(ollamaModel))
            {
                OllamaModelTextBox.Text = ollamaModel;
            }
            else
            {
                OllamaModelTextBox.Text = "llama3";
            }

            // Update visibility of API key fields based on selected service
            UpdateApiKeyFieldsVisibility();
            LoadedTranslationSettings = true;
        }

        private void TranslationServiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TranslationServiceComboBox.SelectedItem != null)
            {

                // Update visibility of API key fields
                UpdateApiKeyFieldsVisibility();
            }
        }

        private void UpdateApiKeyFieldsVisibility()
        {
            // Hide all API key fields
            GoogleTranslateApiKeyPanel.Visibility = Visibility.Collapsed;
            ChatGptApiKeyPanel.Visibility = Visibility.Collapsed;
            GeminiApiKeyPanel.Visibility = Visibility.Collapsed;
            MistralApiKeyPanel.Visibility = Visibility.Collapsed;
            OllamaModelPanel.Visibility = Visibility.Collapsed;

            // Show the appropriate API key field based on the selected service
            if (TranslationServiceComboBox.SelectedItem != null)
            {
                string? selectedService = TranslationServiceComboBox.SelectedItem.ToString();

                switch (selectedService)
                {
                    case "Google Translate":
                        GoogleTranslateApiKeyPanel.Visibility = Visibility.Visible;
                        break;
                    case "ChatGPT":
                        ChatGptApiKeyPanel.Visibility = Visibility.Visible;
                        break;
                    case "Gemini":
                        GeminiApiKeyPanel.Visibility = Visibility.Visible;
                        break;
                    case "Mistral":
                        MistralApiKeyPanel.Visibility = Visibility.Visible;
                        break;
                    case "Ollama":
                        OllamaModelPanel.Visibility = Visibility.Visible;
                        break;
                }
            }
        }

        private void GoogleTranslateApiKeyPasswordBox_PasswordChanged(object sender, System.Windows.Input.KeyEventArgs e)
        {
            configManager.SetGoogleTranslateApiKey(GoogleTranslateApiKeyPasswordBox.Password);
        }

        private void ChatGptApiKeyPasswordBox_PasswordChanged(object sender, System.Windows.Input.KeyEventArgs e)
        {
            configManager.SetChatGptApiKey(ChatGptApiKeyPasswordBox.Password);
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (sender is PasswordBox passwordBox)
                {
                    string apiKey = passwordBox.Password.Trim();
                    string? serviceType = TranslationServiceComboBox.SelectedItem.ToString();

                    if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(serviceType))
                    {
                        // Add Api key to list
                        ConfigManager.Instance.AddApiKey(serviceType, apiKey);

                        // Clear textbox content
                        passwordBox.Password = "";

                        Console.WriteLine($"Added new API key for {serviceType}");


                        System.Windows.MessageBox.Show($"API key added for {serviceType}.", "API Key Added",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        private void GeminiApiKeyPasswordBox_PasswordChanged(object sender, System.Windows.Input.KeyEventArgs e)
        {
            configManager.SetGeminiApiKey(GeminiApiKeyPasswordBox.Password);
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (sender is PasswordBox passwordBox)
                {
                    string apiKey = passwordBox.Password.Trim();
                    string? serviceType = TranslationServiceComboBox.SelectedItem.ToString();

                    if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(serviceType))
                    {
                        // Add Api key to list
                        ConfigManager.Instance.AddApiKey(serviceType, apiKey);

                        // Clear textbox content
                        passwordBox.Password = "";

                        Console.WriteLine($"Added new API key for {serviceType}");


                        System.Windows.MessageBox.Show($"API key added for {serviceType}.", "API Key Added",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        private void MistralApiKeyPasswordBox_PasswordChanged(object sender, System.Windows.Input.KeyEventArgs e)
        {
            configManager.SetMistralApiKey(MistralApiKeyPasswordBox.Password);
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (sender is PasswordBox passwordBox)
                {
                    string apiKey = passwordBox.Password.Trim();
                    string? serviceType = TranslationServiceComboBox.SelectedItem.ToString();

                    if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(serviceType))
                    {
                        // Add Api key to list
                        ConfigManager.Instance.AddApiKey(serviceType, apiKey);

                        // Clear textbox content
                        passwordBox.Password = "";

                        Console.WriteLine($"Added new API key for {serviceType}");


                        System.Windows.MessageBox.Show($"API key added for {serviceType}.", "API Key Added",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        private void OllamaModelTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            configManager.SetOllamaModel(OllamaModelTextBox.Text);
        }

        private void GetApiKey_Click(object sender, RoutedEventArgs e)
        {
            Button? button = sender as Button;
            if (button != null && button.Tag != null)
            {
                string? url = button.Tag.ToString();
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Could not open URL: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Complete Settings

        private void LoadSummarySettings()
        {
            // Get settings from config
            string sourceLanguage = SourceLanguageComboBox.SelectedItem.ToString() ?? "en";
            string targetLanguage = TargetLanguageComboBox.SelectedItem.ToString() ?? "vi";
            string ocrMethod = OcrMethodComboBox.SelectedItem.ToString() ?? "Windows OCR";
            string translationService = TranslationServiceComboBox.SelectedItem.ToString() ?? "Google translate";

            // Format language display
            string sourceFormatted = char.ToUpper(sourceLanguage[0]) + sourceLanguage.Substring(1);
            string targetFormatted = char.ToUpper(targetLanguage[0]) + targetLanguage.Substring(1);
            LanguagesSummaryText.Text = $"{sourceFormatted} → {targetFormatted}";

            // Format OCR method display
            switch (ocrMethod.ToLower())
            {
                case "windowsocr":
                    OcrMethodSummaryText.Text = "Windows OCR";
                    break;
                case "easyocr":
                    OcrMethodSummaryText.Text = "EasyOCR";
                    break;
                case "paddleocr":
                    OcrMethodSummaryText.Text = "PaddleOCR";
                    break;
                case "rapidocr":
                    OcrMethodSummaryText.Text = "RapidOCR";
                    break;
                default:
                    OcrMethodSummaryText.Text = "Windows OCR";
                    break;
            }

            // Format translation service display
            switch (translationService.ToLower())
            {
                case "googletranslate":
                    TranslationServiceSummaryText.Text = "Google Translate";
                    break;
                case "chatgpt":
                    TranslationServiceSummaryText.Text = "ChatGPT";
                    break;
                case "gemini":
                    TranslationServiceSummaryText.Text = "Gemini";
                    break;
                case "mistral":
                    TranslationServiceSummaryText.Text = "Mistral";
                    break;
                case "ollama":
                    TranslationServiceSummaryText.Text = "Ollama";
                    break;
                default:
                    TranslationServiceSummaryText.Text = "Google Translate";
                    break;
            }
        }

        private void JoinDiscord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://discord.gg/FusrDU5tdn",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not open Discord link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Navigation

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep < TotalSteps)
            {
                NavigateToStep(currentStep + 1);
            }
            else
            {
                // Final step - show summary
                NavigateToStep(TotalSteps + 1);
            }
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (FinishButton.Visibility == Visibility.Visible)
            {
                FinishButton.Visibility = Visibility.Collapsed;
                NextButton.Visibility = Visibility.Visible;
            }
            if (currentStep > 1)
            {
                NavigateToStep(currentStep - 1);
            }
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            // Save setting to not show quickstart again if checked
            if (DontShowAgainCheckBox.IsChecked == true)
            {
                configManager.SetNeedShowQuickStart(false);
            }

            this.Close();
        }

        private void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            // Save setting to not show quickstart again if checked
            if (DontShowAgainCheckBox.IsChecked == true)
            {
                configManager.SetNeedShowQuickStart(false);
            }

            // Save setting language
            configManager.SetSourceLanguage(SourceLanguageComboBox.SelectedItem.ToString() ?? "en");
            configManager.SetTargetLanguage(TargetLanguageComboBox.SelectedItem.ToString() ?? "vi");

            // Save setting OCR method
            configManager.SetOcrMethod(OcrMethodComboBox.SelectedItem.ToString() ?? "Windows OCR");
            MainWindow.Instance.SetOcrMethod(OcrMethodComboBox.SelectedItem.ToString() ?? "Windows OCR");

            // Save setting translation services
            configManager.SetTranslationService(TranslationServiceComboBox.SelectedItem.ToString() ?? "Google translate");

            // Set model
            if (TranslationServiceComboBox.SelectedItem.ToString() == "ChatGPT")
            {
                configManager.SetChatGptModel(ChatGptModelComboBox.SelectedItem.ToString() ?? "gpt-3.5-turbo");
            }
            else if (TranslationServiceComboBox.SelectedItem.ToString() == "Gemini")
            {
                configManager.SetGeminiModel(GeminiModelComboBox.SelectedItem.ToString() ?? "gemini-2.0-flash-lite");
            }
            else if (TranslationServiceComboBox.SelectedItem.ToString() == "Mistral")
            {
                configManager.SetMistralModel(MistralModelComboBox.SelectedItem.ToString() ?? "open-mistral-nemo");
            }

            this.Close();
        }

        private void DontShowAgainCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Save setting to not show quickstart again if checked
            configManager.SetNeedShowQuickStart(false);
            Console.WriteLine($"Popup quickstart will not show again");
        }

        private async void SetupConda_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                // Show setup dialog
                MessageBoxResult result = System.Windows.MessageBox.Show(
                    $"Are you sure you want to install conda?\n\n" +
                    "This process may take a long time and requires an internet connection",
                    "Confirm installation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {

                    // Run setup
                    await Task.Run(() =>
                    {
                        OcrServerManager.Instance.InstallConda();
                    });

                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error installing OCR server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}