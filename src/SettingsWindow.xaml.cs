using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Navigation;
using System.Windows.Media.Animation;
using System.Diagnostics;
using System.Collections.ObjectModel;
using ComboBox = System.Windows.Controls.ComboBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using MessageBox = System.Windows.MessageBox;
using System.Globalization;
using System.Windows.Forms;
using System.Windows.Interop;
using RST;
using Windows.Media.SpeechSynthesis;
using System.IO;

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

        public string profileName = "";

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
            LoadAvailableScreens();
            LoadAllProfile();
            LoadAllAudioProcessingModel();
            LoadAvailableWindowTTSVoice();

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

        // Reload setting for setting windows
        public void ReloadSetting()
        {
            _instance = this;
            LoadAvailableScreens();
            LoadAllProfile();
            // LoadAllAudioProcessingModel();
            LoadAvailableWindowTTSVoice();
            SettingsWindow_Loaded(null, null);
        }

        // Show message for multi selection are
        private bool isNeedShowMessage = false;

        // Show message for Auto OCR
        private bool isNeedShowWarningAutoOCR = false;
        // Show message for Manga Mode
        // private bool isNeedShowWarningMangaMode = false;

        // Flag to prevent saving during initialization
        private static bool _isInitializing = true;

        // Collection to hold the ignore phrases
        private ObservableCollection<IgnorePhrase> _ignorePhrases = new ObservableCollection<IgnorePhrase>();

        private void SettingsWindow_Loaded(object? sender, RoutedEventArgs? e)
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
                // Set default values
                hotKeyFunctionComboBox.SelectedIndex = 0;
                combineKey2.SelectedIndex = 0;
                combineKey1.SelectedIndex = 0;

                // Set selected screen from config
                int selectedScreenIndex = ConfigManager.Instance.GetSelectedScreenIndex();
                if (selectedScreenIndex >= 0 && selectedScreenIndex < screenComboBox.Items.Count)
                {
                    screenComboBox.SelectedIndex = selectedScreenIndex;
                }
                else if (screenComboBox.Items.Count > 0)
                {
                    // Default to first screen if saved index is invalid
                    screenComboBox.SelectedIndex = 0;
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

        // Allow MainWindow to enable/disable the setup button in this window
        public void SetSetupButtonEnabled(bool enabled)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => SetSetupButtonEnabled(enabled));
                    return;
                }

                if (btnSetupOcrServer != null)
                {
                    btnSetupOcrServer.IsEnabled = enabled;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting setup button enabled: {ex.Message}");
            }
        }

        // Click handler for the setup button placed in Settings window
        private async void Settings_BtnSetupOcrServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Delegate the actual setup flow to MainWindow (shared logic)
                await MainWindow.Instance.SetupOcrServerAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error invoking setup from SettingsWindow: {ex.Message}");
            }
        }
        // Load list of available window TTS voice
        private void LoadAvailableWindowTTSVoice()
        {
            try
            {
                // Clear any existing items
                windowTTSVoiceComboBox.Items.Clear();

                // Get all available voices from WindowsTTSService
                var availableVoices = WindowsTTSService.GetInstalledVoiceNames();

                if (availableVoices.Count == 0)
                {
                    // Add a placeholder item if no voices are available
                    ComboBoxItem noVoicesItem = new ComboBoxItem
                    {
                        Content = "No voices available",
                        IsEnabled = false
                    };
                    windowTTSVoiceComboBox.Items.Add(noVoicesItem);
                    Console.WriteLine("No Windows TTS voices found");
                }
                else
                {
                    // Group voices by language
                    var groupedVoices = new Dictionary<string, List<string>>();

                    foreach (string voiceName in availableVoices)
                    {
                        // Extract language information from voice
                        string languageCode = "Other";


                        int startIndex = voiceName.IndexOf('(');
                        if (startIndex > 0)
                        {
                            int endIndex = voiceName.IndexOf(',', startIndex);
                            if (endIndex > startIndex)
                            {
                                languageCode = voiceName.Substring(startIndex + 1, endIndex - startIndex - 1).Trim();
                            }
                        }

                        // Add to group
                        if (!groupedVoices.ContainsKey(languageCode))
                        {
                            groupedVoices[languageCode] = new List<string>();
                        }
                        groupedVoices[languageCode].Add(voiceName);
                    }

                    // Prefer show VN language
                    List<string> languagePriority = new List<string> { "vi-VN", "Vietnamese" };

                    foreach (string priorityLang in languagePriority)
                    {
                        if (groupedVoices.ContainsKey(priorityLang))
                        {
                            foreach (string voiceName in groupedVoices[priorityLang])
                            {
                                ComboBoxItem item = new ComboBoxItem
                                {
                                    Content = voiceName
                                };
                                windowTTSVoiceComboBox.Items.Add(item);
                            }
                            groupedVoices.Remove(priorityLang);
                        }
                    }

                    foreach (var group in groupedVoices)
                    {
                        foreach (string voiceName in group.Value)
                        {
                            ComboBoxItem item = new ComboBoxItem
                            {
                                Content = voiceName
                            };
                            windowTTSVoiceComboBox.Items.Add(item);
                        }
                    }

                    // Try to select the current voice from config
                    string currentVoice = ConfigManager.Instance.GetWindowsTtsVoice();
                    bool foundVoice = false;

                    // First try to find an exact match
                    foreach (ComboBoxItem item in windowTTSVoiceComboBox.Items)
                    {
                        if (string.Equals(item.Content?.ToString(), currentVoice, StringComparison.OrdinalIgnoreCase))
                        {
                            windowTTSVoiceComboBox.SelectedItem = item;
                            foundVoice = true;
                            Console.WriteLine($"Selected voice from config: {currentVoice}");
                            break;
                        }
                    }

                    // If the configured voice wasn't found, try to find a Vietnamese voice
                    if (!foundVoice)
                    {
                        foreach (ComboBoxItem item in windowTTSVoiceComboBox.Items)
                        {
                            string? itemContent = item.Content?.ToString();
                            if (itemContent != null &&
                                (itemContent.Contains("Vietnamese") ||
                                itemContent.Contains("vi-VN") ||
                                itemContent.Contains("An")))
                            {
                                windowTTSVoiceComboBox.SelectedItem = item;
                                foundVoice = true;
                                Console.WriteLine($"Selected Vietnamese voice: {itemContent}");
                                break;
                            }
                        }
                    }

                    // If still no voice selected, try to get the default system voice
                    if (!foundVoice)
                    {
                        string? defaultVoice = WindowsTTSService.GetDefaultSystemVoice();

                        if (!string.IsNullOrEmpty(defaultVoice))
                        {
                            foreach (ComboBoxItem item in windowTTSVoiceComboBox.Items)
                            {
                                if (string.Equals(item.Content?.ToString(), defaultVoice, StringComparison.OrdinalIgnoreCase))
                                {
                                    windowTTSVoiceComboBox.SelectedItem = item;
                                    foundVoice = true;
                                    Console.WriteLine($"Selected default system voice: {defaultVoice}");
                                    break;
                                }
                            }
                        }

                        // If still no voice selected, select the first one
                        if (!foundVoice && windowTTSVoiceComboBox.Items.Count > 0)
                        {
                            windowTTSVoiceComboBox.SelectedIndex = 0;
                            ComboBoxItem? firstItem = windowTTSVoiceComboBox.SelectedItem as ComboBoxItem;
                            Console.WriteLine($"Selected first available voice: {firstItem?.Content}");
                        }
                    }
                }

                Console.WriteLine($"Loaded {availableVoices.Count} Windows TTS voices");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading Windows TTS voices: {ex.Message}");
                MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_ErrorLoadingTTSVoices"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        // Load list of available screens with display name and resolution
        private void LoadAvailableScreens()
        {
            try
            {
                // Clear existing items
                screenComboBox.Items.Clear();

                // Get all screens
                var screens = System.Windows.Forms.Screen.AllScreens;

                // Add each screen to the combo box
                for (int i = 0; i < screens.Length; i++)
                {
                    var screen = screens[i];

                    // Get native resolution
                    int width = screen.Bounds.Width;
                    int height = screen.Bounds.Height;

                    // Get friendly display name
                    string displayName = GetFriendlyScreenName(screen.DeviceName);

                    // Create display info
                    string screenInfo = $"{displayName} ({width} x {height})";
                    if (screen.Primary)
                    {
                        screenInfo += " (Primary)";
                    }

                    // Create combo box item
                    ComboBoxItem item = new ComboBoxItem
                    {
                        Content = screenInfo,
                        Tag = i  // Store screen index as Tag
                    };


                    screenComboBox.Items.Add(item);

                    Console.WriteLine($"Screen {i}: {displayName}, {width}x{height}, Primary: {screen.Primary}");
                }

                // Select the primary screen by default
                for (int i = 0; i < screenComboBox.Items.Count; i++)
                {
                    if (screenComboBox.Items[i] is ComboBoxItem item &&
                        item.Content.ToString()?.Contains("Primary") == true)
                    {
                        screenComboBox.SelectedIndex = i;
                        break;
                    }
                }

                // If no primary screen was found, select the first item
                if (screenComboBox.SelectedIndex == -1 && screenComboBox.Items.Count > 0)
                {
                    screenComboBox.SelectedIndex = 0;
                }

                Console.WriteLine($"Loaded {screenComboBox.Items.Count} screens");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading available screens: {ex.Message}");
            }
        }
        // Get friendly name for a screen device
        private string GetFriendlyScreenName(string deviceName)
        {
            try
            {
                // Extract the device name from the full path (e.g., \\.\DISPLAY1)
                string shortDeviceName = deviceName.Substring(deviceName.LastIndexOf('\\') + 1);

                // Try multiple methods to get the friendly name

                // Method 1: Using EnumDisplayDevices with the device name
                DISPLAY_DEVICE displayDevice = new DISPLAY_DEVICE();
                displayDevice.cb = Marshal.SizeOf(displayDevice);

                if (EnumDisplayDevices(deviceName, 0, ref displayDevice, 0))
                {
                    if (!string.IsNullOrEmpty(displayDevice.DeviceString))
                    {
                        return $"{shortDeviceName} - {displayDevice.DeviceString}";
                    }
                }

                uint deviceIndex = 0;
                DISPLAY_DEVICE device = new DISPLAY_DEVICE();
                device.cb = Marshal.SizeOf(device);

                while (EnumDisplayDevices(null, deviceIndex, ref device, 0))
                {
                    if (device.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(device.DeviceString))
                        {
                            return $"{shortDeviceName} - {device.DeviceString}";
                        }
                        break;
                    }

                    // Try to get monitor info for this adapter
                    DISPLAY_DEVICE monitor = new DISPLAY_DEVICE();
                    monitor.cb = Marshal.SizeOf(monitor);
                    uint monitorIndex = 0;

                    while (EnumDisplayDevices(device.DeviceName, monitorIndex, ref monitor, 0))
                    {
                        if (monitor.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(monitor.DeviceString))
                            {
                                return $"{shortDeviceName} - {monitor.DeviceString}";
                            }
                            break;
                        }
                        monitorIndex++;
                        monitor.cb = Marshal.SizeOf(monitor);
                    }

                    deviceIndex++;
                    device.cb = Marshal.SizeOf(device);
                }


                try
                {
                    using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_DesktopMonitor"))
                    {
                        foreach (var monitor in searcher.Get())
                        {
                            string monitorName = monitor["Name"]?.ToString() ?? "";
                            string monitorDeviceId = monitor["DeviceID"]?.ToString() ?? "";
                            string monitorDescription = monitor["Description"]?.ToString() ?? "";

                            if (!string.IsNullOrEmpty(monitorName) && monitorName != "Default Monitor")
                            {
                                return $"{shortDeviceName} - {monitorName}";
                            }
                            else if (!string.IsNullOrEmpty(monitorDescription) && monitorDescription != "Default Monitor")
                            {
                                return $"{shortDeviceName} - {monitorDescription}";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting monitor name via WMI: {ex.Message}");
                }

                // Fallback to just the device name if we couldn't get the friendly name
                return shortDeviceName;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting friendly screen name: {ex.Message}");
                return deviceName;
            }
        }

        // P/Invoke declarations for getting display device information
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DISPLAY_DEVICE
        {
            [MarshalAs(UnmanagedType.U4)]
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            [MarshalAs(UnmanagedType.U4)]
            public DisplayDeviceStateFlags StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [Flags]
        private enum DisplayDeviceStateFlags : int
        {
            AttachedToDesktop = 0x1,
            MultiDriver = 0x2,
            PrimaryDevice = 0x4,
            MirroringDriver = 0x8,
            VGACompatible = 0x16,
            Removable = 0x20,
            ModesPruned = 0x8000000,
            Remote = 0x4000000,
            Disconnect = 0x2000000
        }

        // Select screen - placeholder for now
        private void ScreenComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (screenComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    // Get the screen index from the Tag
                    if (selectedItem.Tag is int screenIndex)
                    {
                        // Save to config
                        ConfigManager.Instance.SetSelectedScreenIndex(screenIndex);
                        Console.WriteLine($"Selected screen index set to: {screenIndex}");

                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling screen selection change: {ex.Message}");
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


                        MessageBox.Show(
                            string.Format(LocalizationManager.Instance.Strings["Msg_ApiKeyAdded"], serviceType),
                            LocalizationManager.Instance.Strings["Title_ApiKeyAdded"],
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
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

        private void SaveApiKeysButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get current service type
                string serviceType = ConfigManager.Instance.GetCurrentTranslationService();

                // Get the corresponding PasswordBox
                PasswordBox? passwordBox = null;

                switch (serviceType)
                {
                    case "Gemini":
                        passwordBox = geminiApiKeyPasswordBox;
                        break;
                    case "ChatGPT":
                        passwordBox = chatGptApiKeyPasswordBox;
                        break;
                    case "Mistral":
                        passwordBox = mistralApiKeyPasswordBox;
                        break;
                    case "Groq":
                        passwordBox = groqApiKeyPasswordBox;
                        break;
                    case "Custom API":
                        passwordBox = customApiKeyPasswordBox;
                        break;
                }

                if (passwordBox != null)
                {
                    string apiKey = passwordBox.Password.Trim();

                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        // add Api key to list
                        ConfigManager.Instance.AddApiKey(serviceType, apiKey);

                        // Delete content in textbox
                        passwordBox.Password = "";

                        Console.WriteLine($"Added new API key for {serviceType}");

                        MessageBox.Show(
                            string.Format(LocalizationManager.Instance.Strings["Msg_ApiKeyAdded"], serviceType),
                            LocalizationManager.Instance.Strings["Title_ApiKeyAdded"],
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    }
                    else
                    {
                        MessageBox.Show(
                            LocalizationManager.Instance.Strings["Msg_PleaseEnterApiKey"],
                            LocalizationManager.Instance.Strings["Title_EmptyApiKey"],
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                    }
                }
                else
                {
                    MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_NoPasswordField"], serviceType),
                        LocalizationManager.Instance.Strings["Title_ConfigurationError"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving API key: {ex.Message}");
                MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_ErrorSavingApiKey"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
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
            minLetterConfidenceTextBox.Text = ConfigManager.Instance.GetMinLetterConfidence().ToString(CultureInfo.InvariantCulture);
            minLineConfidenceTextBox.Text = ConfigManager.Instance.GetMinLineConfidence().ToString(CultureInfo.InvariantCulture);

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
            ListHotKey_TextChanged();

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
            textSimilarThresholdTextBox.Text = ConfigManager.Instance.GetTextSimilarThreshold().ToString(CultureInfo.InvariantCulture);

            // Set char level from config
            charLevelCheckBox.IsChecked = ConfigManager.Instance.IsCharLevelEnabled();

            // Set show icon signal
            showIconSignalCheckBox.IsChecked = ConfigManager.Instance.IsShowIconSignalEnabled();

            // Set auto set overlay background color
            autoSetOverlayBackgroundColorcheckBox.IsChecked = ConfigManager.Instance.IsAutoSetOverlayBackground();

            // Set auto merge overlapping text
            autoMergeOverlappingTextCheckBox.IsChecked = ConfigManager.Instance.IsAutoMergeOverlappingTextEnabled();

            // Set auto OCR
            AutoOCRCheckBox.IsChecked = ConfigManager.Instance.IsAutoOCREnabled();

            // Set HDR support
            hdrSupportCheckBox.IsChecked = ConfigManager.Instance.IsHDRSupportEnabled();

            // Set hot key enable
            hotKeyEnableCheckBox.IsChecked = ConfigManager.Instance.IsHotKeyEnabled();

            // Set audio option
            silenceThresholdTextBox.Text = ConfigManager.Instance.GetSilenceThreshold().ToString(CultureInfo.InvariantCulture);
            silenceDurationTextBox.Text = ConfigManager.Instance.GetSilenceDurationMs().ToString(CultureInfo.InvariantCulture);
            maxBufferSamplesTextBox.Text = ConfigManager.Instance.GetMaxBufferSamples().ToString(CultureInfo.InvariantCulture);
            whisperThreadCountTextBox.Text = ConfigManager.Instance.GetWhisperThreadCount().ToString(CultureInfo.InvariantCulture);

            audioProcessingModelComboBox.SelectionChanged -= AudioProcessingModelComboBox_SelectionChanged;

            // Set audio processing model
            foreach (var item in audioProcessingModelComboBox.Items)
            {
                string itemText = "";
                if (item is ComboBoxItem cbItem)
                {
                    itemText = cbItem.Content?.ToString() ?? "";
                }
                else
                {
                    itemText = item.ToString() ?? "";
                }
                if (string.Equals(itemText.Trim(), ConfigManager.Instance.GetAudioProcessingModel(), StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Found matching audio processing model: '{itemText}'");
                    audioProcessingModelComboBox.SelectedItem = item;
                    UpdateWhisperThreadCountVisibility(itemText);
                    break;
                }
            }

            audioProcessingModelComboBox.SelectionChanged += AudioProcessingModelComboBox_SelectionChanged;

            // Set Whisper runtime from config
            whisperRuntimeComboBox.SelectionChanged -= WhisperRuntimeComboBox_SelectionChanged;
            string savedRuntime = ConfigManager.Instance.GetWhisperRuntime();
            foreach (var item in whisperRuntimeComboBox.Items)
            {
                if (item is ComboBoxItem cbItem && string.Equals(cbItem.Tag?.ToString(), savedRuntime, StringComparison.OrdinalIgnoreCase))
                {
                    whisperRuntimeComboBox.SelectedItem = item;
                    break;
                }
            }
            // Set initial visibility for thread count (only show for CPU)
            UpdateWhisperThreadCountVisibility(savedRuntime);
            whisperRuntimeComboBox.SelectionChanged += WhisperRuntimeComboBox_SelectionChanged;

            // Set manga mode
            // MangaModeCheckBox.IsChecked = ConfigManager.Instance.IsMangaModeEnabled();
            // isNeedShowWarningMangaMode = true;

            // Set WindowsOCR integration
            // windowsOCRIntegrationCheckBox.IsChecked = ConfigManager.Instance.IsWindowsOCRIntegrationEnabled();
            isSendDataToServerCheckBox.IsChecked = ConfigManager.Instance.IsSendDataToServerEnabled();
            // Set multi selection area from config
            multiSelectionAreaCheckBox.IsChecked = ConfigManager.Instance.IsMultiSelectionAreaEnabled();
            if (!ConfigManager.Instance.IsMultiSelectionAreaEnabled())
            {
                isNeedShowMessage = true;
            }

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
                    if (itemText == "Windows OCR" || itemText == "OneOCR")
                    {
                        ocrButtonsPanel.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        ocrButtonsPanel.Visibility = Visibility.Visible;
                    }
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


            blockDetectionPowerTextBox.Text = BlockDetectionManager.Instance.GetBlockDetectionScale().ToString("F2", CultureInfo.InvariantCulture);
            settleTimeTextBox.Text = ConfigManager.Instance.GetBlockDetectionSettleTime().ToString("F2", CultureInfo.InvariantCulture);

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
            // Initialize API key for Custom API
            customApiKeyPasswordBox.Password = ConfigManager.Instance.GetCustomApiKey();
            // Initialize API key for Groq
            groqApiKeyPasswordBox.Password = ConfigManager.Instance.GetGroqApiKey();

            // Initialize Ollama settings
            ollamaUrlTextBox.Text = ConfigManager.Instance.GetOllamaUrl();
            ollamaPortTextBox.Text = ConfigManager.Instance.GetOllamaPort();
            ollamaModelTextBox.Text = ConfigManager.Instance.GetOllamaModel();

            // Initialize LM Studio settings
            lmstudioUrlTextBox.Text = ConfigManager.Instance.GetLMStudioUrl();
            lmstudioPortTextBox.Text = ConfigManager.Instance.GetLMStudioPort();
            lmstudioModelTextBox.Text = ConfigManager.Instance.GetLMStudioModel();

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
            windowTTSVoiceComboBox.SelectionChanged -= WindowTTSVoiceComboBox_SelectionChanged;

            // Set TTS enabled state
            ttsEnabledCheckBox.IsChecked = ConfigManager.Instance.IsTtsEnabled();

            // Set Exclude character name
            excludeCharacterNameCheckBox.IsChecked = ConfigManager.Instance.IsExcludeCharacterNameEnabled();

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

            // Set Window TTS voice
            string windowTTSvoiceId = ConfigManager.Instance.GetWindowsTtsVoice();
            foreach (ComboBoxItem item in windowTTSVoiceComboBox.Items)
            {
                if (string.Equals(item.Tag?.ToString(), windowTTSvoiceId, StringComparison.OrdinalIgnoreCase))
                {
                    windowTTSVoiceComboBox.SelectedItem = item;
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
            windowTTSVoiceComboBox.SelectionChanged += WindowTTSVoiceComboBox_SelectionChanged;

            // Load ignore phrases
            LoadIgnorePhrases();

            // Load exclude regions
            LoadExcludeRegions();

            // Audio Processing settings
            audioProcessingProviderComboBox.SelectedIndex = 0; // Only one for now
            // openAiRealtimeApiKeyPasswordBox.Password = ConfigManager.Instance.GetOpenAiRealtimeApiKey();
            // Load Auto-translate for audio service
            // audioServiceAutoTranslateCheckBox.IsChecked = ConfigManager.Instance.IsAudioServiceAutoTranslateEnabled();

            // Load Clipboard Auto-Translate settings
            clipboardAutoTranslateCheckBox.IsChecked = ConfigManager.Instance.IsClipboardAutoTranslateEnabled();
            clipboardCopyResultCheckBox.IsChecked = ConfigManager.Instance.IsClipboardCopyResultEnabled();
            clipboardDebounceTextBox.Text = ConfigManager.Instance.GetClipboardDebounceMs().ToString();
            clipboardMaxCharsTextBox.Text = ConfigManager.Instance.GetClipboardMaxChars().ToString();
        }


        private void SetHotKeyButton_Click(object sender, RoutedEventArgs e)
        {
            // Skip event if we're initializing
            if (_isInitializing)
            {
                return;
            }

            string functionName;
            string? key1 = "";
            string? key2 = "";
            string combineKey;

            // Check if selected item is a ComboBoxItem
            if (hotKeyFunctionComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                // Use Tag for internal key (not affected by localization)
                functionName = selectedItem.Tag?.ToString() ?? "Start/Stop";
                if (combineKey1.SelectedItem is ComboBoxItem selectedItem1)
                {
                    key1 = selectedItem1.Content.ToString();
                }
                if (combineKey2.SelectedItem is ComboBoxItem selectedItem2)
                {
                    key2 = selectedItem2.Content.ToString();
                }
                if (key1 == "" || key2 == "" || key1 == "----------- Select -----------" || key2 == "----------- Select -----------")
                {
                    MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_HotKeyInvalid"], functionName),
                        LocalizationManager.Instance.Strings["Title_HotKeyInvalid"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
                else
                {
                    combineKey = key1 + "+" + key2;
                    // Save HotKey
                    ConfigManager.Instance.SetHotKey(functionName, combineKey);
                    statusUpdateHotKey.Visibility = Visibility.Visible;
                    ListHotKey_TextChanged();
                    // Init keyboard hook
                    KeyboardShortcuts.InitializeGlobalHook();
                    IntPtr handle = new WindowInteropHelper(this).Handle;
                    KeyboardShortcuts.SetMainWindowHandle(handle);
                    HwndSource source = HwndSource.FromHwnd(handle);
                    source.AddHook(WndProc);
                    // Auto close notification after 1.5 second
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1.3)
                    };

                    timer.Tick += (s, e) =>
                    {
                        statusUpdateHotKey.Visibility = Visibility.Collapsed;
                        timer.Stop();
                    };

                    timer.Start();

                }
            }

        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0312) // WM_HOTKEY
            {
                handled = KeyboardShortcuts.ProcessHotKey(wParam);
            }

            return IntPtr.Zero;
        }

        public void ListHotKey_TextChanged()
        {
            // Setting windows
            hotKeyStartStop.Text = ConfigManager.Instance.GetHotKey("Start/Stop");
            hotKeyOverlay.Text = ConfigManager.Instance.GetHotKey("Overlay");
            hotKeySetting.Text = ConfigManager.Instance.GetHotKey("Setting");
            hotKeyLog.Text = ConfigManager.Instance.GetHotKey("Log");
            hotKeySelectArea.Text = ConfigManager.Instance.GetHotKey("Select Area");
            hotKeyClearAreas.Text = ConfigManager.Instance.GetHotKey("Clear Areas");
            hotKeyClearPreviousArea.Text = ConfigManager.Instance.GetHotKey("Clear Selected Area");
            hotKeyShowArea.Text = ConfigManager.Instance.GetHotKey("Show Area");
            hotKeyChatBox.Text = ConfigManager.Instance.GetHotKey("ChatBox");
            hotKeyArea1.Text = ConfigManager.Instance.GetHotKey("Area 1");
            hotKeyArea2.Text = ConfigManager.Instance.GetHotKey("Area 2");
            hotKeyArea3.Text = ConfigManager.Instance.GetHotKey("Area 3");
            hotKeyArea4.Text = ConfigManager.Instance.GetHotKey("Area 4");
            hotKeyArea5.Text = ConfigManager.Instance.GetHotKey("Area 5");
            hotKeyAudio.Text = ConfigManager.Instance.GetHotKey("Audio Service");
            hotKeySwapLanguages.Text = ConfigManager.Instance.GetHotKey("Swap Languages");
            hotKeyRetryTranslate.Text = ConfigManager.Instance.GetHotKey("Retry Translation");
            // Mainwindows
            MainWindow.Instance.hotKeyStartStop.Text = ConfigManager.Instance.GetHotKey("Start/Stop");
            MainWindow.Instance.hotKeyOverlay.Text = ConfigManager.Instance.GetHotKey("Overlay");
            MainWindow.Instance.hotKeySetting.Text = ConfigManager.Instance.GetHotKey("Setting");
            MainWindow.Instance.hotKeyLog.Text = ConfigManager.Instance.GetHotKey("Log");
            MainWindow.Instance.hotKeySelectArea.Text = ConfigManager.Instance.GetHotKey("Select Area");
            MainWindow.Instance.hotKeyClearPreviousArea.Text = ConfigManager.Instance.GetHotKey("Clear Selected Area");
            MainWindow.Instance.hotKeyClearAreas.Text = ConfigManager.Instance.GetHotKey("Clear Areas");
            MainWindow.Instance.hotKeyShowArea.Text = ConfigManager.Instance.GetHotKey("Show Area");
            MainWindow.Instance.hotKeyChatBox.Text = ConfigManager.Instance.GetHotKey("ChatBox");
            MainWindow.Instance.hotKeyArea1.Text = ConfigManager.Instance.GetHotKey("Area 1");
            MainWindow.Instance.hotKeyArea2.Text = ConfigManager.Instance.GetHotKey("Area 2");
            MainWindow.Instance.hotKeyArea3.Text = ConfigManager.Instance.GetHotKey("Area 3");
            MainWindow.Instance.hotKeyArea4.Text = ConfigManager.Instance.GetHotKey("Area 4");
            MainWindow.Instance.hotKeyArea5.Text = ConfigManager.Instance.GetHotKey("Area 5");
            MainWindow.Instance.hotKeyAudio.Text = ConfigManager.Instance.GetHotKey("Audio Service");
            MainWindow.Instance.hotKeySwapLanguages.Text = ConfigManager.Instance.GetHotKey("Swap Languages");
            MainWindow.Instance.hotKeyRetryTranslate.Text = ConfigManager.Instance.GetHotKey("Retry Translation");
        }

        private void HotKeyFunctionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip event if we're initializing
            if (_isInitializing)
            {
                return;
            }

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                // Use Tag for internal key (not affected by localization)
                string functionName = selectedItem.Tag?.ToString() ?? "Start/Stop";

                // Get HotKey from config
                string hotKey = ConfigManager.Instance.GetHotKey(functionName);
                string[] keyParts = hotKey.Split('+');

                if (keyParts.Length >= 2)
                {
                    string key1 = keyParts[0].ToUpper();
                    string key2 = keyParts[1].ToUpper();


                    foreach (ComboBoxItem item in combineKey1.Items)
                    {
                        if (item.Content.ToString() == key1)
                        {
                            combineKey1.SelectedItem = item;
                            break;
                        }
                    }


                    foreach (ComboBoxItem item in combineKey2.Items)
                    {
                        if (item.Content.ToString() == key2)
                        {
                            combineKey2.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
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
                    if (ocrMethod == "Windows OCR" || ocrMethod == "OneOCR")
                    {
                        ocrButtonsPanel.Visibility = Visibility.Collapsed;
                        MainWindow.Instance.OCRStatusEllipse.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(69, 176, 105)); // Green
                        MainWindow.Instance.OCRStatusText.Text = LocalizationManager.Instance.Strings["Btn_On"];
                    }
                    else
                    {
                        ocrButtonsPanel.Visibility = Visibility.Visible;
                        MainWindow.Instance.OCRStatusEllipse.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // Red
                        MainWindow.Instance.OCRStatusText.Text = LocalizationManager.Instance.Strings["Btn_Off"];
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
        public void SwapLanguagesButton_Click(object? sender, RoutedEventArgs e)
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
            if (float.TryParse(blockDetectionPowerTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out float power))
            {
                // Note: SetBlockDetectionScale will save to config
                BlockDetectionManager.Instance.SetBlockDetectionScale(power);
                // Reset hash to force recalculation of text blocks
                Logic.Instance.ResetHash();
            }
            else
            {
                // If text is invalid, reset to the current value from BlockDetectionManager
                blockDetectionPowerTextBox.Text = BlockDetectionManager.Instance.GetBlockDetectionScale().ToString("F2", CultureInfo.InvariantCulture);
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
            if (float.TryParse(settleTimeTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out float settleTime) && settleTime >= 0)
            {
                ConfigManager.Instance.SetBlockDetectionSettleTime(settleTime);
                Console.WriteLine($"Block detection settle time set to: {settleTime:F2} seconds");

                // Reset hash to force recalculation of text blocks
                Logic.Instance.ResetHash();
            }
            else
            {
                // If text is invalid, reset to the current value from ConfigManager
                settleTimeTextBox.Text = ConfigManager.Instance.GetBlockDetectionSettleTime().ToString("F2", CultureInfo.InvariantCulture);
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
                        statusPrompt.Visibility = Visibility.Visible;
                        statusPrompt.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Green);
                        // Auto close status after 1.5 second
                        var timer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromSeconds(1.5)
                        };

                        timer.Tick += (s, e) =>
                        {
                            statusPrompt.Visibility = Visibility.Collapsed;
                            timer.Stop();
                        };

                        timer.Start();
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
                bool isLMStudioSelected = selectedService == "LM Studio";
                bool isGeminiSelected = selectedService == "Gemini";
                bool isCustomApiSelected = selectedService == "Custom API";
                bool isChatGptSelected = selectedService == "ChatGPT";
                bool isMistralSelected = selectedService == "Mistral";
                bool isGoogleTranslateSelected = selectedService == "Google Translate";
                bool isGroqSelected = selectedService == "Groq";
                bool isMicrosoftSelected = selectedService == "Microsoft";

                // Make sure the window is fully loaded and controls are initialized
                if (ollamaUrlLabel == null || ollamaUrlTextBox == null ||
                    ollamaPortLabel == null || ollamaPortTextBox == null ||
                    ollamaModelLabel == null || ollamaModelGrid == null ||
                    lmstudioUrlLabel == null || lmstudioUrlTextBox == null ||
                    lmstudioPortLabel == null || lmstudioPortTextBox == null ||
                    lmstudioModelLabel == null || lmstudioModelGrid == null ||
                    geminiApiKeyLabel == null || geminiApiKeyPasswordBox == null ||
                    geminiModelLabel == null || geminiModelGrid == null ||
                    customApiKeyLabel == null || customApiKeyPasswordBox == null ||
                    customApiModelLabel == null || customApiModelGrid == null ||
                    groqApiKeyLabel == null || groqApiKeyPasswordBox == null ||
                    groqModelLabel == null || groqModelGrid == null ||
                    mistralApiKeyLabel == null || mistralApiKeyPasswordBox == null ||
                    mistralModelLabel == null || mistralModelGrid == null ||
                    chatGptApiKeyLabel == null || chatGptApiKeyGrid == null ||
                    chatGptModelLabel == null || chatGptModelGrid == null ||
                    googleTranslateApiKeyLabel == null || googleTranslateApiKeyGrid == null ||
                    googleTranslateServiceTypeLabel == null || googleTranslateServiceTypeComboBox == null ||
                    googleTranslateMappingLabel == null || googleTranslateMappingCheckBox == null ||
                    microsoftApiKeyLabel == null || microsoftLegacyKeyPasswordBox == null ||
                    viewMicrosoftKeysButton == null || SaveMicrosoftKeysButton == null ||
                    microsoftLegacyModeCheckBox == null)
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
                SaveGeminiKeysButton.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Custom API-specific settings
                customApiKeyLabel.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                customApiKeyPasswordBox.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                customApiModelLabel.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                customApiModelGrid.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                viewCustomApiKeysButton.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                SaveCustomApiKeysButton.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                customApiUrlLabel.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                customApiUrlTextBox.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                customApiModelLabel.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;
                customApiModelGrid.Visibility = isCustomApiSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Groq-specific settings
                groqApiKeyLabel.Visibility = isGroqSelected ? Visibility.Visible : Visibility.Collapsed;
                groqApiKeyPasswordBox.Visibility = isGroqSelected ? Visibility.Visible : Visibility.Collapsed;
                groqApiKeyHelpText.Visibility = isGroqSelected ? Visibility.Visible : Visibility.Collapsed;
                groqModelLabel.Visibility = isGroqSelected ? Visibility.Visible : Visibility.Collapsed;
                groqModelGrid.Visibility = isGroqSelected ? Visibility.Visible : Visibility.Collapsed;
                viewGroqKeysButton.Visibility = isGroqSelected ? Visibility.Visible : Visibility.Collapsed;
                SaveGroqKeysButton.Visibility = isGroqSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Mistral-specific settings
                mistralApiKeyLabel.Visibility = isMistralSelected ? Visibility.Visible : Visibility.Collapsed;
                mistralApiKeyPasswordBox.Visibility = isMistralSelected ? Visibility.Visible : Visibility.Collapsed;
                mistralApiKeyHelpText.Visibility = isMistralSelected ? Visibility.Visible : Visibility.Collapsed;
                mistralModelLabel.Visibility = isMistralSelected ? Visibility.Visible : Visibility.Collapsed;
                mistralModelGrid.Visibility = isMistralSelected ? Visibility.Visible : Visibility.Collapsed;
                viewMistralKeysButton.Visibility = isMistralSelected ? Visibility.Visible : Visibility.Collapsed;
                SaveMistralKeysButton.Visibility = isMistralSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Ollama-specific settings
                ollamaUrlLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaUrlGrid.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaPortLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaPortTextBox.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaModelLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaModelGrid.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide LM Studio-specific settings
                lmstudioUrlLabel.Visibility = isLMStudioSelected ? Visibility.Visible : Visibility.Collapsed;
                lmstudioUrlGrid.Visibility = isLMStudioSelected ? Visibility.Visible : Visibility.Collapsed;
                lmstudioPortLabel.Visibility = isLMStudioSelected ? Visibility.Visible : Visibility.Collapsed;
                lmstudioPortTextBox.Visibility = isLMStudioSelected ? Visibility.Visible : Visibility.Collapsed;
                lmstudioModelLabel.Visibility = isLMStudioSelected ? Visibility.Visible : Visibility.Collapsed;
                lmstudioModelGrid.Visibility = isLMStudioSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide ChatGPT-specific settings
                chatGptApiKeyLabel.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                chatGptApiKeyGrid.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                chatGptModelLabel.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                chatGptModelGrid.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                viewChatGptKeysButton.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                SaveChatGptKeysButton.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Google Translate-specific settings
                googleTranslateServiceTypeLabel.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTranslateServiceTypeComboBox.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTranslateMappingLabel.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTranslateMappingCheckBox.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;

                // Hide prompt template for Google Translate or Microsoft
                bool showPromptTemplate = !isGoogleTranslateSelected && !isMicrosoftSelected;

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

                // Show/hide Microsoft-specific settings
                // microsoftApiKeyLabel.Visibility = isMicrosoftSelected ? Visibility.Visible : Visibility.Collapsed;
                // microsoftLegacyKeyPasswordBox.Visibility = isMicrosoftSelected ? Visibility.Visible : Visibility.Collapsed;
                // viewMicrosoftKeysButton.Visibility = isMicrosoftSelected ? Visibility.Visible : Visibility.Collapsed;
                // SaveMicrosoftKeysButton.Visibility = isMicrosoftSelected ? Visibility.Visible : Visibility.Collapsed;
                microsoftLegacyModeCheckBox.Visibility = isMicrosoftSelected ? Visibility.Visible : Visibility.Collapsed;

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
                else if (isGroqSelected)
                {
                    groqApiKeyPasswordBox.Password = ConfigManager.Instance.GetGroqApiKey();

                    // Set selected Groq model
                    string groqModel = ConfigManager.Instance.GetGroqModel();

                    // Temporarily remove event handlers to avoid triggering changes
                    groqModelComboBox.SelectionChanged -= GroqModelComboBox_SelectionChanged;

                    // First try to find exact match in dropdown items
                    bool found = false;
                    foreach (ComboBoxItem item in groqModelComboBox.Items)
                    {
                        if (string.Equals(item.Content?.ToString(), groqModel, StringComparison.OrdinalIgnoreCase))
                        {
                            groqModelComboBox.SelectedItem = item;
                            found = true;
                            break;
                        }
                    }

                    // If not found in dropdown, set as custom text
                    if (!found)
                    {
                        groqModelComboBox.Text = groqModel;
                    }

                    // Reattach event handler
                    groqModelComboBox.SelectionChanged += GroqModelComboBox_SelectionChanged;
                }
                else if (isCustomApiSelected)
                {
                    customApiKeyPasswordBox.Password = ConfigManager.Instance.GetCustomApiKey();
                    // Temporarily remove event handlers to avoid triggering changes
                    customApiUrlTextBox.LostFocus -= CustomApiUrlTextBox_LostFocus;
                    customApiUrlTextBox.Text = ConfigManager.Instance.GetCustomApiUrl();

                    // Temporarily remove event handlers to avoid triggering changes
                    customApiModelTextBox.TextChanged -= CustomApiModelTextBox_TextChanged;

                    // Set selected Custom API model
                    customApiModelTextBox.Text = ConfigManager.Instance.GetCustomApiModel();

                    // Reattach event handler
                    customApiModelTextBox.TextChanged += CustomApiModelTextBox_TextChanged;
                    customApiUrlTextBox.LostFocus += CustomApiUrlTextBox_LostFocus;

                }
                else if (isMistralSelected)
                {
                    mistralApiKeyPasswordBox.Password = ConfigManager.Instance.GetMistralApiKey();

                    // Set selected Mistral model
                    string mistralModel = ConfigManager.Instance.GetMistralModel();

                    // Temporarily remove event handlers to avoid triggering changes
                    mistralModelComboBox.SelectionChanged -= MistralModelComboBox_SelectionChanged;

                    // First try to find exact match in dropdown items
                    bool found = false;
                    foreach (ComboBoxItem item in mistralModelComboBox.Items)
                    {
                        if (string.Equals(item.Content?.ToString(), mistralModel, StringComparison.OrdinalIgnoreCase))
                        {
                            mistralModelComboBox.SelectedItem = item;
                            found = true;
                            break;
                        }
                    }

                    // If not found in dropdown, set as custom text
                    if (!found)
                    {
                        mistralModelComboBox.Text = mistralModel;
                    }

                    // Reattach event handler
                    mistralModelComboBox.SelectionChanged += MistralModelComboBox_SelectionChanged;
                }
                else if (isMicrosoftSelected)
                {
                    microsoftLegacyKeyPasswordBox.Password = ConfigManager.Instance.GetMicrosoftApiKey();
                    microsoftLegacyModeCheckBox.IsChecked = ConfigManager.Instance.GetMicrosoftLegacySignatureMode();
                }
                else if (isOllamaSelected)
                {
                    ollamaUrlTextBox.Text = ConfigManager.Instance.GetOllamaUrl();
                    ollamaPortTextBox.Text = ConfigManager.Instance.GetOllamaPort();
                    ollamaModelTextBox.Text = ConfigManager.Instance.GetOllamaModel();
                }
                else if (isLMStudioSelected)
                {
                    lmstudioUrlTextBox.Text = ConfigManager.Instance.GetLMStudioUrl();
                    lmstudioPortTextBox.Text = ConfigManager.Instance.GetLMStudioPort();
                    lmstudioModelTextBox.Text = ConfigManager.Instance.GetLMStudioModel();
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
                bool isWindowTtsSelected = selectedService == "Windows TTS";

                // Make sure the window is fully loaded and controls are initialized
                if (elevenLabsApiKeyLabel == null || elevenLabsApiKeyGrid == null ||
                    elevenLabsApiKeyHelpText == null || elevenLabsVoiceLabel == null ||
                    elevenLabsVoiceComboBox == null || googleTtsApiKeyLabel == null ||
                    googleTtsApiKeyGrid == null || googleTtsVoiceLabel == null ||
                    googleTtsVoiceComboBox == null || windowTTSVoiceLabel == null || windowTTSVoiceComboBox == null)
                {
                    Console.WriteLine("TTS UI elements not initialized yet. Skipping visibility update.");
                    return;
                }

                // Init combobox window TTS


                // Show/hide ElevenLabs-specific settings
                elevenLabsApiKeyLabel.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsApiKeyGrid.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsApiKeyHelpText.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsVoiceLabel.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsVoiceComboBox.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsModelLabel.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsModelComboBox.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;

                // Hide custom voice controls when ElevenLabs is not selected
                if (!isElevenLabsSelected)
                {
                    elevenLabsCustomVoiceLabel.Visibility = Visibility.Collapsed;
                    elevenLabsCustomVoiceTextBox.Visibility = Visibility.Collapsed;
                }

                // Show/hide Google TTS-specific settings
                googleTtsApiKeyLabel.Visibility = isGoogleTtsSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTtsApiKeyGrid.Visibility = isGoogleTtsSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTtsVoiceLabel.Visibility = isGoogleTtsSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTtsVoiceComboBox.Visibility = isGoogleTtsSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Window TTS-specific settings
                windowTTSVoiceLabel.Visibility = isWindowTtsSelected ? Visibility.Visible : Visibility.Collapsed;
                windowTTSVoiceComboBox.Visibility = isWindowTtsSelected ? Visibility.Visible : Visibility.Collapsed;
                windowsTTSGuide.Visibility = isWindowTtsSelected ? Visibility.Visible : Visibility.Collapsed;
                // Load service-specific settings if they're being shown
                if (isElevenLabsSelected)
                {
                    elevenLabsApiKeyPasswordBox.Password = ConfigManager.Instance.GetElevenLabsApiKey();

                    // Set selected voice - check if it's a preset or custom
                    string voiceId = ConfigManager.Instance.GetElevenLabsVoice();
                    bool foundPreset = false;
                    foreach (ComboBoxItem item in elevenLabsVoiceComboBox.Items)
                    {
                        if (item.Tag?.ToString() != "custom" && string.Equals(item.Tag?.ToString(), voiceId, StringComparison.OrdinalIgnoreCase))
                        {
                            elevenLabsVoiceComboBox.SelectedItem = item;
                            foundPreset = true;
                            elevenLabsCustomVoiceLabel.Visibility = Visibility.Collapsed;
                            elevenLabsCustomVoiceTextBox.Visibility = Visibility.Collapsed;
                            break;
                        }
                    }
                    // If not a preset voice, select "Custom" and show the textbox
                    if (!foundPreset && !string.IsNullOrWhiteSpace(voiceId))
                    {
                        foreach (ComboBoxItem item in elevenLabsVoiceComboBox.Items)
                        {
                            if (item.Tag?.ToString() == "custom")
                            {
                                elevenLabsVoiceComboBox.SelectedItem = item;
                                break;
                            }
                        }
                        elevenLabsCustomVoiceTextBox.Text = voiceId;
                        elevenLabsCustomVoiceLabel.Visibility = Visibility.Visible;
                        elevenLabsCustomVoiceTextBox.Visibility = Visibility.Visible;
                    }

                    // Set selected model
                    string modelId = ConfigManager.Instance.GetElevenLabsModel();
                    foreach (ComboBoxItem item in elevenLabsModelComboBox.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), modelId, StringComparison.OrdinalIgnoreCase))
                        {
                            elevenLabsModelComboBox.SelectedItem = item;
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
                else if (isWindowTtsSelected)
                {
                    // Set selected voice
                    string voiceId = ConfigManager.Instance.GetWindowsTtsVoice();
                    foreach (ComboBoxItem item in windowTTSVoiceComboBox.Items)
                    {
                        if (string.Equals(item.Content?.ToString(), voiceId, StringComparison.OrdinalIgnoreCase))
                        {
                            windowTTSVoiceComboBox.SelectedItem = item;
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

        // Custom API Key changed
        private void CustomApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // string apiKey = geminiApiKeyPasswordBox.Password.Trim();

                // // Update the config
                // ConfigManager.Instance.SetGeminiApiKey(apiKey);
                Console.WriteLine("Custom API key updated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Custom API key: {ex.Message}");
            }
        }

        // Groq API Key changed
        private void GroqApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // string apiKey = geminiApiKeyPasswordBox.Password.Trim();

                // // Update the config
                // ConfigManager.Instance.SetGeminiApiKey(apiKey);
                Console.WriteLine("Groq API key updated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Groq API key: {ex.Message}");
            }
        }

        // Microsoft legacy key changed
        private void MicrosoftLegacyKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                Console.WriteLine("Microsoft legacy API key updated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Microsoft API key: {ex.Message}");
            }
        }

        private void MicrosoftLegacyModeCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                bool enabled = microsoftLegacyModeCheckBox.IsChecked ?? false;
                ConfigManager.Instance.SetMicrosoftLegacySignatureMode(enabled);
                Console.WriteLine($"Microsoft legacy signature mode changed: {enabled}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Microsoft legacy mode: {ex.Message}");
            }
        }

        // Mistral API Key changed
        private void MistralApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                Console.WriteLine("Mistral API key updated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Mistral API key: {ex.Message}");
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

        // LM Studio URL changed
        private void LMStudioUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string url = lmstudioUrlTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(url))
            {
                ConfigManager.Instance.SetLMStudioUrl(url);
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

        // LMStudio Port changed
        private void LMStudioPortTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string port = lmstudioPortTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(port))
            {
                // Validate that the port is a number
                if (int.TryParse(port, out _))
                {
                    ConfigManager.Instance.SetLMStudioPort(port);
                }
                else
                {
                    // Reset to default if invalid
                    lmstudioPortTextBox.Text = "1234";
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

        // LM Studio Model changed
        private void LMStudioModelTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Skip if initializing or if the sender isn't the expected TextBox
            if (_isInitializing || sender != lmstudioModelTextBox)
                return;

            string sanitizedModel = lmstudioModelTextBox.Text.Trim();


            // Save valid model to config
            ConfigManager.Instance.SetLMStudioModel(sanitizedModel);
            Console.WriteLine($"LM Studio model set to: {sanitizedModel}");

            // Trigger retranslation if the current service is LM Studio
            if (ConfigManager.Instance.GetCurrentTranslationService() == "LM Studio")
            {
                Console.WriteLine("LM Studio model changed. Triggering retranslation...");

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

        private void ViewModelsButtonLMStudio_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://lmstudio.ai/models");
        }

        private void GeminiApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ai.google.dev/tutorials/setup");
        }

        private void GroqApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://console.groq.com/keys");
        }

        private void MistralApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://docs.mistral.ai/getting-started");
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
        private void CustomApiUrlTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string url = customApiUrlTextBox.Text?.Trim() ?? "";

                // Save to config
                ConfigManager.Instance.SetCustomApiUrl(url);
                Console.WriteLine($"Custom API URL set to: {url}");

                // Trigger retranslation if the current service is Custom API
                if (ConfigManager.Instance.GetCurrentTranslationService() == "Custom API")
                {
                    Console.WriteLine("Custom API URL changed. Triggering retranslation...");

                    // Reset the hash to force a retranslation
                    Logic.Instance.ResetHash();

                    // Clear any existing text objects to refresh the display
                    Logic.Instance.ClearAllTextObjects();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Custom API URL: {ex.Message}");
            }
        }

        private void CustomApiUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string url = customApiUrlTextBox.Text?.Trim() ?? "";

                // Save to config
                ConfigManager.Instance.SetCustomApiUrl(url);
                Console.WriteLine($"Custom API URL set to: {url}");

                // Trigger retranslation if the current service is Custom API
                if (ConfigManager.Instance.GetCurrentTranslationService() == "Custom API")
                {
                    Console.WriteLine("Custom API URL changed. Triggering retranslation...");

                    // Reset the hash to force a retranslation
                    Logic.Instance.ResetHash();

                    // Clear any existing text objects to refresh the display
                    Logic.Instance.ClearAllTextObjects();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Custom API URL: {ex.Message}");
            }
        }

        private void CustomApiModelTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string model = customApiModelTextBox.Text?.Trim() ?? "";

                // Save to config
                ConfigManager.Instance.SetCustomApiModel(model);
                Console.WriteLine($"Custom API model set to: {model}");

                // Trigger retranslation if the current service is Custom API
                if (ConfigManager.Instance.GetCurrentTranslationService() == "Custom API")
                {
                    Console.WriteLine("Custom API model changed. Triggering retranslation...");

                    // Reset the hash to force a retranslation
                    Logic.Instance.ResetHash();

                    // Clear any existing text objects to refresh the display
                    Logic.Instance.ClearAllTextObjects();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Custom API model: {ex.Message}");
            }
        }

        private void GroqModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string model;

                // Handle both dropdown selection and manually typed values
                if (groqModelComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    model = selectedItem.Content?.ToString() ?? "qwen/qwen3-32b";
                }
                else
                {
                    // For manually entered text
                    model = groqModelComboBox.Text?.Trim() ?? "qwen/qwen3-32b";
                }

                if (!string.IsNullOrWhiteSpace(model))
                {
                    // Save to config
                    ConfigManager.Instance.SetGroqModel(model);
                    Console.WriteLine($"Groq model set to: {model}");

                    // Trigger retranslation if the current service is Groq
                    if (ConfigManager.Instance.GetCurrentTranslationService() == "Groq")
                    {
                        Console.WriteLine("Groq model changed. Triggering retranslation...");

                        // Reset the hash to force a retranslation
                        Logic.Instance.ResetHash();

                        // Clear any existing text objects to refresh the display
                        Logic.Instance.ClearAllTextObjects();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Groq model: {ex.Message}");
            }
        }

        private void MistralModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string model;

                // Handle both dropdown selection and manually typed values
                if (mistralModelComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    model = selectedItem.Content?.ToString() ?? "open-mistral-nemo";
                }
                else
                {
                    // For manually entered text
                    model = mistralModelComboBox.Text?.Trim() ?? "open-mistral-nemo";
                }

                if (!string.IsNullOrWhiteSpace(model))
                {
                    // Save to config
                    ConfigManager.Instance.SetMistralModel(model);
                    Console.WriteLine($"Mistral model set to: {model}");

                    // Trigger retranslation if the current service is Mistral
                    if (ConfigManager.Instance.GetCurrentTranslationService() == "Mistral")
                    {
                        Console.WriteLine("Mistral model changed. Triggering retranslation...");

                        // Reset the hash to force a retranslation
                        Logic.Instance.ResetHash();

                        // Clear any existing text objects to refresh the display
                        Logic.Instance.ClearAllTextObjects();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Mistral model: {ex.Message}");
            }
        }

        private void ViewGeminiModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ai.google.dev/gemini-api/docs/models");
        }

        private void ViewGroqModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://console.groq.com/docs/models");
        }

        private void ViewMistralModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://docs.mistral.ai/getting-started/models/models_overview/");
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

        private void AudioProcessingModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (localWhisperService.Instance.IsRunning)
                {
                   MessageBoxResult result = MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_WhisperServiceRunning"],
                        LocalizationManager.Instance.Strings["Title_Confirm"],
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning
                    );
                    if (result == MessageBoxResult.Cancel)
                    {
                        if (e.RemovedItems.Count > 0)
                        {
                            _isInitializing = true; 
                            
                            audioProcessingModelComboBox.SelectedItem = e.RemovedItems[0];
                            
                            _isInitializing = false;
                        }
                        return;
                    }
                    else
                    {
                        localWhisperService.Instance.Stop();
                        audioServiceAutoTranslateCheckBox.IsChecked = false;
                    }
                }

                string model = (audioProcessingModelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
                               ?? audioProcessingModelComboBox.SelectedItem?.ToString()
                               ?? "ggml-tiny";

                if (!string.IsNullOrWhiteSpace(model))
                {
                    // Save to config
                    ConfigManager.Instance.SetAudioProcessingModel(model);
                    Console.WriteLine($"Audio processing model set from text input to: {model}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating audio processing model from text input: {ex.Message}");
            }
        }

        private void WhisperRuntimeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string newRuntime = (whisperRuntimeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "cpu";
                string currentRuntime = ConfigManager.Instance.GetWhisperRuntime();

                // Update thread count visibility (only show for CPU)
                UpdateWhisperThreadCountVisibility(newRuntime);
                
                // if no change, do nothing
                if (string.Equals(newRuntime, currentRuntime, StringComparison.OrdinalIgnoreCase))
                    return;

                // Notify that changing runtime requires app restart
                MessageBoxResult result = MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_WhisperRuntimeChangeRequiresRestart"],
                    LocalizationManager.Instance.Strings["Title_Confirm"],
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Information
                );
                
                if (result == MessageBoxResult.Cancel)
                {
                    // Revert selection
                    if (e.RemovedItems.Count > 0)
                    {
                        _isInitializing = true;
                        whisperRuntimeComboBox.SelectedItem = e.RemovedItems[0];
                        // Also revert visibility
                        string revertedRuntime = (e.RemovedItems[0] as ComboBoxItem)?.Tag?.ToString() ?? "cpu";
                        UpdateWhisperThreadCountVisibility(revertedRuntime);
                        _isInitializing = false;
                    }
                    return;
                }

                // If Whisper is running, stop it
                if (localWhisperService.Instance.IsRunning)
                {
                    localWhisperService.Instance.Stop();
                    audioServiceAutoTranslateCheckBox.IsChecked = false;
                }

                // Save new runtime
                ConfigManager.Instance.SetWhisperRuntime(newRuntime);
                Console.WriteLine($"Whisper runtime set to: {newRuntime} (requires app restart to take effect)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Whisper runtime: {ex.Message}");
            }
        }

        /// <summary>
        /// Show/hide CPU thread count controls based on runtime selection.
        /// Thread count only applies to CPU mode, not GPU (CUDA/Vulkan).
        /// </summary>
        private void UpdateWhisperThreadCountVisibility(string runtime)
        {
            bool isCpu = string.Equals(runtime, "cpu", StringComparison.OrdinalIgnoreCase);
            Visibility visibility = isCpu ? Visibility.Visible : Visibility.Collapsed;
            
            whisperThreadCountLabel.Visibility = visibility;
            whisperThreadCountTextBox.Visibility = visibility;
            whisperThreadCountTip.Visibility = visibility;
        }

        private void CustomApiModelTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string model = customApiModelTextBox.Text?.Trim() ?? "";

                if (!string.IsNullOrWhiteSpace(model))
                {
                    // Save to config
                    ConfigManager.Instance.SetCustomApiModel(model);
                    Console.WriteLine($"Custom API model set from text input to: {model}");

                    // Trigger retranslation if the current service is Custom API
                    if (ConfigManager.Instance.GetCurrentTranslationService() == "Custom API")
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
                Console.WriteLine($"Error updating Custom API model from text input: {ex.Message}");
            }
        }

        private void GroqModelComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string model = groqModelComboBox.Text?.Trim() ?? "";

                if (!string.IsNullOrWhiteSpace(model))
                {
                    // Save to config
                    ConfigManager.Instance.SetGroqModel(model);
                    Console.WriteLine($"Groq model set from text input to: {model}");

                    // Trigger retranslation if the current service is Groq
                    if (ConfigManager.Instance.GetCurrentTranslationService() == "Groq")
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
                Console.WriteLine($"Error updating Groq model from text input: {ex.Message}");
            }
        }

        private void MistralModelComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string model = mistralModelComboBox.Text?.Trim() ?? "";

                if (!string.IsNullOrWhiteSpace(model))
                {
                    // Save to config
                    ConfigManager.Instance.SetMistralModel(model);
                    Console.WriteLine($"Mistral model set from text input to: {model}");

                    // Trigger retranslation if the current service is Mistral
                    if (ConfigManager.Instance.GetCurrentTranslationService() == "Mistral")
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
                Console.WriteLine($"Error updating Mistral model from text input: {ex.Message}");
            }
        }

        private void OllamaDownloadLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ollama.com");
        }

        private void LMStudioDownloadLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://lmstudio.ai/download");
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
                    string model = selectedItem.Tag?.ToString() ?? "gpt-4.1-mini";

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
        private void NaturalVoiceSAPIAdapterLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/gexgd0419/NaturalVoiceSAPIAdapter");
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
                    string tag = selectedItem.Tag?.ToString() ?? "";

                    // Handle custom voice selection
                    if (tag == "custom")
                    {
                        // Show custom voice input
                        elevenLabsCustomVoiceLabel.Visibility = Visibility.Visible;
                        elevenLabsCustomVoiceTextBox.Visibility = Visibility.Visible;
                        // Don't save "custom" as voice ID - the textbox handler will save the actual ID
                        Console.WriteLine("Custom voice selected - enter voice ID in the textbox");
                    }
                    else
                    {
                        // Hide custom voice input and save the preset voice ID
                        elevenLabsCustomVoiceLabel.Visibility = Visibility.Collapsed;
                        elevenLabsCustomVoiceTextBox.Visibility = Visibility.Collapsed;
                        ConfigManager.Instance.SetElevenLabsVoice(tag);
                        Console.WriteLine($"ElevenLabs voice set to: {selectedItem.Content} (ID: {tag})");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ElevenLabs voice: {ex.Message}");
            }
        }

        private void ElevenLabsCustomVoiceTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string voiceId = elevenLabsCustomVoiceTextBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(voiceId))
                {
                    ConfigManager.Instance.SetElevenLabsVoice(voiceId);
                    Console.WriteLine($"ElevenLabs custom voice ID set to: {voiceId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ElevenLabs custom voice: {ex.Message}");
            }
        }

        private void ElevenLabsModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (elevenLabsModelComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string modelId = selectedItem.Tag?.ToString() ?? "eleven_flash_v2_5";
                    ConfigManager.Instance.SetElevenLabsModel(modelId);
                    Console.WriteLine($"ElevenLabs model set to: {selectedItem.Content} (ID: {modelId})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ElevenLabs model: {ex.Message}");
            }
        }

        private void WindowTTSVoiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (windowTTSVoiceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string voiceId = selectedItem.Content?.ToString() ?? "Microsoft David (en-US, Male)";
                    ConfigManager.Instance.SetWindowsTtsVoice(voiceId);
                    Console.WriteLine($"Windows TTS voice set to: {selectedItem.Content} (ID: {voiceId})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Windows TTS voice: {ex.Message}");
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
                string lastThreshold = ConfigManager.Instance.GetTextSimilarThreshold().ToString(CultureInfo.InvariantCulture);

                // Validate input is a valid number
                if (!double.TryParse(textSimilarThresholdTextBox.Text, System.Globalization.CultureInfo.InvariantCulture, out double similarThreshold))
                {
                    MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_InvalidNumber"],
                        LocalizationManager.Instance.Strings["Title_Error"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );

                    // Reset textbox to last valid value
                    textSimilarThresholdTextBox.Text = lastThreshold;
                    return;
                }

                // Check range
                if (similarThreshold > 1.0 || similarThreshold < 0.5)
                {
                    // Show warning message
                    MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_ValueBetween"],
                        LocalizationManager.Instance.Strings["Title_Error"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );

                    // Reset to current value from config
                    textSimilarThresholdTextBox.Text = lastThreshold;
                    Console.WriteLine($"Text similar threshold reset to default value: {lastThreshold}");
                    return;
                }

                // If we get here, the value is valid, so save it
                ConfigManager.Instance.SetTextSimilarThreshold(similarThreshold.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine($"Text similar threshold updated to: {similarThreshold}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating text similar threshold: {ex}");

                // Restore last known good value in case of any error
                textSimilarThresholdTextBox.Text = ConfigManager.Instance.GetTextSimilarThreshold().ToString(CultureInfo.InvariantCulture);
            }
        }

        private void MinLetterConfidenceTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (double.TryParse(minLetterConfidenceTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double confidence) && confidence >= 0 && confidence <= 1)
                {
                    ConfigManager.Instance.SetMinLetterConfidence(confidence);
                    Console.WriteLine($"Minimum letter confidence set to: {confidence}");

                    // Reset the hash to force new OCR processing
                    Logic.Instance.ResetHash();
                }
                else
                {
                    // Reset to current value from config if invalid
                    minLetterConfidenceTextBox.Text = ConfigManager.Instance.GetMinLetterConfidence().ToString(CultureInfo.InvariantCulture);
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

                if (double.TryParse(minLineConfidenceTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double confidence) && confidence >= 0 && confidence <= 1)
                {
                    ConfigManager.Instance.SetMinLineConfidence(confidence);
                    Console.WriteLine($"Minimum line confidence set to: {confidence}");

                    // Reset the hash to force new OCR processing
                    Logic.Instance.ResetHash();
                }
                else
                {
                    // Reset to current value from config if invalid
                    minLineConfidenceTextBox.Text = ConfigManager.Instance.GetMinLineConfidence().ToString(CultureInfo.InvariantCulture);
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

                MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_NoLanguageSelected"],
                    LocalizationManager.Instance.Strings["Title_LanguagePackCheck"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
            else
            {
                Console.WriteLine($"Checking language pack for: {sourceLanguage}");

                _isLanguagePackInstall = WindowsOCRManager.Instance.CheckLanguagePackInstall(sourceLanguage);

                if (_isLanguagePackInstall)
                {
                    MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_LanguagePackInstalled"],
                        LocalizationManager.Instance.Strings["Title_LanguagePackCheck"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                }
                else
                {
                    if (!string.IsNullOrEmpty(WindowsOCRManager.Instance._currentLanguageCode))
                    {
                        string message = string.Format(
                            LocalizationManager.Instance.Strings["Msg_LanguagePackInstallGuide"],
                            WindowsOCRManager.Instance._currentLanguageCode
                        );

                        MessageBox.Show(
                            message,
                            LocalizationManager.Instance.Strings["Title_LanguagePackCheck"],
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                    }
                    else
                    {
                        MessageBox.Show(
                            LocalizationManager.Instance.Strings["Msg_LanguageNotSupported"],
                            LocalizationManager.Instance.Strings["Title_LanguagePackCheck"],
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                    }
                }
            }

        }

        private void CreateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button)
            {
                profileName = profileNameTextBox.Text.Trim();

                if (string.IsNullOrEmpty(profileName))
                {
                    MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_PleaseEnterProfileName"],
                        LocalizationManager.Instance.Strings["Title_Error"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
                else
                {
                    // Create profile with current config
                    ConfigManager.Instance.SaveTranslationAreas(MainWindow.Instance.savedTranslationAreas, profileName);
                    // Add to combo box
                    bool exists = false;
                    foreach (var item in profileComboBox.Items)
                    {
                        if (item.ToString() == profileName)
                        {
                            profileComboBox.SelectedItem = item;
                            exists = true;
                            break;
                        }
                    }


                    if (!exists)
                    {
                        profileComboBox.Items.Add(profileName);
                        profileComboBox.SelectedIndex = profileComboBox.Items.Count - 1;
                    }
                    // Show status
                    statusUpdateGameProfile.Visibility = Visibility.Visible;
                    statusUpdateGameProfile.Text = $"Create {profileName} successfully!";
                    statusUpdateGameProfile.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Green);

                    // Auto close status after 1.5 second
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1.5)
                    };

                    timer.Tick += (s, e) =>
                    {
                        statusUpdateGameProfile.Text = "";
                        timer.Stop();
                    };

                    timer.Start();
                }
            }
        }

        private void RemoveProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (profileComboBox.SelectedItem != null)
            {
                string? selectedText = profileComboBox.SelectedItem.ToString();
                string filePath = Path.Combine(ConfigManager.Instance._profileFolderPath, $"{selectedText}.txt");
                if (File.Exists(filePath))
                {
                    MessageBoxResult result = MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_ConfirmRemoveProfile"],
                        LocalizationManager.Instance.Strings["Title_Confirm"],
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Information
                    );
                    if (result == MessageBoxResult.OK)
                    {
                        File.Delete(filePath);
                        Console.WriteLine("Profile deleted successfully!");
                        profileComboBox.Items.Remove(selectedText);
                        profileComboBox.SelectedIndex = -1;
                        // Clear all save selection areas
                        MainWindow.Instance.savedTranslationAreas.Clear();
                        MainWindow.Instance.hasSelectedTranslationArea = false;
                        MainWindow.Instance.currentAreaIndex = -1;
                        // Show status
                        statusUpdateGameProfile.Visibility = Visibility.Visible;
                        statusUpdateGameProfile.Text = $"Remove {profileName} successfully!";
                        statusUpdateGameProfile.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Red);

                        // Auto close status after 1.5 second
                        var timer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromSeconds(1.5)
                        };

                        timer.Tick += (s, e) =>
                        {
                            statusUpdateGameProfile.Text = "";
                            timer.Stop();
                        };

                        timer.Start();
                    }
                }
                else
                {
                    MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_ProfileNotFound"],
                        LocalizationManager.Instance.Strings["Title_Error"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
            else
            {
                MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_NoProfileSelected"],
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void UpdateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (profileComboBox.SelectedItem != null)
            {
                string selectedText = profileComboBox.SelectedItem.ToString() ?? "Default";
                string filePath = Path.Combine(ConfigManager.Instance._profileFolderPath, $"{selectedText}.txt");
                if (File.Exists(filePath))
                {
                    MessageBoxResult result = MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_ConfirmUpdateProfile"],
                        LocalizationManager.Instance.Strings["Title_Confirm"],
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Information
                    );
                    if (result == MessageBoxResult.OK)
                    {
                        if (MainWindow.Instance.savedTranslationAreas.Count > 0)
                        {
                            ConfigManager.Instance.SaveTranslationAreas(MainWindow.Instance.savedTranslationAreas, selectedText);
                            Console.WriteLine($"Saved {selectedText}.txt {MainWindow.Instance.savedTranslationAreas.Count} translation areas to config");
                        }
                        else
                        {
                            // Clear saved areas in config if we have none
                            ConfigManager.Instance.SaveTranslationAreas(new List<Rect>(), selectedText);
                            Console.WriteLine("Cleared translation areas in config");
                        }
                        // Show status
                        statusUpdateGameProfile.Visibility = Visibility.Visible;
                        statusUpdateGameProfile.Text = $"Update {profileName} successfully!";
                        statusUpdateGameProfile.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Orange);

                        // Auto close status after 1.5 second
                        var timer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromSeconds(1.5)
                        };

                        timer.Tick += (s, e) =>
                        {
                            statusUpdateGameProfile.Text = "";
                            timer.Stop();
                        };

                        timer.Start();
                    }

                }
                else
                {
                    MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_ProfileNotFoundCreate"],
                        LocalizationManager.Instance.Strings["Title_Error"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
            else
            {
                MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_NoProfileSelected"],
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void LoadProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (profileComboBox.SelectedItem != null)
            {
                string? selectedText = profileComboBox.SelectedItem.ToString();
                string filePath = Path.Combine(ConfigManager.Instance._profileFolderPath, $"{selectedText}.txt");
                if (File.Exists(filePath))
                {
                    // Get saved areas from config
                    List<Rect> areas = ConfigManager.Instance.GetTranslationAreas(filePath);
                    if (areas.Count > 0)
                    {
                        // Update our areas list
                        MainWindow.Instance.savedTranslationAreas.Clear();
                        MainWindow.Instance.savedTranslationAreas = areas;
                        MainWindow.Instance.hasSelectedTranslationArea = true;
                        MainWindow.Instance.SwitchToTranslationArea(MainWindow.Instance.savedTranslationAreas.Count - 1);
                    }
                    else
                    {
                        Console.WriteLine("No translation areas found in config");
                    }
                    // Show status
                    statusUpdateGameProfile.Visibility = Visibility.Visible;
                    statusUpdateGameProfile.Text = $"Load {profileName} successfully!";
                    statusUpdateGameProfile.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Violet);
                    ReloadSetting();

                    // Auto close status after 1.5 second
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1.5)
                    };

                    timer.Tick += (s, e) =>
                    {
                        statusUpdateGameProfile.Text = "";
                        timer.Stop();
                    };

                    timer.Start();
                }
                else
                {
                    MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_ProfileNotFoundCreate"],
                        LocalizationManager.Instance.Strings["Title_Error"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
            else
            {
                MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_NoProfileSelected"],
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void LoadAllAudioProcessingModel()
        {
            audioProcessingModelComboBox.Items.Clear();

            HashSet<string> uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<string?> fileNames = Directory.GetFiles(ConfigManager.Instance._audioProcessingModelFolderPath, "*.bin")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrEmpty(name))
                .OrderBy(name => name)
                .ToList();

            foreach (string? name in fileNames)
            {
                if (!string.IsNullOrEmpty(name) && uniqueNames.Add(name))
                {
                    audioProcessingModelComboBox.Items.Add(new ComboBoxItem { Content = name });
                }
            }
        }

        private void LoadAllProfile()
        {
            if (!Directory.Exists(ConfigManager.Instance._profileFolderPath))
            {
                Directory.CreateDirectory(ConfigManager.Instance._profileFolderPath);
            }

            profileComboBox.Items.Clear();

            HashSet<string> uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<string?> fileNames = Directory.GetFiles(ConfigManager.Instance._profileFolderPath, "*.txt")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrEmpty(name))
                .OrderBy(name => name)
                .ToList();

            foreach (string? name in fileNames)
            {
                if (!string.IsNullOrEmpty(name) && uniqueNames.Add(name))
                {
                    profileComboBox.Items.Add(name);
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
                MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_ContextCleared"],
                    LocalizationManager.Instance.Strings["Title_Success"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                Console.WriteLine("Translation context cleared successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing translation context: {ex.Message}");
                MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_ErrorClearingContext"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
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
                    MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_PleaseEnterPhrase"],
                        LocalizationManager.Instance.Strings["Title_Error"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                // Check if the phrase already exists
                if (_ignorePhrases.Any(p => p.Phrase == phrase))
                {
                    MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_PhraseAlreadyExists"], phrase),
                        LocalizationManager.Instance.Strings["Title_Error"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
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
                MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_ErrorAddingPhrase"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
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
                    MessageBoxResult result = MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_ConfirmRemovePhrase"], phrase),
                        LocalizationManager.Instance.Strings["Title_Confirm"],
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question
                    );

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
                MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_ErrorRemovingPhrase"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
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

        // Load exclude regions from ConfigManager
        private void LoadExcludeRegions()
        {
            try
            {
                // Get regions from MainWindow (which loads from ConfigManager)
                var regions = MainWindow.Instance.excludeRegions;
                
                // Populate the list view
                excludeRegionsListView.ItemsSource = regions;
                
                // Set the show exclude regions checkbox
                showExcludeRegionsCheckBox.IsChecked = MainWindow.Instance.GetShowExcludeRegions();
                
                Console.WriteLine($"Loaded {regions.Count} exclude regions from config");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading exclude regions: {ex.Message}");
            }
        }

        // Add a new exclude region
        private void AddExcludeRegionButton_Click(object sender, RoutedEventArgs e)
        {
            // Hide settings window
            this.Hide();
            
            // Trigger exclude region selection
            MainWindow.Instance.ToggleExcludeRegionSelector();
            
            
            // Reload to show the new region
            LoadExcludeRegions();
        }

        // Clear all exclude regions
        private void ClearExcludeRegionsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MainWindow.Instance.ClearExcludeRegions();
                excludeRegionsListView.ItemsSource = null;
                excludeRegionsListView.ItemsSource = MainWindow.Instance.excludeRegions;
                Console.WriteLine("All exclude regions cleared from settings");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing exclude regions: {ex.Message}");
            }
        }

        // Show exclude regions checkbox checked
        private void ShowExcludeRegionsCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            bool show = showExcludeRegionsCheckBox.IsChecked ?? true;
            MainWindow.Instance.SetShowExcludeRegions(show);
        }

        // Show exclude regions checkbox unchecked
        private void ShowExcludeRegionsCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            bool show = showExcludeRegionsCheckBox.IsChecked ?? true;
            MainWindow.Instance.SetShowExcludeRegions(show);
        }

        private void ShowIconSignal_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool enabled = showIconSignalCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetShowIconSignal(enabled);
            Console.WriteLine($"Show icon signal set to {enabled}");
        }

        private void AudioProcessingProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (audioProcessingProviderComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                ConfigManager.Instance.SetAudioProcessingProvider(selectedItem.Content.ToString() ?? "OpenAI Realtime API");
            }
        }

        // private void OpenAiRealtimeApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        // {
        //     if (_isInitializing) return;
        //     ConfigManager.Instance.SetOpenAiRealtimeApiKey(openAiRealtimeApiKeyPasswordBox.Password.Trim());
        // }

        //Handle Auto-translate checkbox change for audio service
        private async void AudioServiceAutoTranslateCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool enabled = audioServiceAutoTranslateCheckBox.IsChecked ?? false;
            // ConfigManager.Instance.SetAudioServiceAutoTranslateEnabled(enabled);
            Console.WriteLine($"Settings window: Audio service auto-translate set to {enabled}");
            if (enabled && !localWhisperService.Instance.IsRunning)
            {
                // Start the local Whisper service if not already running
                await localWhisperService.Instance.StartServiceAsync((original, translated) =>
                {
                    Console.WriteLine($"Whisper detected: {original}");
                });
                Console.WriteLine("Local Whisper Service started");
            }
            else if (!enabled && localWhisperService.Instance.IsRunning)
            {
                // Stop the local Whisper service if it was running
                localWhisperService.Instance.Stop();
                Console.WriteLine("Local Whisper Service stopped");
            }
        }

        // Handle Multi selection area checkbox change for multi selection area
        private void MultiSelectionAreaCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool enabled = multiSelectionAreaCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetUseMultiSelectionArea(enabled);
            Console.WriteLine($"Settings window: Multi selection area set to {enabled}");
            if (isNeedShowMessage)
            {
                // Show notification
                MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_MultiSelectionGuide"],
                    LocalizationManager.Instance.Strings["Title_MultiSelectionGuide"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            isNeedShowMessage = !enabled;
        }

        private void InstallServerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get the base directory of the application
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string workingDirectory = Path.Combine(baseDirectory, "translation_server");
                // Choose the appropriate batch file and working directory based on the OCR method
                string setupBatchFileName = "install_requirements.bat";
                // Check if batch file exists
                string setupBatchFilePath = Path.Combine(workingDirectory, setupBatchFileName);
                if (!File.Exists(setupBatchFilePath))
                {
                    Console.WriteLine($"File installation not found: {setupBatchFilePath}");
                    return;
                }
                // Initialize process start info
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {setupBatchFileName}",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                // Start the process
                using (Process? setupProcess = Process.Start(startInfo))
                {
                    if (setupProcess == null)
                    {
                        Console.WriteLine("Unable to start the server installation process");
                        return;
                    }

                    // Wait for the process to finish
                    setupProcess.WaitForExit();

                    Console.WriteLine($"The server installation process has been completed");
                    return;
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error when installing server: {ex.Message}");
                return;
            }
        }

        private void StartServerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get the base directory of the application
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string workingDirectory = Path.Combine(baseDirectory, "translation_server");
                // Choose the appropriate batch file and working directory based on the OCR method
                string setupBatchFileName = "start_server.bat";
                // Check if batch file exists
                string setupBatchFilePath = Path.Combine(workingDirectory, setupBatchFileName);
                if (!File.Exists(setupBatchFilePath))
                {
                    Console.WriteLine($"File start not found: {setupBatchFilePath}");
                    return;
                }
                // Initialize process start info
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {setupBatchFileName}",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                // Start the process
                using (Process? setupProcess = Process.Start(startInfo))
                {
                    if (setupProcess == null)
                    {
                        Console.WriteLine("Unable to start the server process");
                        return;
                    }

                    Console.WriteLine($"The server start process has been completed");
                    return;
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error when start server: {ex.Message}");
                return;
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening URL: {ex.Message}");
            }
        }

        private void AudioModelDownloadTextBlock_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is TextBlock textBlock)
                {
                    UpdateAudioModelDownloadText(textBlock);
                    
                    LocalizationManager.Instance.PropertyChanged += (s, args) =>
                    {
                        if (args.PropertyName == "Strings")
                        {
                            UpdateAudioModelDownloadText(textBlock);
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading audio model download text: {ex.Message}");
            }
        }

        private void UpdateAudioModelDownloadText(TextBlock textBlock)
        {
            try
            {
                textBlock.Inlines.Clear();
                
                textBlock.Inlines.Add(new Run(LocalizationManager.Instance.Strings["Tip_AudioModelDownload_Part1"]));
                
                // Hyperlink
                var hyperlink = new Hyperlink(new Run("https://huggingface.co/ggerganov/whisper.cpp/tree/main"))
                {
                    NavigateUri = new Uri("https://huggingface.co/ggerganov/whisper.cpp/tree/main"),
                    Foreground = System.Windows.Media.Brushes.Blue
                };
                hyperlink.RequestNavigate += Hyperlink_RequestNavigate;
                textBlock.Inlines.Add(hyperlink);
                
                textBlock.Inlines.Add(new Run(LocalizationManager.Instance.Strings["Tip_AudioModelDownload_Part2"]));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating audio model download text: {ex.Message}");
            }
        }

        private void RemoveServerButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                LocalizationManager.Instance.Strings["Msg_ConfirmRemoveServer"],
                LocalizationManager.Instance.Strings["Title_ConfirmRemoval"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Get the base directory of the application
                    string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    string workingDirectory = Path.Combine(baseDirectory, "translation_server");
                    // Choose the appropriate batch file and working directory based on the OCR method
                    string setupBatchFileName = "Remove_server.bat";
                    // Check if batch file exists
                    string setupBatchFilePath = Path.Combine(workingDirectory, setupBatchFileName);
                    if (!File.Exists(setupBatchFilePath))
                    {
                        Console.WriteLine($"File start not found: {setupBatchFilePath}");
                        return;
                    }
                    // Initialize process start info
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {setupBatchFileName}",
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = true,
                        CreateNoWindow = false
                    };

                    // Start the process
                    using (Process? setupProcess = Process.Start(startInfo))
                    {
                        if (setupProcess == null)
                        {
                            Console.WriteLine("Unable to Remove the server");
                            return;
                        }

                        Console.WriteLine($"The server has been removed");
                        return;
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error when remove server: {ex.Message}");
                    return;
                }
            }
        }

        private void IsSendDataToServerCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool enabled = isSendDataToServerCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetSendDataToServer(enabled);
            Console.WriteLine($"Settings window: Is send data to server set to {enabled}");
        }

        // private void WindowsOCRIntegrationCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        // {
        //     bool enabled = windowsOCRIntegrationCheckBox.IsChecked ?? false;
        //     ConfigManager.Instance.SetWindowsOCRIntegration(enabled);
        //     Console.WriteLine($"WindowsOCR integration set to {enabled}");
        // }

        private void AutoOCRCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool enabled = AutoOCRCheckBox.IsChecked ?? true;
            ConfigManager.Instance.SetAutoOCR(enabled);
            Console.WriteLine($"Auto OCR set to {enabled}");
            if (isNeedShowWarningAutoOCR && !enabled)
            {
                // Show notification
                MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_AutoOCRDisabled"],
                    LocalizationManager.Instance.Strings["Title_AutoOCRDisabled"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            isNeedShowWarningAutoOCR = enabled;
        }

        private void ExcludeCharacterNameCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool enabled = excludeCharacterNameCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetExcludeCharacterName(enabled);
            Console.WriteLine($"Exclude character name set to {enabled}");
        }

        private void AutoSetOverlayBackgroundColorcheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool enabled = autoSetOverlayBackgroundColorcheckBox.IsChecked ?? true;
            ConfigManager.Instance.SetAutoSetOverlayBackground(enabled);
            Console.WriteLine($"Auto set overlay background color set to {enabled}");
        }

        private void AutoMergeOverlappingTextCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool enabled = autoMergeOverlappingTextCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetAutoMergeOverlappingText(enabled);
            Console.WriteLine($"Auto merge overlapping text set to {enabled}");
        }

        private void SetHotKeyEnableCheckBox_CheckChange(object sender, RoutedEventArgs e)
        {
            bool enabled = hotKeyEnableCheckBox.IsChecked ?? true;
            ConfigManager.Instance.SetHotKeyEnable(enabled);
            Console.WriteLine($"Hot key enable set to {enabled}");
        }

        private void HDRSupportCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool enabled = hdrSupportCheckBox.IsChecked ?? true;
            ConfigManager.Instance.SetHDRSupportEnabled(enabled);
            Console.WriteLine($"HDR support set to {enabled}");
        }

        private void SilenceThresholdTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string silenceThreshold = silenceThresholdTextBox.Text?.Trim() ?? "";

                if (!string.IsNullOrWhiteSpace(silenceThreshold))
                {
                    // Save to config
                    ConfigManager.Instance.SetSilenceThreshold(silenceThreshold);
                    Console.WriteLine($"Silence threshold set to: {silenceThreshold}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating silence threshold: {ex.Message}");
            }
        }

        private void SilenceDurationTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string silenceDuration = silenceDurationTextBox.Text?.Trim() ?? "";

                if (!string.IsNullOrWhiteSpace(silenceDuration))
                {
                    // Save to config
                    ConfigManager.Instance.SetSilenceDurationMs(silenceDuration);
                    Console.WriteLine($"Silence duration set to: {silenceDuration}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating silence duration: {ex.Message}");
            }
        }

        private void MaxBufferSamplesTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string maxBufferSamples = maxBufferSamplesTextBox.Text?.Trim() ?? "";

                if (!string.IsNullOrWhiteSpace(maxBufferSamples))
                {
                    // Save to config
                    ConfigManager.Instance.SetMaxBufferSamples(maxBufferSamples);
                    Console.WriteLine($"Max buffer samples set to: {maxBufferSamples}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating max buffer samples: {ex.Message}");
            }
        }

        private void WhisperThreadCountTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                string threadCount = whisperThreadCountTextBox.Text?.Trim() ?? "";

                if (!string.IsNullOrWhiteSpace(threadCount))
                {
                    // Validate: must be 0 or positive integer
                    if (int.TryParse(threadCount, out int count) && count >= 0)
                    {
                        int oldValue = ConfigManager.Instance.GetWhisperThreadCount();
                        if (oldValue != count)
                        {
                            // Check if service is running
                            if (localWhisperService.Instance.IsRunning)
                            {
                                MessageBoxResult result = MessageBox.Show(
                                    LocalizationManager.Instance.Strings["Msg_WhisperServiceRunning"],
                                    LocalizationManager.Instance.Strings["Title_Confirm"],
                                    MessageBoxButton.OKCancel,
                                    MessageBoxImage.Warning
                                );
                                if (result == MessageBoxResult.Cancel)
                                {
                                    // Revert to old value
                                    whisperThreadCountTextBox.Text = oldValue.ToString();
                                    return;
                                }
                                else
                                {
                                    localWhisperService.Instance.Stop();
                                    audioServiceAutoTranslateCheckBox.IsChecked = false;
                                }
                            }

                            ConfigManager.Instance.SetWhisperThreadCount(threadCount);
                            Console.WriteLine($"Whisper thread count set to: {threadCount}");
                        }
                    }
                    else
                    {
                        // Reset to default if invalid
                        whisperThreadCountTextBox.Text = "0";
                        ConfigManager.Instance.SetWhisperThreadCount("0");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating whisper thread count: {ex.Message}");
            }
        }

        private void RemoveOcrButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                LocalizationManager.Instance.Strings["Msg_ConfirmRemoveOCRData"],
                LocalizationManager.Instance.Strings["Title_ConfirmRemoval"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
            string currentOcrEngine = ConfigManager.Instance.GetOcrMethod();
            if (currentOcrEngine == "Windows OCR" || currentOcrEngine == "OneOCR")
            {
                MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_CannotRemoveWindowsOCRData"],
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return;
            }
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string webserverPath = Path.Combine(baseDirectory, "webserver");
            string ocrDataPath = Path.Combine(webserverPath, currentOcrEngine);
            // mapping OCR method to folder path name
            string fullOcrPath = "";
            if (currentOcrEngine == "PaddleOCR")
            {
                fullOcrPath = Path.Combine(ocrDataPath, "ocrstuffpaddleocr");
            }
            else if (currentOcrEngine == "EasyOCR")
            {
                fullOcrPath = Path.Combine(ocrDataPath, "ocrstuffeasyocr");
            }
            else if (currentOcrEngine == "RapidOCR")
            {
                fullOcrPath = Path.Combine(ocrDataPath, "ocrstuffrapidocr");
            }
            Console.WriteLine("Removing OCR data at: " + fullOcrPath);
            if (Directory.Exists(fullOcrPath))
            {
                try
                {
                    Directory.Delete(fullOcrPath, true);
                    MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_OCRDataRemoved"],
                        LocalizationManager.Instance.Strings["Title_Success"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error removing OCR data: {ex.Message}");
                    MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_ErrorRemovingOCRData"], ex.Message),
                        LocalizationManager.Instance.Strings["Title_Error"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return;
                }
            }
            else
            {
                MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_NoOCRDataFound"],
                    LocalizationManager.Instance.Strings["Title_Info"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }
            
            Console.WriteLine("OCR data removed successfully");
        }

        #region Clipboard Auto-Translate Event Handlers

        private void ClipboardAutoTranslateCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Skip if initializing
            if (_isInitializing) return;

            try
            {
                bool enabled = clipboardAutoTranslateCheckBox.IsChecked ?? false;
                ConfigManager.Instance.SetClipboardAutoTranslateEnabled(enabled);
                Console.WriteLine($"Clipboard auto-translate enabled: {enabled}");

                // Update MainWindow clipboard monitor state
                MainWindow.Instance?.UpdateClipboardMonitorState();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating clipboard auto-translate: {ex.Message}");
            }
        }

        private void ClipboardCopyResultCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Skip if initializing
            if (_isInitializing) return;

            try
            {
                bool enabled = clipboardCopyResultCheckBox.IsChecked ?? true;
                ConfigManager.Instance.SetClipboardCopyResultEnabled(enabled);
                Console.WriteLine($"Clipboard copy result enabled: {enabled}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating clipboard copy result: {ex.Message}");
            }
        }

        private void ClipboardDebounceTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Skip if initializing
            if (_isInitializing) return;

            try
            {
                string debounceText = clipboardDebounceTextBox.Text?.Trim() ?? "";
                
                if (int.TryParse(debounceText, out int debounceMs))
                {
                    ConfigManager.Instance.SetClipboardDebounceMs(debounceMs);
                    Console.WriteLine($"Clipboard debounce set to: {debounceMs}ms");

                    // Update MainWindow clipboard monitor
                    MainWindow.Instance?.UpdateClipboardMonitorState();
                }
                else
                {
                    // Reset to current config value
                    clipboardDebounceTextBox.Text = ConfigManager.Instance.GetClipboardDebounceMs().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating clipboard debounce: {ex.Message}");
            }
        }

        private void ClipboardMaxCharsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Skip if initializing
            if (_isInitializing) return;

            try
            {
                string maxCharsText = clipboardMaxCharsTextBox.Text?.Trim() ?? "";
                
                if (int.TryParse(maxCharsText, out int maxChars))
                {
                    ConfigManager.Instance.SetClipboardMaxChars(maxChars);
                    Console.WriteLine($"Clipboard max chars set to: {maxChars}");

                    // Update MainWindow clipboard monitor
                    MainWindow.Instance?.UpdateClipboardMonitorState();
                }
                else
                {
                    // Reset to current config value
                    clipboardMaxCharsTextBox.Text = ConfigManager.Instance.GetClipboardMaxChars().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating clipboard max chars: {ex.Message}");
            }
        }

        #endregion

        // private void MangaModeCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        // {
        //     bool enabled = MangaModeCheckBox.IsChecked ?? true;
        //     ConfigManager.Instance.SetMangaMode(enabled);
        //     Console.WriteLine($"Manga mode set to {enabled}");
        //     if (isNeedShowWarningMangaMode && enabled)
        //     {
        //         // Show notification
        //         MessageBox.Show(
        //         "If you enable this feature, translation speed will be slower but will provide more accurate overlay display for manga.",
        //         "Manga Mode Enabled",
        //         MessageBoxButton.OK,
        //         MessageBoxImage.Information);
        //         isNeedShowWarningMangaMode = false;
        //     } 
        //     else 
        //     {
        //         isNeedShowWarningMangaMode = true;
        //     }
        // }
    }
}