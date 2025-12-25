using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Drawing.Imaging;
using Color = System.Windows.Media.Color;
using System.Windows.Threading;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Text;
using System.Windows.Shell;
using System.Net.Http;
using System.Text.Json;
using SocketIOClient;
using Windows.ApplicationModel.VoiceCommands;


namespace RSTGameTranslation
{
    public partial class MainWindow : Window
    {
        // For screen capture
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref System.Drawing.Point lpPoint);

        [DllImport("user32.dll")]
        private static extern int ShowCursor(bool bShow);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [DllImport("user32.dll")]
        private static extern bool EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);

        // ShowWindow commands
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        public string Windows_Version = "Windows 10";

        // Constants for disabling the close button
        private const uint SC_CLOSE = 0xF060;
        private const uint MF_BYCOMMAND = 0x00000000;
        private const uint MF_GRAYED = 0x00000001;
        private static readonly HttpClient _httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(5) };


        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // Constants
        public const string DEFAULT_OUTPUT_PATH = @"webserver\image_to_process.png";
        private const double CAPTURE_INTERVAL_SECONDS = 1;
        private const int TITLE_BAR_HEIGHT = 50; // Height of our custom title bar (includes 10px for resize)
        private const int FOOTER_BAR_HEIGHT = 30; // Height of our custom title bar (includes 10px for resize)

        bool _bOCRCheckIsWanted = false;
        public void SetOCRCheckIsWanted(bool bCaptureIsWanted) { _bOCRCheckIsWanted = bCaptureIsWanted; }
        public bool GetOCRCheckIsWanted() { return _bOCRCheckIsWanted; }
        private bool isStarted = false;
        public bool isStopOCR = false;
        private DispatcherTimer _captureTimer;
        private string outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DEFAULT_OUTPUT_PATH);
        private WindowInteropHelper helper;
        private System.Drawing.Rectangle captureRect;

        // Store translate area information
        private bool isSelectingTranslationArea = false;
        private Rect selectedTranslationArea;
        public bool hasSelectedTranslationArea = false;
        public List<Rect> savedTranslationAreas = new List<Rect>();
        public int currentAreaIndex = 0;

        // Force update prompt
        private int isForceUpdatePrompt = 7; //increase to force update prompt


        // Store previous capture position to calculate offset
        private int previousCaptureX;
        private int previousCaptureY;

        public bool isCapturingWindow = false;
        private IntPtr capturedWindowHandle = IntPtr.Zero;
        private string capturedWindowTitle = string.Empty;
        // private System.Windows.Controls.Button? selectWindowButton;

        // Auto translation
        private bool isAutoTranslateEnabled = true;

        // ChatBox management
        private ChatBoxWindow? chatBoxWindow;
        private bool isChatBoxVisible = false;
        private bool isSelectingChatBoxArea = false;
        private bool _chatBoxEventsAttached = false;

        // Keep translation history even when ChatBox is closed
        private Queue<TranslationEntry> _translationHistory = new Queue<TranslationEntry>();

        // Accessor for ChatBoxWindow to get the translation history
        public Queue<TranslationEntry> GetTranslationHistory()
        {
            return _translationHistory;
        }

        // Method to clear translation history
        public void ClearTranslationHistory()
        {
            _translationHistory.Clear();
            Console.WriteLine("Translation history cleared from MainWindow");
        }


        //allow this to be accesible through an "Instance" variable
        public static MainWindow Instance { get { return _this!; } }
        // Socket connection status
        private TextBlock? socketStatusText;

        // Console visibility management
        // private bool isConsoleVisible = false;
        private IntPtr consoleWindow;

        static MainWindow? _this = null;

        [DllImport("kernel32.dll")]
        public static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleOutputCP(uint wCodePageID);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleCP(uint wCodePageID);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetCurrentConsoleFontEx(IntPtr hConsoleOutput, bool bMaximumWindow, ref CONSOLE_FONT_INFOEX lpConsoleCurrentFontEx);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int nStdHandle);

        // Console mode control
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        // Standard input handle constant
        public const int STD_INPUT_HANDLE = -10;

        // Console input mode flags
        public const uint ENABLE_ECHO_INPUT = 0x0004;
        public const uint ENABLE_LINE_INPUT = 0x0002;
        public const uint ENABLE_PROCESSED_INPUT = 0x0001;
        public const uint ENABLE_WINDOW_INPUT = 0x0008;
        public const uint ENABLE_MOUSE_INPUT = 0x0010;
        public const uint ENABLE_INSERT_MODE = 0x0020;
        public const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
        public const uint ENABLE_EXTENDED_FLAGS = 0x0080;
        public const uint ENABLE_AUTO_POSITION = 0x0100;

        // Keyboard hooks are now managed in KeyboardShortcuts.cs

        // We'll use a different approach that doesn't rely on SetConsoleCtrlHandler

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct CONSOLE_FONT_INFOEX
        {
            public uint cbSize;
            public uint nFont;
            public COORD dwFontSize;
            public int FontFamily;
            public int FontWeight;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string FaceName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COORD
        {
            public short X;
            public short Y;
        }

        public const int STD_OUTPUT_HANDLE = -11;
        public bool GetIsStarted() { return isStarted; }
        public bool GetTranslateEnabled() { return isAutoTranslateEnabled; }

        // Methods for syncing UI controls with MonitorWindow
        // Flag to prevent saving during initialization
        private static bool _isInitializing = true;


        public void SetOcrMethod(string method)
        {
            Console.WriteLine($"MainWindow.SetOcrMethod called with method: {method} (isInitializing: {_isInitializing})");

            // Only update the MainWindow's internal state during initialization
            // Don't update other windows or save to config
            // if (true)
            // {
            Console.WriteLine($"Setting OCR method during initialization: {method}");
            selectedOcrMethod = method;
            // Important: Update status text even during initialization
            if (method == "Windows OCR")
            {
                SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_UsingBuiltInOcr"], method));
                OcrServerPanel.Visibility = Visibility.Collapsed;
                OcrServerBorder.Visibility = Visibility.Collapsed;
            }
            else if (method == "OneOCR")
            {
                SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_UsingBuiltInOcr"], method));
                OcrServerPanel.Visibility = Visibility.Collapsed;
                OcrServerBorder.Visibility = Visibility.Collapsed;
            }
            else if (method == "EasyOCR")
            {
                SetStatus(LocalizationManager.Instance.Strings["Status_PleaseStartEasyOCR"]);
                OcrServerPanel.Visibility = Visibility.Visible;
                OcrServerBorder.Visibility = Visibility.Visible;
            }
            else if (method == "RapidOCR")
            {
                SetStatus(LocalizationManager.Instance.Strings["Status_PleaseStartRapidOCR"]);
                OcrServerPanel.Visibility = Visibility.Visible;
                OcrServerBorder.Visibility = Visibility.Visible;
            }
            else
            {
                SetStatus(LocalizationManager.Instance.Strings["Status_PleaseStartPaddleOCR"]);
                OcrServerPanel.Visibility = Visibility.Visible;
                OcrServerBorder.Visibility = Visibility.Visible;
            }
            return;
            // }

            // Only process if actually changing the method
            if (selectedOcrMethod != method)
            {
                Console.WriteLine($"MainWindow changing OCR method from {selectedOcrMethod} to {method}");
                selectedOcrMethod = method;
                // No need to handle socket connection here, the MonitorWindow handles that
                if (method == "Windows OCR")
                {
                    if (isStarted)
                    {
                        OnStartButtonToggleClicked(toggleButton, new RoutedEventArgs());
                    }

                    OcrServerManager.Instance.StopOcrServer();
                    SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_UsingBuiltInOcr"], method));
                }
                else
                {
                    if (isStarted)
                    {
                        OnStartButtonToggleClicked(toggleButton, new RoutedEventArgs());
                    }

                    OcrServerManager.Instance.StopOcrServer();
                    SetStatus(LocalizationManager.Instance.Strings["Status_PleaseClickStartServer"]);

                }
            }
        }

        public void GetVersionWindows()
        {
            var version = Environment.OSVersion.Version;
            if (version.Build >= 22000)
            {
                Windows_Version = "Windows 11";
            }
            else
            {
                Windows_Version = "Windows 10";
            }
        }

        public void SetAutoTranslateEnabled(bool enabled)
        {
            if (isAutoTranslateEnabled != enabled)
            {
                isAutoTranslateEnabled = enabled;

                // Save to config
                ConfigManager.Instance.SetAutoTranslateEnabled(enabled);

                // Clear text objects
                Logic.Instance.ClearAllTextObjects();
                Logic.Instance.ResetHash();

                // Force OCR to run again
                SetOCRCheckIsWanted(true);

                MonitorWindow.Instance.RefreshOverlays();
            }
        }

        // The global keyboard hooks are now managed by KeyboardShortcuts class

        public MainWindow()
        {
            // Make sure the initialization flag is set before anything else
            _isInitializing = true;
            Console.WriteLine("MainWindow constructor: Setting _isInitializing to true");

            _this = this;
            InitializeComponent();
            GetVersionWindows();

            // Initialize console but keep it hidden initially
            InitializeConsole();

            // Hide the console window initially
            consoleWindow = GetConsoleWindow();
            KeyboardShortcuts.SetConsoleWindowHandle(consoleWindow);
            ShowWindow(consoleWindow, SW_HIDE);

            // Initialize helper
            helper = new WindowInteropHelper(this);

            // Setup timer for continuous capture
            _captureTimer = new DispatcherTimer();
            _captureTimer.Interval = TimeSpan.FromSeconds(0.25);
            _captureTimer.Tick += OnUpdateTick;
            _captureTimer.Start();

            // Initial update of capture rectangle and setup after window is loaded
            this.Loaded += MainWindow_Loaded;
            if (isForceUpdatePrompt > ConfigManager.Instance.GetForceUpdatePrompt())
            {
                // Gemini
                string gemini_prompt = ConfigManager.Instance.GetDefaultServicePrompt("Gemini");
                if (!string.IsNullOrWhiteSpace(gemini_prompt))
                {
                    // Save to config
                    bool success = ConfigManager.Instance.SaveServicePrompt("Gemini", gemini_prompt);

                    if (success)
                    {
                        Console.WriteLine($"Prompt saved for Gemini");
                    }
                }
                // ChatGPT
                string chatgpt_prompt = ConfigManager.Instance.GetDefaultServicePrompt("ChatGPT");
                if (!string.IsNullOrWhiteSpace(chatgpt_prompt))
                {
                    // Save to config
                    bool success = ConfigManager.Instance.SaveServicePrompt("ChatGPT", chatgpt_prompt);

                    if (success)
                    {
                        Console.WriteLine($"Prompt saved for ChatGPT");
                    }
                }
                // MistralAI
                string mistral_prompt = ConfigManager.Instance.GetDefaultServicePrompt("Mistral");
                if (!string.IsNullOrWhiteSpace(mistral_prompt))
                {
                    // Save to config
                    bool success = ConfigManager.Instance.SaveServicePrompt("Mistral", mistral_prompt);

                    if (success)
                    {
                        Console.WriteLine($"Prompt saved for Mistral");
                    }
                }
                // Groq
                string groq_prompt = ConfigManager.Instance.GetDefaultServicePrompt("Groq");
                if (!string.IsNullOrWhiteSpace(groq_prompt))
                {
                    // Save to config
                    bool success = ConfigManager.Instance.SaveServicePrompt("Groq", groq_prompt);

                    if (success)
                    {
                        Console.WriteLine($"Prompt saved for Groq");
                    }
                }
                // Custom API
                string custom_api_prompt = ConfigManager.Instance.GetDefaultServicePrompt("Custom API");
                if (!string.IsNullOrWhiteSpace(custom_api_prompt))
                {
                    // Save to config
                    bool success = ConfigManager.Instance.SaveServicePrompt("Custom API", custom_api_prompt);

                    if (success)
                    {
                        Console.WriteLine($"Prompt saved for Custom API");
                    }
                }
                // Ollama
                string ollama_prompt = ConfigManager.Instance.GetDefaultServicePrompt("Ollama");
                if (!string.IsNullOrWhiteSpace(ollama_prompt))
                {
                    // Save to config
                    bool success = ConfigManager.Instance.SaveServicePrompt("Ollama", ollama_prompt);

                    if (success)
                    {
                        Console.WriteLine($"Prompt saved for Ollama");
                    }
                }

                // LM Studio
                string lmstudio_prompt = ConfigManager.Instance.GetDefaultServicePrompt("LM Studio");
                if (!string.IsNullOrWhiteSpace(lmstudio_prompt))
                {
                    // Save to config
                    bool success = ConfigManager.Instance.SaveServicePrompt("LM Studio", lmstudio_prompt);

                    if (success)
                    {
                        Console.WriteLine($"Prompt saved for LM Studio");
                    }
                }
                ConfigManager.Instance.SetForceUpdatePrompt(isForceUpdatePrompt);

            }
            MonitorWindow.Instance.Show();
            // Create socket status text block
            CreateSocketStatusIndicator();

            // Get reference to the already initialized ChatBoxWindow
            // chatBoxWindow = ChatBoxWindow.Instance;

            // Register application-wide keyboard shortcut handler
            this.PreviewKeyDown += Application_KeyDown;

            // Register keyboard shortcuts events
            KeyboardShortcuts.StartStopRequested += (s, e) => OnStartButtonToggleClicked(toggleButton, new RoutedEventArgs());
            KeyboardShortcuts.ClearAreasRequested += (s, e) => btnClearSelectionArea_Click(toggleButton, new RoutedEventArgs());
            KeyboardShortcuts.MonitorToggleRequested += (s, e) => MonitorButton_Click(monitorButton, new RoutedEventArgs());
            KeyboardShortcuts.ChatBoxToggleRequested += (s, e) => ChatBoxButton_Click(chatBoxButton, new RoutedEventArgs());
            KeyboardShortcuts.SettingsToggleRequested += (s, e) => SettingsButton_Click(settingsButton, new RoutedEventArgs());
            KeyboardShortcuts.LogToggleRequested += (s, e) => LogButton_Click(logButton, new RoutedEventArgs());
            KeyboardShortcuts.SelectTranslationRegion += (s, e) => SelectAreaButton_Click(selectAreaButton, new RoutedEventArgs());
            KeyboardShortcuts.ClearSelectedAreaRequested += (s, e) => clearSelectedArea();
            KeyboardShortcuts.ShowAreaRequested += (s, e) => ShowAreaButton_Click(showAreaButton, new RoutedEventArgs());
            KeyboardShortcuts.SelectArea1Requested += (s, e) => SwitchToTranslationArea(0);
            KeyboardShortcuts.SelectArea2Requested += (s, e) => SwitchToTranslationArea(1);
            KeyboardShortcuts.SelectArea3Requested += (s, e) => SwitchToTranslationArea(2);
            KeyboardShortcuts.SelectArea4Requested += (s, e) => SwitchToTranslationArea(3);
            KeyboardShortcuts.SelectArea5Requested += (s, e) => SwitchToTranslationArea(4);


            // Set up global keyboard hook to handle shortcuts even when console has focus
            KeyboardShortcuts.InitializeGlobalHook();
        }

        private void SelectAreaButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleTranslationAreaSelector();
        }

        private void ToggleTranslationAreaSelector()
        {
            if (this.WindowState != WindowState.Minimized)
            {
                this.WindowState = WindowState.Minimized;
            }
            if (isSelectingTranslationArea)
            {
                // Cancel translation region selection if active
                isSelectingTranslationArea = false;
                selectAreaButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue

                // Find and close the region selection window if currently open
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window is TranslationAreaSelectorWindow translationSelector)
                    {
                        translationSelector.Close();
                        return;
                    }
                }
                return;
            }
            if (MonitorWindow.Instance.textOverlayCanvas != null)
            {
                MonitorWindow.Instance.RefreshOverlays();
                Logic.Instance.ClearAllTextObjects();
            }

            // Show translation region picker window
            TranslationAreaSelectorWindow selectorWindow = TranslationAreaSelectorWindow.GetInstance();
            selectorWindow.SelectionComplete += TranslationAreaSelector_SelectionComplete;
            selectorWindow.Closed += (s, e) =>
            {
                isSelectingTranslationArea = false;
                if (!hasSelectedTranslationArea)
                {
                    selectAreaButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
                }
            };
            selectorWindow.Show();

            isSelectingTranslationArea = true;
            selectAreaButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red
        }

        // Handle the event when translation region is selected
        private void TranslationAreaSelector_SelectionComplete(object? sender, Rect selectionRect)
        {
            // Check if multiple areas are allowed
            bool allowMultipleAreas = ConfigManager.Instance.IsMultiSelectionAreaEnabled();

            if (allowMultipleAreas)
            {

                if (savedTranslationAreas == null)
                    savedTranslationAreas = new List<Rect>();

                // Add new selection area to the list
                savedTranslationAreas.Add(selectionRect);

                // Limit selection area = 5
                if (savedTranslationAreas.Count > 5)
                {
                    savedTranslationAreas = savedTranslationAreas.Skip(savedTranslationAreas.Count - 5).Take(5).ToList();
                    Console.WriteLine("Maximum of 5 areas allowed. Keeping only the 5 most recent selections.");
                    System.Windows.MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_MaximumAreasAllowed"],
                        LocalizationManager.Instance.Strings["Title_MaximumSelectionArea"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            else
            {
                // Save lastest selection area if multiple areas are not allowed
                savedTranslationAreas = new List<Rect> { selectionRect };
            }

            // Default using lastest selection area
            currentAreaIndex = savedTranslationAreas.Count - 1;

            // Current selection area
            Rect currentRect = savedTranslationAreas[currentAreaIndex];

            // Save current selection area
            selectedTranslationArea = currentRect;
            hasSelectedTranslationArea = true;

            Console.WriteLine($"The translation area has been selected: X={currentRect.X}, Y={currentRect.Y}, Width={currentRect.Width}, Height={currentRect.Height}");
            Console.WriteLine($"Total saved areas: {savedTranslationAreas.Count}, Current index: {currentAreaIndex + 1}");

            // Update capture for new selection area
            UpdateCustomCaptureRect();

            selectAreaButton.Background = new SolidColorBrush(Color.FromRgb(20, 180, 20)); // Green
        }

        public void SwitchToTranslationArea(int index)
        {
            if (savedTranslationAreas.Count == 0)
            {
                Console.WriteLine("No translation areas available. Please select an area first.");
                return;
            }
            if (MonitorWindow.Instance.textOverlayCanvas != null)
            {
                Logic.Instance.ClearAllTextObjects();
                MonitorWindow.Instance.RefreshOverlays();
            }

            // Ensure valid index
            if (index < 0 || index >= savedTranslationAreas.Count)
            {
                Console.WriteLine($"Invalid area index: {index + 1}. Available areas: {savedTranslationAreas.Count}");
                return;
            }

            // Update current index
            currentAreaIndex = index;

            Rect selectedRect = savedTranslationAreas[currentAreaIndex];

            // Update select area
            selectedTranslationArea = selectedRect;
            hasSelectedTranslationArea = true;

            Console.WriteLine($"Switched to translation area {currentAreaIndex + 1}: X={selectedRect.X}, Y={selectedRect.Y}, Width={selectedRect.Width}, Height={selectedRect.Height}");

            // Update capture area for new selection area
            UpdateCustomCaptureRect();

            // Show notification for user
            ShowAreaSwitchNotification(currentAreaIndex + 1);
        }

        private void ShowAreaSwitchNotification(int areaNumber)
        {
            // Create a small notification
            var notification = new System.Windows.Controls.Primitives.Popup
            {
                Width = 200,
                Height = 40,
                IsOpen = true,
                StaysOpen = false,
                AllowsTransparency = true,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute,
                PlacementTarget = this
            };

            // Content notification
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10)
            };

            var text = new TextBlock
            {
                Text = $"Switched to Selection Area {areaNumber}",
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            border.Child = text;
            notification.Child = border;

            // Set position to selected area
            notification.HorizontalOffset = selectedTranslationArea.X + (selectedTranslationArea.Width / 2) - (notification.Width / 2);
            notification.VerticalOffset = selectedTranslationArea.Y + (selectedTranslationArea.Height / 2) - (notification.Height / 2);

            // Auto close notification after 1.5 second
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            SetOCRCheckIsWanted(false);
            timer.Tick += (s, e) =>
            {
                notification.IsOpen = false;
                timer.Stop();
                SetOCRCheckIsWanted(true);
            };

            timer.Start();
        }

        // Set capture area to selected region
        private void UpdateCustomCaptureRect()
        {
            if (!hasSelectedTranslationArea) return;

            captureRect = new System.Drawing.Rectangle(
                (int)selectedTranslationArea.X,
                (int)selectedTranslationArea.Y,
                (int)selectedTranslationArea.Width,
                (int)selectedTranslationArea.Height
            );

            previousCaptureX = captureRect.Left;
            previousCaptureY = captureRect.Top;

            // Update current capture position for Logic
            Logic.Instance.SetCurrentCapturePosition(captureRect.Left, captureRect.Top);
        }

        // Update the position of MonitorWindow based on the selected area
        private void UpdateMonitorWindowToSelectedArea()
        {
            if (!hasSelectedTranslationArea) return;

            // Place the MonitorWindow exactly at the position of the selected area
            MonitorWindow.Instance.Left = selectedTranslationArea.X;
            MonitorWindow.Instance.Top = selectedTranslationArea.Y;

            // Set the size of the MonitorWindow to match the size of the selected area
            MonitorWindow.Instance.Width = selectedTranslationArea.Width;
            MonitorWindow.Instance.Height = selectedTranslationArea.Height;

            // Save the new position for future display
            monitorWindowLeft = selectedTranslationArea.X;
            monitorWindowTop = selectedTranslationArea.Y;

            // Show the MonitorWindow if it is not already displayed
            if (!MonitorWindow.Instance.IsVisible)
            {
                MonitorWindow.Instance.Show();
                monitorButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red
            }

            Console.WriteLine($"The position of the MonitorWindow has been updated according to the selected area.: ({selectedTranslationArea.X}, {selectedTranslationArea.Y}, {selectedTranslationArea.Width}, {selectedTranslationArea.Height})");
        }

        // Add method for show/hide the main window
        private void ToggleMainWindowVisibility()
        {
            if (MainBorder.Visibility == Visibility.Visible)
            {
                HideButton_Click(hideButton, new RoutedEventArgs());
            }
            else
            {
                if (showButton != null)
                {
                    ShowButton_Click(showButton, new RoutedEventArgs());
                }
            }

        }

        public void SetStatus(string text)
        {
            if (socketStatusText != null)
            {
                socketStatusText!.Text = text;
            }
        }

        private void CreateSocketStatusIndicator()
        {
            // Create socket status text
            socketStatusText = new TextBlock
            {
                Text = "Connecting to Python backend...",
                Foreground = new SolidColorBrush(Colors.Red),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0)
            };
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Update capture rectangle
            UpdateCaptureRect();
            SettingsWindow.Instance.ListHotKey_TextChanged();

            // Update app version on setup screen
            AppVersion.Text = LocalizationManager.Instance.Strings["App_Version"] + " " + SplashManager.CurrentVersion.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

            // Add socket status to the header
            if (FooterBorder != null && FooterBorder.Child is Grid footerGrid)
            {
                // Find the StackPanel in the header
                var elements = footerGrid.Children;
                foreach (var element in elements)
                {
                    if (element is StackPanel stackPanel &&
                        stackPanel.HorizontalAlignment == System.Windows.HorizontalAlignment.Left)
                    {
                        // Add socket status text to the stack panel
                        if (socketStatusText != null)
                        {
                            stackPanel.Children.Add(socketStatusText);
                        }
                        break;
                    }
                }
            }

            // Initialize the Logic
            Logic.Instance.Init();

            // Load OCR method from config
            string savedOcrMethod = ConfigManager.Instance.GetOcrMethod();
            Console.WriteLine($"MainWindow_Loaded: Loading OCR method from config: '{savedOcrMethod}'");

            // Set OCR method in this window (MainWindow)
            SetOcrMethod(savedOcrMethod);

            // Subscribe to translation events
            Logic.Instance.TranslationCompleted += Logic_TranslationCompleted;

            // Test configuration loading
            TestConfigLoading();

            // Load language interface settings
            string currentLang = ConfigManager.Instance.GetLanguageInterface();
            foreach (ComboBoxItem item in languageSelector.Items)
            {
                if (item.Tag.ToString() == currentLang)
                {
                    languageSelector.SelectedItem = item;
                    break;
                }
            }
            // if(ConfigManager.Instance.IsAudioServiceAutoTranslateEnabled())
            // {
            //     Task.Run(async () => 
            //     {
            //         try
            //         {
            //             await localWhisperService.Instance.StartServiceAsync((original, translated) =>
            //             {
            //                 Console.WriteLine($"Whisper detected: {original}");
            //             });
            //         }
            //         catch (Exception ex)
            //         {
            //             Console.WriteLine($"Error starting Whisper service: {ex.Message}");
            //         }
            //     });
            //     Console.WriteLine("Local Whisper Service started");
            // }

            // Initialization is complete, now we can save settings changes
            _isInitializing = false;
            Console.WriteLine("MainWindow initialization complete. Settings changes will now be saved.");

            // Force the OCR method to match the config again
            // This ensures the config value is preserved and not overwritten
            string configOcrMethod = ConfigManager.Instance.GetOcrMethod();
            Console.WriteLine($"Ensuring config OCR method is preserved: {configOcrMethod}");
            ConfigManager.Instance.SetOcrMethod(configOcrMethod);

            // Load language settings from config
            LoadLanguageSettingsFromConfig();

            // Load auto-translate setting from config
            isAutoTranslateEnabled = ConfigManager.Instance.IsAutoTranslateEnabled();

            // Register the LocationChanged event to update the position of the MonitorWindow when the MainWindow moves
            this.LocationChanged += MainWindow_LocationChanged;
            // ToggleMonitorWindow();
            CheckAndShowQuickstart();
        }

        // The LocationChanged event to update the position of the MonitorWindow when the MainWindow moves
        private void MainWindow_LocationChanged(object? sender, EventArgs e)
        {
            UpdateCaptureRect();
        }

        // Handler for application-level keyboard shortcuts
        private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!ConfigManager.Instance.IsHotKeyEnabled())
            {
                return;
            }
            // Forward to the central keyboard shortcuts handler
            KeyboardShortcuts.HandleKeyDown(e);
        }

        private void TestConfigLoading()
        {
            try
            {
                // Get and log configuration values
                string apiKey = Logic.Instance.GetGeminiApiKey();
                string llmPrompt = Logic.Instance.GetLlmPrompt();
                string ocrMethod = ConfigManager.Instance.GetOcrMethod();
                string translationService = ConfigManager.Instance.GetCurrentTranslationService();

                Console.WriteLine("=== Configuration Test ===");
                Console.WriteLine($"API Key: {(string.IsNullOrEmpty(apiKey) ? "Not set" : "Set - " + apiKey.Substring(0, 4) + "...")}");
                Console.WriteLine($"LLM Prompt: {(string.IsNullOrEmpty(llmPrompt) ? "Not set" : "Set - " + llmPrompt.Length + " chars")}");
                Console.WriteLine($"OCR Method: {ocrMethod}");
                Console.WriteLine($"Translation Service: {translationService}");

                if (!string.IsNullOrEmpty(llmPrompt))
                {
                    Console.WriteLine("First 100 characters of LLM Prompt:");
                    Console.WriteLine(llmPrompt.Length > 100 ? llmPrompt.Substring(0, 100) + "..." : llmPrompt);
                }

                Console.WriteLine("=========================");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error testing config: {ex.Message}");
            }
        }

        // Update MonitorWindow position
        private void UpdateMonitorWindowPosition()
        {
            double dpiScale = MonitorWindow.Instance.dpiScale;
            if (dpiScale <= 0)
            {
                dpiScale = 1.0;
                Console.WriteLine("Warning: DPI scale was 0 or negative, using default value 1.0");
            }
            // Place the MonitorWindow exactly at the position of the capture area
            MonitorWindow.Instance.Left = captureRect.Left / dpiScale;
            MonitorWindow.Instance.Top = captureRect.Top / dpiScale;

            // double width = Math.Max(1, captureRect.Width);  
            // double height = Math.Max(1, captureRect.Height);

            // Set the size of the MonitorWindow to match the size of the capture area
            MonitorWindow.Instance.Width = Math.Max(1.0, captureRect.Width / dpiScale);
            MonitorWindow.Instance.Height = Math.Max(1.0, captureRect.Height / dpiScale);

            // Save the new position for future display
            monitorWindowLeft = captureRect.Left / dpiScale;
            monitorWindowTop = captureRect.Top / dpiScale;

            // Console.WriteLine($"Updated MonitorWindow position to match capture rect: ({captureRect.Left}, {captureRect.Top}, {captureRect.Width}, {captureRect.Height})");

        }

        private void UpdateCaptureRect()
        {
            // If a custom translation area has been selected, use that area
            if (hasSelectedTranslationArea)
            {
                previousCaptureX = captureRect.Left;
                previousCaptureY = captureRect.Top;

                captureRect = new System.Drawing.Rectangle(
                    (int)selectedTranslationArea.X,
                    (int)selectedTranslationArea.Y,
                    (int)selectedTranslationArea.Width,
                    (int)selectedTranslationArea.Height
                );
            }
            else
            {
                // Use the old method if a custom translation area has not been selected
                // Retrieve the handle using WindowInteropHelper
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                IntPtr hwnd = helper.Handle;
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                // Get our window position including the entire window (client + custom chrome)
                RECT windowRect;
                GetWindowRect(hwnd, out windowRect);

                // Use the custom header's height and footer bar height to exclude them from the capture area
                int customFooterHeight = FOOTER_BAR_HEIGHT;
                int customTitleBarHeight = TITLE_BAR_HEIGHT;
                // Border thickness settings
                int leftBorderThickness = 9;  // Increased from 7 to 9
                int rightBorderThickness = 8; // Adjusted to 8
                int bottomBorderThickness = 9; // Increased from 7 to 9

                // Store previous position for calculating offset
                previousCaptureX = captureRect.Left;
                previousCaptureY = captureRect.Top;

                // Adjust the capture rectangle to exclude the custom title bar and border areas
                captureRect = new System.Drawing.Rectangle(
                    windowRect.Left + leftBorderThickness,
                    windowRect.Top + customTitleBarHeight,
                    (windowRect.Right - windowRect.Left) - leftBorderThickness - rightBorderThickness,
                    (windowRect.Bottom - windowRect.Top) - customTitleBarHeight - customFooterHeight - bottomBorderThickness);
            }

            // If position changed and we have text objects, update their positions
            if ((previousCaptureX != captureRect.Left || previousCaptureY != captureRect.Top) &&
                Logic.Instance.TextObjects.Count > 0)
            {
                // Calculate the offset
                int offsetX = captureRect.Left - previousCaptureX;
                int offsetY = captureRect.Top - previousCaptureY;

                // Apply offset to text objects
                Logic.Instance.UpdateTextObjectPositions(offsetX, offsetY);

                Console.WriteLine($"Capture position changed by ({offsetX}, {offsetY}). Text overlays updated.");
            }


            UpdateMonitorWindowPosition();
        }

        //!Main loop

        private void OnUpdateTick(object? sender, EventArgs e)
        {

            PerformCapture();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Standard header drag functionality
            if (e.ClickCount == 1)
            {
                this.DragMove();
                e.Handled = true;
                UpdateCaptureRect();
            }
        }

        private void OnStartButtonToggleClicked(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button btn = (System.Windows.Controls.Button)sender;
            String method = ConfigManager.Instance.GetOcrMethod();
            bool isReady = false;

            if (method == "Windows OCR" || method == "OneOCR")
            {
                // Windows OCR always ready because it don't need server
                isReady = true;
            }
            else
            {
                // EasyOCR, RapidOCR and PaddleOCR need connect to server
                isReady = socketStatusText != null &&
                        (socketStatusText.Text == $"Successfully connected to {method} server");
            }

            if (isStarted)
            {
                Logic.Instance.ResetHash();
                isStarted = false;
                btn.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("Strings[Btn_Start]") { Source = LocalizationManager.Instance });
                btn.Background = new SolidColorBrush(Color.FromRgb(20, 180, 20)); // Green                                                   
                Logic.Instance.ClearAllTextObjects();
                MonitorWindow.Instance.RefreshOverlays();
                MonitorWindow.Instance.HideTranslationStatus();
                if (ConfigManager.Instance.IsTtsEnabled())
                {
                    WindowsTTSService.StopAllTTS();
                }
                ShowFastNotification(LocalizationManager.Instance.Strings["NotificationTitle_TranslationStopped"], LocalizationManager.Instance.Strings["NotificationMessage_TranslationStopped_Details"]);
            }
            else
            {
                if (isReady)
                {
                    isStarted = true;
                    isStopOCR = false;
                    btn.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("Strings[Btn_Stop]") { Source = LocalizationManager.Instance });
                    UpdateCaptureRect();
                    SetOCRCheckIsWanted(true);
                    btn.Background = new SolidColorBrush(Color.FromRgb(220, 0, 0)); // Red
                    ShowFastNotification(LocalizationManager.Instance.Strings["NotificationTitle_TranslationStarted"], LocalizationManager.Instance.Strings["NotificationMessage_TranslationStarted_Details"]);
                }
                else
                {
                    // Warning message if OCR server is not ready
                    System.Windows.MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_OcrServerNotReady"], method),
                        LocalizationManager.Instance.Strings["Title_OcrServerNotReady"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }


        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            localWhisperService.Instance.Stop();
            this.Close();
        }

        // Override OnClosing to ensure the application shuts down completely
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Stop local whisper service
            localWhisperService.Instance.Stop();

            // Clean up Logic resources
            Logic.Instance.Finish();

            // Dispose TaskbarIcon to remove tray icon and close any balloons
            if (MyNotifyIcon != null)
            {
                MyNotifyIcon.Dispose();
            }

            // Force close MonitorWindow
            if (MonitorWindow.Instance != null)
            {
                MonitorWindow.Instance.ForceClose();
            }

            // Force close all other windows
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window != this && window.IsLoaded)
                {
                    try
                    {
                        window.Close();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error closing window {window.GetType().Name}: {ex.Message}");
                    }
                }
            }

            // Shutdown the entire application
            System.Windows.Application.Current.Shutdown();

            base.OnClosing(e);
        }

        // Button to show the window when it's hidden
        private System.Windows.Controls.Button? showButton;

        private void HideButton_Click(object sender, RoutedEventArgs e)
        {
            // Hide the main window elements
            MainBorder.Visibility = Visibility.Collapsed;

            // Create a small "Show" button that remains visible
            if (showButton == null)
            {
                showButton = new System.Windows.Controls.Button
                {
                    Content = "Show",
                    Width = 30,
                    Height = 20,
                    Padding = new Thickness(2, 0, 2, 0),
                    FontSize = 10,
                    Background = new SolidColorBrush(Color.FromRgb(20, 180, 20)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    Margin = new Thickness(10, 10, 0, 0),
                    // Make sure it receives all input events
                    IsHitTestVisible = true
                };

                // Make button visible to WindowChrome
                WindowChrome.SetIsHitTestVisibleInChrome(showButton, true);

                showButton.Click += ShowButton_Click;

                // Get the main grid
                var mainGrid = this.Content as Grid;
                if (mainGrid != null)
                {
                    // Add the button as the last child (top-most)
                    mainGrid.Children.Add(showButton);

                    // Ensure it's on top by setting a high ZIndex
                    System.Windows.Controls.Panel.SetZIndex(showButton, 1000);

                    Console.WriteLine("Show button added to main grid");
                }
                else
                {
                    Console.WriteLine("ERROR: Couldn't find main grid");
                }
            }
            else
            {
                showButton.Visibility = Visibility.Visible;
            }
        }

        private void ShowButton_Click(object sender, RoutedEventArgs e)
        {
            // Show the main window elements
            MainBorder.Visibility = Visibility.Visible;

            // Hide the show button
            if (showButton != null)
            {
                showButton.Visibility = Visibility.Collapsed;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Get the handle of the main window
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            KeyboardShortcuts.SetMainWindowHandle(handle);


            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0312) // WM_HOTKEY
            {
                handled = KeyboardShortcuts.ProcessHotKey(wParam);
            }

            return IntPtr.Zero;
        }

        private void ShowFastNotification(string title, string message)
        {
            MyNotifyIcon.CloseBalloon();

            FancyBalloon balloon = new FancyBalloon(title, message, MyNotifyIcon);
            MyNotifyIcon.ShowCustomBalloon(balloon, System.Windows.Controls.Primitives.PopupAnimation.Slide, 4000);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Remove global keyboard hook
            KeyboardShortcuts.CleanupGlobalHook();

            // Clean up MouseManager resources
            MouseManager.Instance.Cleanup();

            Logic.Instance.Finish();
            OcrServerManager.Instance.StopOcrServer();
            OcrServerManager.Instance.KillProcessesByPort(9191);

            // Make sure the console is closed
            if (consoleWindow != IntPtr.Zero)
            {
                ShowWindow(consoleWindow, SW_HIDE);
            }
            localWhisperService.Instance.Stop();

            // Make sure the application exits when the main window is closed
            System.Windows.Application.Current.Shutdown();

            base.OnClosed(e);
        }

        // Settings button toggle handler
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleSettingsWindow();
        }

        // Remember the settings window position
        private double settingsWindowLeft = -1;
        private double settingsWindowTop = -1;

        // Show/hide the settings window
        private void ToggleSettingsWindow()
        {
            // Check if settings window is visible
            if (SettingsWindow.Instance.IsVisible)
            {
                // Store current position before hiding
                settingsWindowLeft = SettingsWindow.Instance.Left;
                settingsWindowTop = SettingsWindow.Instance.Top;

                Console.WriteLine($"Saving settings position: {settingsWindowLeft}, {settingsWindowTop}");

                SettingsWindow.Instance.Hide();
                Console.WriteLine("Settings window hidden");
                settingsButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(108, 117, 125));
            }
            else
            {
                SettingsWindow.Instance.ReloadSetting();
                // Always use the remembered position if it has been set
                if (settingsWindowLeft != -1 || settingsWindowTop != -1)
                {
                    // Restore previous position
                    SettingsWindow.Instance.Left = settingsWindowLeft;
                    SettingsWindow.Instance.Top = settingsWindowTop;
                    Console.WriteLine($"Restoring settings position to: {settingsWindowLeft}, {settingsWindowTop}");
                }
                else
                {
                    // Position to the center of mainwindow
                    double mainCenter = this.Left + this.ActualWidth / 2;
                    double mainTop = this.Top;

                    SettingsWindow.Instance.Left = mainCenter;
                    SettingsWindow.Instance.Top = mainTop;
                    Console.WriteLine("No saved position, positioning settings window to the right");
                }

                SettingsWindow.Instance.Show();
                Console.WriteLine($"Settings window shown at position {SettingsWindow.Instance.Left}, {SettingsWindow.Instance.Top}");
                settingsButton.Background = new SolidColorBrush(Color.FromRgb(176, 125, 69)); // Orange
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);


        private Bitmap CaptureWindow(IntPtr handle)
        {
            try
            {
                RECT rect;
                GetWindowRect(handle, out rect);
                int windowWidth = rect.Right - rect.Left;
                int windowHeight = rect.Bottom - rect.Top;

                if (windowWidth <= 0 || windowHeight <= 0)
                {
                    Console.WriteLine($"Invalid window dimensions: {windowWidth}x{windowHeight}");
                    throw new ArgumentException("Invalid window dimensions");
                }

                int captureX = 0;
                int captureY = 0;
                int captureWidth = windowWidth;
                int captureHeight = windowHeight;

                if (hasSelectedTranslationArea && currentAreaIndex >= 0 && currentAreaIndex < savedTranslationAreas.Count)
                {
                    var selectedArea = savedTranslationAreas[currentAreaIndex];

                    captureX = (int)selectedArea.X - rect.Left;
                    captureY = (int)selectedArea.Y - rect.Top;
                    captureWidth = (int)selectedArea.Width;
                    captureHeight = (int)selectedArea.Height;


                    if (captureX < 0) captureX = 0;
                    if (captureY < 0) captureY = 0;
                    if (captureX + captureWidth > windowWidth) captureWidth = windowWidth - captureX;
                    if (captureY + captureHeight > windowHeight) captureHeight = windowHeight - captureY;

                    Console.WriteLine($"Capturing region in window: X={captureX}, Y={captureY}, Width={captureWidth}, Height={captureHeight}");

                    Logic.Instance.SetCurrentCapturePosition(rect.Left + captureX, rect.Top + captureY);
                }
                else
                {
                    Console.WriteLine($"Capturing entire window: Width={windowWidth}, Height={windowHeight}");

                    Logic.Instance.SetCurrentCapturePosition(rect.Left, rect.Top);
                }


                Bitmap fullWindowBmp = new Bitmap(windowWidth, windowHeight);
                using (Graphics g = Graphics.FromImage(fullWindowBmp))
                {
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;

                    IntPtr hdc = g.GetHdc();
                    try
                    {

                        bool success = PrintWindow(handle, hdc, 0x00000002);
                        if (!success)
                        {
                            int error = Marshal.GetLastWin32Error();
                            Console.WriteLine($"PrintWindow failed with error code: {error}");

                            g.ReleaseHdc(hdc);
                            return FallbackCaptureWindow(handle);
                        }
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }

                if (IsBitmapEmpty(fullWindowBmp))
                {
                    Console.WriteLine("PrintWindow produced empty bitmap, trying fallback method...");
                    return FallbackCaptureWindow(handle);
                }

                if (hasSelectedTranslationArea && currentAreaIndex >= 0 && currentAreaIndex < savedTranslationAreas.Count)
                {
                    try
                    {
                        Bitmap regionBmp = new Bitmap(captureWidth, captureHeight);
                        using (Graphics g = Graphics.FromImage(regionBmp))
                        {
                            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;

                            g.DrawImage(fullWindowBmp,
                                        new Rectangle(0, 0, captureWidth, captureHeight),
                                        new Rectangle(captureX, captureY, captureWidth, captureHeight),
                                        GraphicsUnit.Pixel);
                        }

                        fullWindowBmp.Dispose();

                        return regionBmp;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error cropping window region: {ex.Message}");
                        return fullWindowBmp;
                    }
                }

                return fullWindowBmp;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CaptureWindow: {ex.Message}");
                return FallbackCaptureWindow(handle);
            }
        }

        private bool IsBitmapEmpty(Bitmap bmp)
        {
            try
            {
                int sampleSize = 20;
                Random rand = new Random();

                for (int i = 0; i < sampleSize; i++)
                {
                    int x = rand.Next(bmp.Width);
                    int y = rand.Next(bmp.Height);

                    System.Drawing.Color pixel = bmp.GetPixel(x, y);
                    if (pixel.R > 5 || pixel.G > 5 || pixel.B > 5)
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        private Bitmap FallbackCaptureWindow(IntPtr handle)
        {
            try
            {
                // Get window size
                RECT rect;
                GetWindowRect(handle, out rect);
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;

                Bitmap bmp = new Bitmap(width, height);

                // using CopyFromScreen
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;

                    g.CopyFromScreen(rect.Left, rect.Top, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
                }

                return bmp;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in FallbackCaptureWindow: {ex.Message}");

                return new Bitmap(1, 1);
            }
        }

        //!This is where we decide to process the bitmap we just grabbed or not
        private void PerformCapture()
        {
            // Update the capture rectangle to ensure correct dimensions
            UpdateCaptureRect();
            if (isCapturingWindow && capturedWindowHandle != IntPtr.Zero)
            {
                try
                {
                    if (IsWindow(capturedWindowHandle) && IsWindowVisible(capturedWindowHandle))
                    {
                        RECT windowRect;
                        GetWindowRect(capturedWindowHandle, out windowRect);

                        if (hasSelectedTranslationArea && currentAreaIndex >= 0 && currentAreaIndex < savedTranslationAreas.Count)
                        {
                            var selectedArea = savedTranslationAreas[currentAreaIndex];
                        }
                        else
                        {
                            Logic.Instance.SetCurrentCapturePosition(windowRect.Left, windowRect.Top);
                        }


                        using (Bitmap bitmap = CaptureWindow(capturedWindowHandle))
                        {

                            bitmap.Save(outputPath, ImageFormat.Png);


                            if (MonitorWindow.Instance.IsVisible)
                            {
                                MonitorWindow.Instance.UpdateScreenshotFromBitmap();
                            }


                            bool shouldPerformOcr = GetIsStarted() && GetOCRCheckIsWanted() &&
                                                (!isStopOCR || ConfigManager.Instance.IsAutoOCREnabled());

                            if (shouldPerformOcr)
                            {
                                Stopwatch stopwatch = new Stopwatch();
                                stopwatch.Start();

                                SetOCRCheckIsWanted(false);


                                string ocrMethod = GetSelectedOcrMethod();
                                if (ocrMethod == "Windows OCR")
                                {
                                    string sourceLanguage = (sourceLanguageComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString()!;
                                    Logic.Instance.ProcessWithWindowsOCR(bitmap, sourceLanguage);
                                }
                                // else if (ocrMethod != "Windows OCR" && ConfigManager.Instance.IsWindowsOCRIntegrationEnabled())
                                // {
                                //     string sourceLanguage = (sourceLanguageComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString()!;
                                //     Logic.Instance.ProcessWithWindowsOCRIntegration(bitmap, sourceLanguage, outputPath);
                                // }
                                else if (ocrMethod == "OneOCR")
                                {
                                    string sourceLanguage = (sourceLanguageComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString()!;
                                    Logic.Instance.ProcessWithOneOCR(bitmap, sourceLanguage);
                                }
                                else
                                {
                                    Logic.Instance.SendImageToServerOCR(outputPath);
                                }

                                stopwatch.Stop();
                                Console.WriteLine($"OCR processing completed in {stopwatch.ElapsedMilliseconds}ms");
                            }
                        }

                        return;
                    }
                    else
                    {

                        isCapturingWindow = false;
                        capturedWindowHandle = IntPtr.Zero;
                        capturedWindowTitle = string.Empty;

                        Dispatcher.Invoke(() =>
                        {
                            selectWindowButton.Content = "Select Window";
                            selectWindowButton.Background = new SolidColorBrush(Color.FromRgb(69, 107, 160)); // Blue

                            System.Windows.MessageBox.Show(
                                LocalizationManager.Instance.Strings["Msg_WindowLost"],
                                LocalizationManager.Instance.Strings["Title_WindowLost"],
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        });

                        Console.WriteLine("Captured window no longer exists, reverting to normal capture mode");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error capturing window: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");


                    isCapturingWindow = false;
                    capturedWindowHandle = IntPtr.Zero;
                    capturedWindowTitle = string.Empty;

                    Dispatcher.Invoke(() =>
                    {
                        selectWindowButton.Content = "Select Window";
                        selectWindowButton.Background = new SolidColorBrush(Color.FromRgb(69, 107, 160)); // Blue
                    });
                }
            }


            //if capture rect is less than 1 pixel, don't capture
            if (captureRect.Width < 1 || captureRect.Height < 1) return;

            // Create bitmap with window dimensions
            using (Bitmap bitmap = new Bitmap(captureRect.Width, captureRect.Height))
            {
                try
                {
                    // Use direct GDI capture with the overlay hidden
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        // Configure for speed and quality
                        g.CompositingQuality = CompositingQuality.HighSpeed;
                        g.SmoothingMode = SmoothingMode.HighSpeed;
                        g.InterpolationMode = InterpolationMode.Low;
                        g.PixelOffsetMode = PixelOffsetMode.HighSpeed;

                        try
                        {
                            g.CopyFromScreen(
                                captureRect.Left,
                                captureRect.Top,
                                0, 0,
                                bitmap.Size,
                                CopyPixelOperation.SourceCopy);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error during screen capture: {ex.Message}");
                            Console.WriteLine($"Stack trace: {ex.StackTrace}");
                            return;
                        }
                    }
                    // if (Windows_Version == "Windows 10")
                    // {
                    //     // Show overlay again
                    //     if (MonitorWindow.Instance.IsVisible)
                    //     {
                    //         MonitorWindow.Instance.ShowOverlay();
                    //     }
                    // }
                    // Store the current capture coordinates for use with OCR results
                    Logic.Instance.SetCurrentCapturePosition(captureRect.Left, captureRect.Top);

                    // Update Monitor window with the copy (without saving to file)
                    if (MonitorWindow.Instance.IsVisible)
                    {
                        MonitorWindow.Instance.UpdateScreenshotFromBitmap();
                    }

                    bool shouldPerformOcr = GetIsStarted() && GetOCRCheckIsWanted() &&
                                        (!isStopOCR || ConfigManager.Instance.IsAutoOCREnabled());

                    if (shouldPerformOcr)
                    {
                        Stopwatch stopwatch = new Stopwatch();
                        stopwatch.Start();

                        SetOCRCheckIsWanted(false);

                        // Save bitmap to png file
                        Console.WriteLine($"Saving bitmap to {outputPath}");
                        bitmap.Save(outputPath, ImageFormat.Png);

                        // handle OCR
                        string ocrMethod = GetSelectedOcrMethod();
                        if (ocrMethod == "Windows OCR")
                        {
                            string sourceLanguage = (sourceLanguageComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString()!;
                            Logic.Instance.ProcessWithWindowsOCR(bitmap, sourceLanguage);
                        }
                        else if (ocrMethod != "Windows OCR" && ConfigManager.Instance.IsWindowsOCRIntegrationEnabled())
                        {
                            string sourceLanguage = (sourceLanguageComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString()!;
                            Logic.Instance.ProcessWithWindowsOCRIntegration(bitmap, sourceLanguage, outputPath);
                        }
                        else if (ocrMethod == "OneOCR")
                        {
                            string sourceLanguage = (sourceLanguageComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString()!;
                            Logic.Instance.ProcessWithOneOCR(bitmap, sourceLanguage);
                        }
                        else
                        {
                            Logic.Instance.SendImageToServerOCR(outputPath);
                        }

                        stopwatch.Stop();
                        Console.WriteLine($"OCR processing completed in {stopwatch.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    // Handle potential file lock or other errors
                    Console.WriteLine($"Error processing screenshot: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    Thread.Sleep(100);
                }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            // this.WindowState = WindowState.Minimized;
            this.Hide();
        }

        // private void AutoTranslateCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        // {
        //     // Convert sender to CheckBox
        //     if (sender is System.Windows.Controls.CheckBox checkBox)
        //     {
        //         isAutoTranslateEnabled = checkBox.IsChecked ?? false;
        //         Console.WriteLine($"Auto-translate {(isAutoTranslateEnabled ? "enabled" : "disabled")}");
        //         //Clear textobjects
        //         Logic.Instance.ClearAllTextObjects();
        //         Logic.Instance.ResetHash();
        //         //force OCR to run again
        //         SetOCRCheckIsWanted(true);

        //         MonitorWindow.Instance.RefreshOverlays();
        //     }
        // }


        // Reset OCR hash when language selection changes
        private void SourceLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip saving during initialization
            if (_isInitializing)
            {
                return;
            }

            if (sender is System.Windows.Controls.ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string language = selectedItem.Content.ToString() ?? "ja";
                Console.WriteLine($"Source language changed to: {language}");

                // Save to config
                ConfigManager.Instance.SetSourceLanguage(language);
            }

            // Reset the OCR hash to force a fresh comparison after changing source language
            Logic.Instance.ClearAllTextObjects();
        }

        private void TargetLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip saving during initialization
            if (_isInitializing)
            {
                return;
            }

            if (sender is System.Windows.Controls.ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string language = selectedItem.Content.ToString() ?? "en";
                Console.WriteLine($"Target language changed to: {language}");

                // Save to config
                ConfigManager.Instance.SetTargetLanguage(language);
            }

            // Reset the OCR hash to force a fresh comparison after changing target language
            Logic.Instance.ClearAllTextObjects();
        }

        private async Task OcrMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.ComboBox comboBox)
            {
                string? ocrMethod = (comboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();

                if (!string.IsNullOrEmpty(ocrMethod))
                {
                    // Reset the OCR hash to force a fresh comparison after changing OCR method
                    Logic.Instance.ResetHash();

                    Console.WriteLine($"OCR method changed to: {ocrMethod}");

                    // Clear any existing text objects
                    Logic.Instance.ClearAllTextObjects();

                    // Try stop server OCR if haved one running
                    OcrServerManager.Instance.StopOcrServer();
                    SocketManager.Instance.Disconnect();
                    // Update the UI and connection state based on the selected OCR method
                    if (ocrMethod == "Windows OCR" || ocrMethod == "OneOCR")
                    {
                        // Using Windows OCR, no need for socket connection
                        SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_UsingBuiltInOcr"], ocrMethod));
                    }
                    else
                    {
                        // Using EasyOCR, RapidOCR or PaddleOCR, try to connect to the socket server
                        SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_ConnectingToServer"], ocrMethod));

                        _ = OcrServerManager.Instance.StartOcrServerAsync(ocrMethod);
                        while (!OcrServerManager.Instance.serverStarted)
                        {
                            await Task.Delay(100);
                            if (OcrServerManager.Instance.timeoutStartServer)
                            {
                                SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_CannotStartOcrServer"], ocrMethod));
                                System.Windows.MessageBox.Show(
                                    string.Format(LocalizationManager.Instance.Strings["Msg_ServerStartupTimeoutShort"], ocrMethod),
                                    LocalizationManager.Instance.Strings["Title_Error"],
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error);
                                break;
                            }
                        }
                        if (OcrServerManager.Instance.serverStarted)
                        {
                            _ = SocketManager.Instance.TryReconnectAsync();
                            SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_ConnectedToServer"], ocrMethod));

                        }
                        else
                        {
                            SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_CannotConnectToServer"], ocrMethod));
                        }
                    }
                }
            }
        }

        // Keep track of selected OCR method
        private string selectedOcrMethod = "Windows OCR";

        public string GetSelectedOcrMethod()
        {
            return selectedOcrMethod;
        }

        // Toggle the monitor window
        private void MonitorButton_Click(object sender, RoutedEventArgs e)
        {
            if (Windows_Version == "Windows 10" && !isCapturingWindow)
            {
                System.Windows.MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_Win10RequiresSelectWindow"]),
                        LocalizationManager.Instance.Strings["Title_Information"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
            }
            else
            {
                ToggleMonitorWindow();
            }
        }

        // Handler for the Log button click
        private void LogButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleConsoleWindow();
        }

        // Toggle console window visibility
        private void ToggleConsoleWindow()
        {
            if (LogWindow.Instance.IsVisible)
            {
                // Hide log window
                LogWindow.Instance.Hide();
                logButton.Background = new SolidColorBrush(Color.FromRgb(153, 69, 176)); // Purple
            }
            else
            {
                // Show log window
                // Set MainWindow as owner to ensure Log window appears above it
                LogWindow.Instance.Owner = this;
                LogWindow.Instance.Show();
                logButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 153)); // Pink/Red
            }
        }

        // Initialize console window with proper encoding and font
        private void InitializeConsole()
        {
            AllocConsole();

            // Disable the close button on the console window to prevent closing the app
            IntPtr consoleHandle = GetConsoleWindow();
            if (consoleHandle != IntPtr.Zero)
            {
                IntPtr hMenu = GetSystemMenu(consoleHandle, false);
                if (hMenu != IntPtr.Zero)
                {
                    EnableMenuItem(hMenu, SC_CLOSE, MF_BYCOMMAND | MF_GRAYED);
                }
            }

            // Disable console input to prevent the app from freezing
            DisableConsoleInput();

            // Set Windows console code page to UTF-8 (65001)
            SetConsoleCP(65001);
            SetConsoleOutputCP(65001);

            // Set up a proper font for Japanese characters
            IntPtr hConsoleOutput = GetStdHandle(STD_OUTPUT_HANDLE);
            CONSOLE_FONT_INFOEX fontInfo = new CONSOLE_FONT_INFOEX();
            fontInfo.cbSize = (uint)Marshal.SizeOf(fontInfo);
            fontInfo.FaceName = "MS Gothic"; // Font with good Japanese support
            fontInfo.FontFamily = 54; // FF_MODERN and TMPF_TRUETYPE
            fontInfo.FontWeight = 400; // Normal weight
            fontInfo.dwFontSize = new COORD { X = 0, Y = 16 }; // Font size
            SetCurrentConsoleFontEx(hConsoleOutput, false, ref fontInfo);

            // Set .NET console encoding to UTF-8
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            // Redirect standard output to the console with UTF-8 encoding
            StreamWriter standardOutput = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8)
            {
                AutoFlush = true
            };
            Console.SetOut(standardOutput);

            // Write initial message
            Console.WriteLine("Console output initialized. Toggle visibility with the Log button.");
            Console.WriteLine("Note: Console input is disabled to prevent application freeze.");
        }

        // Disable console input to prevent app freezing when focus is in the console
        private void DisableConsoleInput()
        {
            try
            {
                // Get the console input handle
                IntPtr hStdIn = GetStdHandle(STD_INPUT_HANDLE);
                if (hStdIn == IntPtr.Zero || hStdIn == new IntPtr(-1))
                {
                    Console.WriteLine("Error getting console input handle");
                    return;
                }

                // Get current console mode
                uint mode;
                if (!GetConsoleMode(hStdIn, out mode))
                {
                    Console.WriteLine($"Error getting console mode: {Marshal.GetLastWin32Error()}");
                    return;
                }

                // Disable input modes that would cause the app to wait for input
                // This prevents the console from freezing when it gets focus
                // We're turning off all input processing to make the console display-only
                uint newMode = 0; // Set to 0 to disable all input

                // You can selectively re-enable certain input features if needed:
                // newMode = ENABLE_EXTENDED_FLAGS | ENABLE_WINDOW_INPUT;

                if (!SetConsoleMode(hStdIn, newMode))
                {
                    Console.WriteLine($"Error setting console mode: {Marshal.GetLastWin32Error()}");
                    return;
                }

                Console.WriteLine("Console input disabled successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disabling console input: {ex.Message}");
            }
        }

        // Update log button state (called from LogWindow when it's closed)
        public void updateLogButtonState(bool isVisible)
        {
            logButton.Background = isVisible
                ? new SolidColorBrush(Color.FromRgb(176, 69, 153)) // Pink/Red - visible
                : new SolidColorBrush(Color.FromRgb(153, 69, 176)); // Purple - hidden
        }

        // Remember the monitor window position
        private double monitorWindowLeft = -1;
        private double monitorWindowTop = -1;

        private void ToggleMonitorWindow()
        {
            if (MonitorWindow.Instance.imageScrollViewer.Visibility == Visibility.Visible && MonitorWindow.Instance.IsVisible)
            {

                Console.WriteLine($"Saving monitor position: {monitorWindowLeft}, {monitorWindowTop}");

                MonitorWindow.Instance.imageScrollViewer.Visibility = Visibility.Collapsed;
                Console.WriteLine("Monitor window hidden from MainWindow toggle");
                monitorButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
                ShowFastNotification(LocalizationManager.Instance.Strings["NotificationTitle_OverlayHidden"], LocalizationManager.Instance.Strings["NotificationMessage_OverlayHidden_Details"]);
            }
            // else 
            // {
            //     MonitorWindow.Instance.Show();
            //     Console.WriteLine("Monitor window shown from MainWindow toggle");
            //     monitorButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red
            // }
            else
            {
                try
                {
                    Console.WriteLine("Creating new MonitorWindow instance...");
                    // Close and reset instance
                    MonitorWindow.Instance.imageScrollViewer.Visibility = Visibility.Visible;

                    // If a custom translation area has been selected, use that area
                    // if (hasSelectedTranslationArea)
                    // {
                    UpdateMonitorWindowPosition();
                    // }
                    // else
                    // {
                    //     UpdateMonitorWindowPosition();
                    // }

                    // Perform a new capture immediately
                    // using (Bitmap bitmap = new Bitmap(captureRect.Width, captureRect.Height))
                    // {
                    //     using (Graphics g = Graphics.FromImage(bitmap))
                    //     {
                    //         g.CompositingQuality = CompositingQuality.HighSpeed;
                    //         g.SmoothingMode = SmoothingMode.HighSpeed;
                    //         g.InterpolationMode = InterpolationMode.Low;
                    //         g.PixelOffsetMode = PixelOffsetMode.HighSpeed;

                    //         g.CopyFromScreen(
                    //             captureRect.Left,
                    //             captureRect.Top,
                    //             0, 0,
                    //             bitmap.Size,
                    //             CopyPixelOperation.SourceCopy);
                    //     }

                    //     bitmap.Save(outputPath, ImageFormat.Png);

                    //     Console.WriteLine("Updating MonitorWindow with fresh capture");
                    //     MonitorWindow.Instance.UpdateScreenshotFromBitmap();
                    // }
                    MonitorWindow.Instance.UpdateScreenshotFromBitmap();
                    // Refresh overlays
                    MonitorWindow.Instance.RefreshOverlays();

                    monitorButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red
                    Console.WriteLine("MonitorWindow setup complete");
                    ShowFastNotification(LocalizationManager.Instance.Strings["NotificationTitle_OverlayVisible"], LocalizationManager.Instance.Strings["NotificationMessage_OverlayVisible_Details"]);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR in ToggleMonitorWindow: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        // Show current area button click handler
        private void ShowAreaButton_Click(object sender, RoutedEventArgs e)
        {
            // if (!MonitorWindow.Instance.IsVisible)
            // {
            //     ToggleMonitorWindow();
            // }

            // Toggle BorderThickness: 0 <-> 1
            MonitorWindow.Instance.BorderThickness =
                (MonitorWindow.Instance.BorderThickness == new Thickness(1))
                    ? new Thickness(0)
                    : new Thickness(1);
        }

        // ChatBox Button click handler
        private void ChatBoxButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleChatBox();
        }

        // Toggle ChatBox visibility and position
        private void ToggleChatBox()
        {
            if (isSelectingChatBoxArea)
            {
                // Cancel the selection mode if already selecting
                isSelectingChatBoxArea = false;
                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 176, 105)); // Blue

                // Find and close any existing selector window
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window is ChatBoxSelectorWindow selectorWindow)
                    {
                        selectorWindow.Close();
                        return;
                    }
                }
                return;
            }

            // ChatBoxWindow.Instance is always available, but may not be visible
            // Make sure our chatBoxWindow reference is up to date
            // chatBoxWindow = ChatBoxWindow.Instance;

            bool recreateOnShow = ConfigManager.Instance.IsChatboxRecreateOnShowEnabled();

            if (isChatBoxVisible && chatBoxWindow != null)
            {
                // Hide ChatBox
                chatBoxWindow.Hide();
                isChatBoxVisible = false;
                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 176, 105)); // Blue

                // Don't set chatBoxWindow to null here - we're just hiding it, not closing it
            }
            else if (!isChatBoxVisible && !recreateOnShow && chatBoxWindow != null)
            {
                // Show existing ChatBox (reuse)
                chatBoxWindow.Show();
                isChatBoxVisible = true;
                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red
            }
            else
            {
                if (recreateOnShow)
                {
                    // Create and show a fresh ChatBox instance every time
                    try
                    {
                        // If an existing chatBoxWindow exists, fully close it first
                        if (chatBoxWindow != null)
                        {
                            try
                            {
                                chatBoxWindow.ForceClose();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error closing existing ChatBox: {ex.Message}");
                            }
                            chatBoxWindow = null;
                        }

                        // Create a fresh ChatBox instance and show it
                        var newChat = new ChatBoxWindow();
                        chatBoxWindow = ChatBoxWindow.Instance;
                        if (chatBoxWindow != null)
                        {
                            chatBoxWindow.Owner = this;
                            chatBoxWindow.Show();
                            isChatBoxVisible = true;
                            chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red
                        }
                        isChatBoxVisible = true;
                        chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error creating ChatBox: {ex.Message}");
                    }
                }
                else
                {
                    // Show selector to allow user to position ChatBox
                    ChatBoxSelectorWindow selectorWindow = ChatBoxSelectorWindow.GetInstance();
                    selectorWindow.SelectionComplete += ChatBoxSelector_SelectionComplete;
                    selectorWindow.Closed += (s, e) =>
                    {
                        isSelectingChatBoxArea = false;
                        // Only set button to blue if the ChatBox isn't visible (was cancelled)
                        if (!isChatBoxVisible || chatBoxWindow == null || !chatBoxWindow.IsVisible)
                        {
                            chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 176, 105)); // Blue
                        }
                    };
                    selectorWindow.Show();

                    // Set button to red while selector is active
                    isSelectingChatBoxArea = true;
                    chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red
                }
            }
        }

        // Handle selection completion
        private void ChatBoxSelector_SelectionComplete(object? sender, Rect selectionRect)
        {
            // Use the existing ChatBoxWindow.Instance
            chatBoxWindow = ChatBoxWindow.Instance;

            // Check if event handlers are already attached
            if (!_chatBoxEventsAttached && chatBoxWindow != null)
            {
                // Subscribe to both Closed and IsVisibleChanged events
                chatBoxWindow.Closed += (s, e) =>
                {
                    isChatBoxVisible = false;
                    chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
                };

                // Also handle visibility changes for when the X button is clicked (which now hides instead of closes)
                chatBoxWindow.IsVisibleChanged += (s, e) =>
                {
                    if (!(bool)e.NewValue) // Window is now hidden
                    {
                        isChatBoxVisible = false;
                        chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
                    }
                };

                _chatBoxEventsAttached = true;
            }

            // Position and size the ChatBox
            chatBoxWindow!.Left = selectionRect.Left;
            chatBoxWindow.Top = selectionRect.Top;
            chatBoxWindow.Width = selectionRect.Width;
            chatBoxWindow.Height = selectionRect.Height;

            // Show the ChatBox
            chatBoxWindow.Show();
            isChatBoxVisible = true;
            chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red when active

            // The ChatBox will get its data from MainWindow.GetTranslationHistory()
            // No need to manually load entries, just trigger an update
            if (chatBoxWindow != null)
            {
                Console.WriteLine($"Updating ChatBox with {_translationHistory.Count} translation entries");
                chatBoxWindow.UpdateChatHistory();
            }
        }

        public void AddTranslationToHistory(string originalText, string translatedText)
        {
            // Check for duplicate with most recent entry
            if (_translationHistory.Count > 0)
            {
                var lastEntry = _translationHistory.Last();
                if (lastEntry.OriginalText == originalText)
                {
                    Console.WriteLine("Skipping duplicate translation entry");
                    return;
                }
            }

            // Create new entry
            var entry = new TranslationEntry
            {
                OriginalText = originalText,
                TranslatedText = translatedText,
                Timestamp = DateTime.Now
            };

            // Add to history
            _translationHistory.Enqueue(entry);

            // Keep history size limited based on configuration
            int maxHistorySize = ConfigManager.Instance.GetChatBoxHistorySize();
            while (_translationHistory.Count > maxHistorySize)
            {
                _translationHistory.Dequeue();
            }

            //Console.WriteLine($"Translation added to history. History size: {_translationHistory.Count}");
            ChatBoxWindow.Instance!.OnTranslationWasAdded(originalText, translatedText);
            if (ConfigManager.Instance.IsSendDataToServerEnabled())
            {
                _ = Task.Run(() => SendTranslatedTextToServer(translatedText));
            }

        }

        private SocketIOClient.SocketIO? _socketClient;
        private Queue<TranslationItem> _pendingTranslations = new Queue<TranslationItem>();
        private volatile int _isSendingFlag = 0;


        private class TranslationItem
        {
            public string Text { get; set; } = "";
            public int SequenceNumber { get; set; }
        }


        private int _translationSequence = 0;

        private async Task SendTranslatedTextToServer(string text, string serverUrl = "http://localhost:9191")
        {

            if (string.IsNullOrEmpty(text))
                return;


            var translationItem = new TranslationItem
            {
                Text = text,
                SequenceNumber = Interlocked.Increment(ref _translationSequence)
            };

            // Add to queue
            lock (_pendingTranslations)
            {
                _pendingTranslations.Enqueue(translationItem);
            }

            if (Interlocked.CompareExchange(ref _isSendingFlag, 1, 0) == 1)
                return;

            try
            {
                while (true)
                {
                    // Get all translated text in queue
                    List<TranslationItem> itemsToProcess;
                    lock (_pendingTranslations)
                    {
                        if (_pendingTranslations.Count == 0)
                            break;

                        // Get max 10 item
                        itemsToProcess = new List<TranslationItem>();
                        int batchSize = Math.Min(10, _pendingTranslations.Count);
                        for (int i = 0; i < batchSize; i++)
                        {
                            if (_pendingTranslations.Count > 0)
                                itemsToProcess.Add(_pendingTranslations.Dequeue());
                            else
                                break;
                        }
                    }

                    itemsToProcess.Sort((a, b) => a.SequenceNumber.CompareTo(b.SequenceNumber));

                    bool sentViaWebSocket = false;
                    if (_socketClient != null && _socketClient.Connected)
                    {
                        try
                        {
                            foreach (var item in itemsToProcess)
                            {
                                await _socketClient.EmitAsync("send_translation", new
                                {
                                    translation = item.Text,
                                    sequence = item.SequenceNumber
                                });
                            }

                            Console.WriteLine($"✅ Sent {itemsToProcess.Count} translations via WebSocket");
                            sentViaWebSocket = true;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ WebSocket error: {ex.Message}. Falling back to HTTP.");
                            sentViaWebSocket = false;
                        }
                    }

                    // Fallback to HTTP
                    if (!sentViaWebSocket)
                    {
                        try
                        {
                            var batchData = new
                            {
                                translations = itemsToProcess.Select(i => new
                                {
                                    translation = i.Text,
                                    sequence = i.SequenceNumber
                                }).ToArray()
                            };

                            var jsonContent = JsonSerializer.Serialize(batchData);
                            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                            {
                                var response = await _httpClient.PostAsync($"{serverUrl}/api/update-batch", content, cts.Token);

                                if (response.IsSuccessStatusCode)
                                {
                                    Console.WriteLine($"✅ Sent {itemsToProcess.Count} translations via HTTP batch API");

                                    if (_socketClient == null || !_socketClient.Connected)
                                    {
                                        InitSocketIO(serverUrl);
                                    }
                                    continue;
                                }

                                Console.WriteLine("Batch API not available, sending individually");
                            }

                            foreach (var item in itemsToProcess)
                            {
                                try
                                {
                                    var singleData = new
                                    {
                                        translation = item.Text,
                                        sequence = item.SequenceNumber
                                    };
                                    var singleJson = JsonSerializer.Serialize(singleData);
                                    var singleContent = new StringContent(singleJson, Encoding.UTF8, "application/json");

                                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                                    {
                                        var response = await _httpClient.PostAsync($"{serverUrl}/api/update", singleContent, cts.Token);

                                        if (response.IsSuccessStatusCode)
                                        {
                                            Console.WriteLine($"✅ Sent translation #{item.SequenceNumber} via HTTP");
                                        }
                                        else
                                        {
                                            Console.WriteLine($"❌ Failed to send translation #{item.SequenceNumber}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"❌ Error sending individual translation: {ex.Message}");
                                }
                            }

                            if (_socketClient == null || !_socketClient.Connected)
                            {
                                InitSocketIO(serverUrl);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ HTTP batch error: {ex.Message}");
                        }
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isSendingFlag, 0);

                lock (_pendingTranslations)
                {
                    if (_pendingTranslations.Count > 0)
                    {
                        _ = SendTranslatedTextToServer(string.Empty);
                    }
                }
            }
        }

        private void InitSocketIO(string serverUrl = "http://localhost:9191")
        {
            try
            {
                if (_socketClient != null)
                {
                    try { _socketClient.DisconnectAsync().Wait(1000); } catch { }
                }

                _socketClient = new SocketIOClient.SocketIO(serverUrl, new SocketIOClient.SocketIOOptions
                {
                    ConnectionTimeout = TimeSpan.FromSeconds(3),
                    Reconnection = true,
                    ReconnectionAttempts = 3,
                    ReconnectionDelay = 1000
                });

                _socketClient.OnConnected += (sender, e) =>
                {
                    Console.WriteLine("✅ Connected to WebSocket");
                };

                _socketClient.OnDisconnected += (sender, e) =>
                {
                    Console.WriteLine("❌ Disconnected from WebSocket");
                };

                _socketClient.ConnectAsync().Wait(3000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ WebSocket init error: {ex.Message}");
                _socketClient = null;
            }
        }

        // Handle translation events from Logic
        private void Logic_TranslationCompleted(object? sender, TranslationEventArgs e)
        {
            AddTranslationToHistory(e.OriginalText, e.TranslatedText);
        }

        private async void btnStartOcrServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable the button to prevent multiple clicks
                btnStartOcrServer.IsEnabled = false;

                // Get the OCR server port from the configuration
                string ocrMethod = ConfigManager.Instance.GetOcrMethod();


                if (ocrMethod == "Windows OCR" || ocrMethod == "OneOCR")
                {
                    System.Windows.MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_OcrNoStartRequired"], ocrMethod),
                        LocalizationManager.Instance.Strings["Title_WarningExclamation"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    btnStartOcrServer.IsEnabled = true;
                    return;
                }

                SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_StartingOcrServer"], ocrMethod));

                // Start the OCR server
                await OcrServerManager.Instance.StartOcrServerAsync(ocrMethod);
                SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_StartingOcrServer"], ocrMethod));
                var startTime = DateTime.Now;
                while (!OcrServerManager.Instance.serverStarted)
                {
                    await Task.Delay(100);
                    if (OcrServerManager.Instance.timeoutStartServer)
                    {
                        SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_CannotStartOcrServer"], ocrMethod));
                        System.Windows.MessageBox.Show(
                            string.Format(LocalizationManager.Instance.Strings["Msg_ServerStartupTimeout"], ocrMethod),
                            LocalizationManager.Instance.Strings["Title_Error"],
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        break;
                    }
                }



                if (OcrServerManager.Instance.serverStarted)
                {
                    UpdateServerButtonStatus(OcrServerManager.Instance.serverStarted);
                    // Update socket status
                    await SocketManager.Instance.TryReconnectAsync();

                }
                else
                {
                    OcrServerManager.Instance.StopOcrServer();
                }

            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_ErrorStartingOcrServer"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                // Reactivate the button after the operation is complete
                btnStartOcrServer.IsEnabled = true;
            }
        }

        private void btnStopOcrServer_Click(object sender, RoutedEventArgs e)
        {
            if (ConfigManager.Instance.GetOcrMethod() == "PaddleOCR" || ConfigManager.Instance.GetOcrMethod() == "EasyOCR" || ConfigManager.Instance.GetOcrMethod() == "RapidOCR")
            {
                if (isStarted)
                {
                    OnStartButtonToggleClicked(toggleButton, new RoutedEventArgs());
                }
                try
                {
                    // Stop OCR server
                    OcrServerManager.Instance.StopOcrServer();
                    SetStatus(LocalizationManager.Instance.Strings["Status_OcrServerStopped"]);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_ErrorStoppingOcrServer"], ex.Message),
                        LocalizationManager.Instance.Strings["Title_Error"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            else
            {
                System.Windows.MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_OcrNoStopRequired"],
                        LocalizationManager.Instance.Strings["Title_WarningExclamation"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
            }
        }

        private async void btnSetupOcrServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable the button to prevent multiple clicks
                btnSetupOcrServer.IsEnabled = false;

                // Get current OCR method
                string ocrMethod = GetSelectedOcrMethod();


                if (ocrMethod == "Windows OCR")
                {
                    System.Windows.MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_OcrNoInstallRequired"], ocrMethod),
                        LocalizationManager.Instance.Strings["Title_WarningExclamation"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    btnSetupOcrServer.IsEnabled = true;
                    return;
                }

                // Show setup dialog
                MessageBoxResult result = System.Windows.MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_ConfirmOcrInstall"], ocrMethod),
                        LocalizationManager.Instance.Strings["Title_ConfirmInstallation"],
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Show status message
                    SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_SettingUpEnvironment"], ocrMethod));

                    // Run setup
                    await Task.Run(() =>
                    {
                        OcrServerManager.Instance.SetupOcrEnvironment(ocrMethod);
                    });

                    SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_EnvironmentSetupCompleted"], ocrMethod));
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_ErrorInstallingOcrServer"], ex.Message),
                        LocalizationManager.Instance.Strings["Title_Error"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
            }
            finally
            {
                // Reactivate the button after the operation is complete
                btnSetupOcrServer.IsEnabled = true;
            }
        }
        private void clearSelectedArea()
        {
            int numberAreaCount = savedTranslationAreas.Count;
            if (numberAreaCount <= 0)
            {
                Console.WriteLine("No previous area to clear");
                return;
            }
            else
            {

                // Remove the last area from the list
                savedTranslationAreas.RemoveAt(currentAreaIndex);

                // Default switch to area last index
                if (savedTranslationAreas.Count >= 1)
                {
                    SwitchToTranslationArea(savedTranslationAreas.Count - 1);
                }
            }
            Console.WriteLine("Previous translation area have been removed.");
        }

        private void btnClearSelectionArea_Click(object sender, RoutedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => btnClearSelectionArea_Click(sender, e));
                return;
            }
            // Clear all save selection areas
            savedTranslationAreas.Clear();
            hasSelectedTranslationArea = false;
            currentAreaIndex = -1;

            // Set button background to blue
            selectAreaButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue

            // Update capture area to default area
            UpdateCaptureRect();

            Console.WriteLine("All translation areas have been cleared.");

            // Show notification
            System.Windows.MessageBox.Show("All translation areas have been cleared.",
                            "Areas Cleared",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
        }

        public void UpdateServerButtonStatus(bool isConnected)
        {

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateServerButtonStatus(isConnected));
                return;
            }

            // Update button status
            btnStopOcrServer.IsEnabled = isConnected;

            // Update status text
            if (isConnected)
            {
                string ocrMethod = GetSelectedOcrMethod();
                SetStatus($"Successfully connected to {ocrMethod} server");
            }
        }

        private void SelectWindowButton_Click(object sender, RoutedEventArgs e)
        {
            if (isCapturingWindow)
            {
                isCapturingWindow = false;
                capturedWindowHandle = IntPtr.Zero;
                capturedWindowTitle = string.Empty;
                selectWindowButton.Content = "Select Window";
                selectWindowButton.Background = new SolidColorBrush(Color.FromRgb(69, 107, 160)); // Blue
                UpdateCaptureRect();
                if (Windows_Version != "Windows 10")
                {
                    MonitorWindow.Instance.EnableExcludeFromCapture();
                }

                Console.WriteLine("Window capture mode disabled");
            }
            else
            {
                WindowSelectorPopup popup = new WindowSelectorPopup();
                popup.WindowSelected += OnWindowSelected;
                popup.ShowDialog();
            }
        }

        private void OnWindowSelected(IntPtr windowHandle, string windowTitle)
        {
            if (windowHandle != IntPtr.Zero)
            {
                capturedWindowHandle = windowHandle;
                capturedWindowTitle = windowTitle;
                isCapturingWindow = true;

                selectWindowButton.Content = $"Window: {(capturedWindowTitle.Length > 10 ? capturedWindowTitle.Substring(0, 10) + "..." : capturedWindowTitle)}";
                selectWindowButton.Background = new SolidColorBrush(Color.FromRgb(220, 0, 0)); // Red

                Console.WriteLine($"Selected window: {capturedWindowTitle} (Handle: {capturedWindowHandle})");

                System.Windows.MessageBox.Show(
                    $"Now capturing window: {capturedWindowTitle}",
                    "Window Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        // Load language settings from config
        private void LoadLanguageSettingsFromConfig()
        {
            try
            {
                string savedSourceLanguage = ConfigManager.Instance.GetSourceLanguage();
                string savedTargetLanguage = ConfigManager.Instance.GetTargetLanguage();

                Console.WriteLine($"Loading language settings from config: Source={savedSourceLanguage}, Target={savedTargetLanguage}");

                // Set source language if found in config
                if (!string.IsNullOrEmpty(savedSourceLanguage))
                {
                    // Find and select matching ComboBoxItem by content
                    foreach (ComboBoxItem item in sourceLanguageComboBox.Items)
                    {
                        if (string.Equals(item.Content.ToString(), savedSourceLanguage, StringComparison.OrdinalIgnoreCase))
                        {
                            // Temporarily remove event handler to prevent triggering changes
                            sourceLanguageComboBox.SelectionChanged -= SourceLanguageComboBox_SelectionChanged;

                            sourceLanguageComboBox.SelectedItem = item;
                            Console.WriteLine($"Set source language to {savedSourceLanguage}");

                            // Reattach event handler
                            sourceLanguageComboBox.SelectionChanged += SourceLanguageComboBox_SelectionChanged;
                            break;
                        }
                    }
                }

                // Set target language if found in config
                if (!string.IsNullOrEmpty(savedTargetLanguage))
                {
                    // Find and select matching ComboBoxItem by content
                    foreach (ComboBoxItem item in targetLanguageComboBox.Items)
                    {
                        if (string.Equals(item.Content.ToString(), savedTargetLanguage, StringComparison.OrdinalIgnoreCase))
                        {
                            // Temporarily remove event handler to prevent triggering changes
                            targetLanguageComboBox.SelectionChanged -= TargetLanguageComboBox_SelectionChanged;

                            targetLanguageComboBox.SelectedItem = item;
                            Console.WriteLine($"Set target language to {savedTargetLanguage}");

                            // Reattach event handler
                            targetLanguageComboBox.SelectionChanged += TargetLanguageComboBox_SelectionChanged;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading language settings from config: {ex.Message}");
            }
        }

        private void TutorialLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }

        private void QuickStartButton_Click(object sender, RoutedEventArgs e)
        {
            QuickstartWindow quickstartWindow = new QuickstartWindow();
            quickstartWindow.Owner = this;
            quickstartWindow.ShowDialog();
        }

        private void CheckAndShowQuickstart()
        {
            bool showQuickstart = ConfigManager.Instance.IsNeedShowQuickStart();

            if (showQuickstart)
            {
                QuickstartWindow quickstartWindow = new QuickstartWindow();
                quickstartWindow.Owner = this;
                quickstartWindow.ShowDialog();
            }
        }

        private void DiscordButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open browser and go to discord
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://discord.gg/FusrDU5tdn",
                    UseShellExecute = true
                });
                Console.WriteLine("Opening Discord invite link");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening Discord link: {ex.Message}");

                // Show error message
                System.Windows.MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_CannotOpenDiscordLink"], ex.Message),
                        LocalizationManager.Instance.Strings["Title_Error"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
            }
        }

        // private bool isListening = false;
        // private OpenAIRealtimeAudioServiceWhisper? openAIRealtimeAudioService = null;

        // private void ListenButton_Click(object sender, RoutedEventArgs e)
        // {
        //     var btn = (System.Windows.Controls.Button)sender;
        //     if (isListening)
        //     {
        //         isListening = false;
        //         btn.Content = "Listen";
        //         btn.Background = new SolidColorBrush(Color.FromRgb(69, 119, 176)); // Blue
        //         openAIRealtimeAudioService?.Stop();
        //     }
        //     else
        //     {
        //         isListening = true;
        //         btn.Content = "Stop Listening";
        //         btn.Background = new SolidColorBrush(Color.FromRgb(220, 0, 0)); // Red
        //         if (openAIRealtimeAudioService == null)
        //             openAIRealtimeAudioService = new OpenAIRealtimeAudioServiceWhisper();
        //         openAIRealtimeAudioService.StartRealtimeAudioService(OnOpenAITranscriptionReceived);
        //     }
        // }

        private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (languageSelector.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string languageCode)
            {
                LocalizationManager.Instance.CurrentLanguage = languageCode;
                ConfigManager.Instance.SetLanguageInterface(languageCode);
                AppVersion.Text = LocalizationManager.Instance.Strings["App_Version"] + " " + SplashManager.CurrentVersion.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private void OnOpenAITranscriptionReceived(string text, string translatedText)
        {
            Dispatcher.Invoke(() =>
            {

                //!Handle raw transcribed audio
                AddTranslationToHistory(text, translatedText);

                ChatBoxWindow.Instance?.OnTranslationWasAdded(text, translatedText);
            });
        }

        private void TrayStartStop_Click(object sender, RoutedEventArgs e)
        {
            OnStartButtonToggleClicked(toggleButton, e);
        }

        private void TrayOverlay_Click(object sender, RoutedEventArgs e)
        {
            MonitorButton_Click(monitorButton, e);
        }

        private void TraySettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsButton_Click(settingsButton, e);
        }

        private void TrayExit_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }
        private void TrayIcon_DoubleClick(object sender, RoutedEventArgs e)
        {
            this.Show();

            if (this.WindowState == WindowState.Minimized)
            {
                this.WindowState = WindowState.Normal;
            }

            this.Activate();
        }
    }
}