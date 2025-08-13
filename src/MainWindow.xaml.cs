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
        
        // ShowWindow commands
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        // Constants
        private const string DEFAULT_OUTPUT_PATH = @"webserver\image_to_process.png";
        private const double CAPTURE_INTERVAL_SECONDS = 1;
        private const int TITLE_BAR_HEIGHT = 50; // Height of our custom title bar (includes 10px for resize)
        private const int FOOTER_BAR_HEIGHT = 30; // Height of our custom title bar (includes 10px for resize)

        bool _bOCRCheckIsWanted = false;
        public void SetOCRCheckIsWanted(bool bCaptureIsWanted) { _bOCRCheckIsWanted = bCaptureIsWanted; }
        public bool GetOCRCheckIsWanted() { return _bOCRCheckIsWanted; }
        private bool isStarted = false;
        private DispatcherTimer _captureTimer;
        private string outputPath = DEFAULT_OUTPUT_PATH;
        private WindowInteropHelper helper;
        private System.Drawing.Rectangle captureRect;

        // Store translate area information
        private bool isSelectingTranslationArea = false;
        private Rect selectedTranslationArea;
        private bool hasSelectedTranslationArea = false;
        private List<Rect> savedTranslationAreas = new List<Rect>();
        private int currentAreaIndex = 0;


        // Store previous capture position to calculate offset
        private int previousCaptureX;
        private int previousCaptureY;
        
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
        private bool isConsoleVisible = false;
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
            if (_isInitializing)
            {
                Console.WriteLine($"Setting OCR method during initialization: {method}");
                selectedOcrMethod = method;
                // Important: Update status text even during initialization
                if (method == "Windows OCR")
                {
                    SetStatus($"Using {method} (built-in)");
                }
                else if (method == "EasyOCR")
                {
                    SetStatus("Please start EasyOCR server");
                }
                else
                {
                    SetStatus("Please start PaddleOCR server");
                }
                return;
            }
            
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
                    SetStatus($"Using {method} (built-in)");
                }
                else
                {
                    if (isStarted)
                    {
                        OnStartButtonToggleClicked(toggleButton, new RoutedEventArgs());
                    }

                    OcrServerManager.Instance.StopOcrServer();
                    SetStatus($"Please click StartServer button to reconnect server");


                    // // Ensure we're connected when switching to new OCR
                    // if (!SocketManager.Instance.IsConnected)
                    // {
                    //     Console.WriteLine($"Socket not connected when switching to {method}");
                    //     _ = Task.Run(async () =>
                    //     {
                    //         try
                    //         {
                    //             bool reconnected = await SocketManager.Instance.TryReconnectAsync();

                    //             if (!reconnected || !SocketManager.Instance.IsConnected)
                    //             {
                    //                 // Only show an error message if explicitly requested by user action
                    //                 Console.WriteLine("Failed to connect to socket server - EasyOCR will not be available");
                    //             }


                    //         }
                    //         catch (Exception ex)
                    //         {
                    //             Console.WriteLine($"Error reconnecting: {ex.Message}");

                    //             // Show an error message
                    //             System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    //             {
                    //                 System.Windows.MessageBox.Show($"Socket connection error: {ex.Message}",
                    //                     "Connection Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    //             });
                    //         }
                    //     });
                    // }
                }
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
            _captureTimer.Interval = TimeSpan.FromSeconds(1 / 60.0f);
            _captureTimer.Tick += OnUpdateTick;
            _captureTimer.Start();

            // Initial update of capture rectangle and setup after window is loaded
            this.Loaded += MainWindow_Loaded;

            // Create socket status text block
            CreateSocketStatusIndicator();

            // Get reference to the already initialized ChatBoxWindow
            chatBoxWindow = ChatBoxWindow.Instance;

            // Register application-wide keyboard shortcut handler
            this.PreviewKeyDown += Application_KeyDown;

            // Register keyboard shortcuts events
            KeyboardShortcuts.StartStopRequested += (s, e) => OnStartButtonToggleClicked(toggleButton, new RoutedEventArgs());
            KeyboardShortcuts.MonitorToggleRequested += (s, e) => MonitorButton_Click(monitorButton, new RoutedEventArgs());
            KeyboardShortcuts.ChatBoxToggleRequested += (s, e) => ChatBoxButton_Click(chatBoxButton, new RoutedEventArgs());
            KeyboardShortcuts.SettingsToggleRequested += (s, e) => SettingsButton_Click(settingsButton, new RoutedEventArgs());
            KeyboardShortcuts.LogToggleRequested += (s, e) => LogButton_Click(logButton, new RoutedEventArgs());
            KeyboardShortcuts.SelectTranslationRegion += (s, e) => SelectAreaButton_Click(selectAreaButton, new RoutedEventArgs());
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
                    System.Windows.MessageBox.Show($"Maximum of 5 areas allowed. Keeping only the 5 most recent selections.",
                        "Maximum selection area", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        
        private void SwitchToTranslationArea(int index)
        {
            if (savedTranslationAreas.Count == 0)
            {
                Console.WriteLine("No translation areas available. Please select an area first.");
                return;
            }
            
            // Ensure valid index
            if (index < 0 || index >= savedTranslationAreas.Count)
            {
                Console.WriteLine($"Invalid area index: {index+1}. Available areas: {savedTranslationAreas.Count}");
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
                Placement = System.Windows.Controls.Primitives.PlacementMode.Center,
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
            
            // Auto close notification after 1.5 second
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            
            timer.Tick += (s, e) =>
            {
                notification.IsOpen = false;
                timer.Stop();
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

            // Update app version on setup screen
            AppVersion.Text = "Version " + SplashManager.CurrentVersion;

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

            // Make sure monitor window is shown on startup to the right of the main window
            // if (!MonitorWindow.Instance.IsVisible)
            // {
            //     // Position to the right of the main window, only for initial startup
            //     PositionMonitorWindowToTheRight();
            //     MonitorWindow.Instance.Show();

            //     // Consider this the initial position for the monitor window toggle
            //     monitorWindowLeft = MonitorWindow.Instance.Left;
            //     monitorWindowTop = MonitorWindow.Instance.Top;

            //     // Update monitor button color to red since the monitor is now active
            //     monitorButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red
            // }

            // Test configuration loading
            TestConfigLoading();

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
        }

        // The LocationChanged event to update the position of the MonitorWindow when the MainWindow moves
        private void MainWindow_LocationChanged(object? sender, EventArgs e)
        {
            UpdateCaptureRect();
        }
        
        // Handler for application-level keyboard shortcuts
        private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
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
            
            if (method == "Windows OCR")
            {
                // Windows OCR always ready because it don't need server
                isReady = true;
            }
            else 
            {
                // EasyOCR and PaddleOCR need connect to server
                isReady = socketStatusText != null &&
                        (socketStatusText.Text == $"Successfully connected to {method} server");
            }
           
            if (isStarted)
            {
                Logic.Instance.ResetHash();
                isStarted = false;
                btn.Content = "Start";
                btn.Background = new SolidColorBrush(Color.FromRgb(20, 180, 20)); // Green                                                                //erase any active text objects
                Logic.Instance.ClearAllTextObjects();
                MonitorWindow.Instance.HideTranslationStatus();
            }
            else
            {
                if (isReady)
                {
                    isStarted = true;
                    btn.Content = "Stop";
                    UpdateCaptureRect();
                    SetOCRCheckIsWanted(true);
                    btn.Background = new SolidColorBrush(Color.FromRgb(220, 0, 0)); // Red
                }
                else
                {
                    // Warning message if OCR server is not ready
                    System.Windows.MessageBox.Show($"Please make sure {method} server is ready before starting.",
                        "OCR Server Not Ready", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
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

        protected override void OnClosed(EventArgs e)
        {
            // Remove global keyboard hook
            KeyboardShortcuts.CleanupGlobalHook();

            // Clean up MouseManager resources
            MouseManager.Instance.Cleanup();
            
            Logic.Instance.Finish();
            OcrServerManager.Instance.StopOcrServer();

            // Make sure the console is closed
            if (consoleWindow != IntPtr.Zero)
            {
                ShowWindow(consoleWindow, SW_HIDE);
            }

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
                    // Position to the right of the main window for first run
                    double mainRight = this.Left + this.ActualWidth;
                    double mainTop = this.Top;
                    
                    SettingsWindow.Instance.Left = mainRight + 10; // 10px gap
                    SettingsWindow.Instance.Top = mainTop;
                    Console.WriteLine("No saved position, positioning settings window to the right");
                }
                
                SettingsWindow.Instance.Show();
                Console.WriteLine($"Settings window shown at position {SettingsWindow.Instance.Left}, {SettingsWindow.Instance.Top}");
                settingsButton.Background = new SolidColorBrush(Color.FromRgb(176, 125, 69)); // Orange
            }
        }

        //!This is where we decide to process the bitmap we just grabbed or not
        private void PerformCapture()
        {

            if (helper.Handle == IntPtr.Zero) return;

            // Update the capture rectangle to ensure correct dimensions
            UpdateCaptureRect();

            //if capture rect is less than 1 pixel, don't capture
            if (captureRect.Width < 1 || captureRect.Height < 1) return;

            // Create bitmap with window dimensions
            using (Bitmap bitmap = new Bitmap(captureRect.Width, captureRect.Height))
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
                    }
                      
                }
                
                // Store the current capture coordinates for use with OCR results
                Logic.Instance.SetCurrentCapturePosition(captureRect.Left, captureRect.Top);

                try
                {

                    // Update Monitor window with the copy (without saving to file)
                    if (MonitorWindow.Instance.IsVisible)
                    {
                        MonitorWindow.Instance.UpdateScreenshotFromBitmap(bitmap);
                    }

                    //do we actually want to do OCR right now?  
                    if (!GetIsStarted()) return;

                    if (!GetOCRCheckIsWanted())
                    {
                        return;
                    }

                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    SetOCRCheckIsWanted(false);

                    // Check if we're using Windows OCR - if so, process in memory without saving
                    if (GetSelectedOcrMethod() == "Windows OCR")
                    {
                        string sourceLanguage = (sourceLanguageComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString()!;
                        Logic.Instance.ProcessWithWindowsOCR(bitmap, sourceLanguage);
                    }
                    else
                    {
                        //write saving bitmap to log
                        Console.WriteLine($"Saving bitmap to {outputPath}");
                        bitmap.Save(outputPath, ImageFormat.Png);
                        Logic.Instance.SendImageToServerOCR(outputPath);
                    }

                    stopwatch.Stop();
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
            this.WindowState = WindowState.Minimized;
        }
        
        private void AutoTranslateCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Convert sender to CheckBox
            if (sender is System.Windows.Controls.CheckBox checkBox)
            {
                isAutoTranslateEnabled = checkBox.IsChecked ?? false;
                Console.WriteLine($"Auto-translate {(isAutoTranslateEnabled ? "enabled" : "disabled")}");
                //Clear textobjects
                Logic.Instance.ClearAllTextObjects();
                Logic.Instance.ResetHash();
                //force OCR to run again
                SetOCRCheckIsWanted(true);

                MonitorWindow.Instance.RefreshOverlays();
            }
        }
        
   
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
                    if (ocrMethod == "Windows OCR")
                    {
                        // Using Windows OCR, no need for socket connection
                        SetStatus($"Using {ocrMethod} (built-in)");
                    }
                    else
                    {
                        // Using EasyOCR or PaddleOCR, try to connect to the socket server
                        SetStatus($"Connecting to Server {ocrMethod}.");

                        _ = OcrServerManager.Instance.StartOcrServerAsync(ocrMethod);
                        while (!OcrServerManager.Instance.serverStarted)
                        {
                            await Task.Delay(100);
                            if (OcrServerManager.Instance.timeoutStartServer)
                            {
                                SetStatus($"Cannot start {ocrMethod} server");
                                System.Windows.MessageBox.Show($"Server startup timeout {ocrMethod}. Please check if the environment has been installed.",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                break;
                            }
                        }
                        if (OcrServerManager.Instance.serverStarted)
                        {
                            _ = SocketManager.Instance.TryReconnectAsync();
                            SetStatus($"Connected to Server {ocrMethod}.");

                        }
                        else
                        {
                            SetStatus($"Can not connected to Server {ocrMethod}.");
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

            ToggleMonitorWindow();   
        }
        
        // Handler for the Log button click
        private void LogButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleConsoleWindow();
        }
        
        // Toggle console window visibility
        private void ToggleConsoleWindow()
        {
            if (consoleWindow == IntPtr.Zero)
            {
                consoleWindow = GetConsoleWindow();

                // If console window handle is still null, the console might not be initialized
                if (consoleWindow == IntPtr.Zero)
                {
                    InitializeConsole();
                    consoleWindow = GetConsoleWindow();
                }
            }

            if (isConsoleVisible)
            {
                // Hide console
                ShowWindow(consoleWindow, SW_HIDE);
                isConsoleVisible = false;
                logButton.Background = new SolidColorBrush(Color.FromRgb(153, 69, 176)); // Purple
            }
            else
            {
                // Show console
                ShowWindow(consoleWindow, SW_SHOW);
                isConsoleVisible = true;
                logButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 153)); // Pink/Red

                // Write a header message if being shown for the first time
                Console.WriteLine("\n=== Console Log Visible ===");
                Console.WriteLine("Application log messages will appear here.");
                Console.WriteLine("==========================\n");

                // Ensure console input is disabled to prevent freezing
                DisableConsoleInput();
            }
        }
        
        // Initialize console window with proper encoding and font
        private void InitializeConsole()
        {
            AllocConsole();
            
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
        
        // Position the monitor window to the right of the main window
        private void PositionMonitorWindowToTheRight()
        {
            UpdateMonitorWindowPosition();
        }
        
        // Remember the monitor window position
        private double monitorWindowLeft = -1;
        private double monitorWindowTop = -1;
        
        private void ToggleMonitorWindow()
        {
            if (MonitorWindow.Instance.IsVisible)
            {
                // Store current position before hiding
                monitorWindowLeft = MonitorWindow.Instance.Left;
                monitorWindowTop = MonitorWindow.Instance.Top;

                Console.WriteLine($"Saving monitor position: {monitorWindowLeft}, {monitorWindowTop}");

                MonitorWindow.Instance.Hide();
                Console.WriteLine("Monitor window hidden from MainWindow toggle");
                monitorButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
            }
            else
            {
                try
                {
                    // Close and reset instance
                    MonitorWindow.ResetInstance();

                    Console.WriteLine("Creating new MonitorWindow instance...");

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
                    using (Bitmap bitmap = new Bitmap(captureRect.Width, captureRect.Height))
                    {
                        using (Graphics g = Graphics.FromImage(bitmap))
                        {
                            g.CompositingQuality = CompositingQuality.HighSpeed;
                            g.SmoothingMode = SmoothingMode.HighSpeed;
                            g.InterpolationMode = InterpolationMode.Low;
                            g.PixelOffsetMode = PixelOffsetMode.HighSpeed;

                            g.CopyFromScreen(
                                captureRect.Left,
                                captureRect.Top,
                                0, 0,
                                bitmap.Size,
                                CopyPixelOperation.SourceCopy);
                        }

                        bitmap.Save(outputPath, ImageFormat.Png);

                        Console.WriteLine("Updating MonitorWindow with fresh capture");
                        MonitorWindow.Instance.UpdateScreenshotFromBitmap(bitmap);
                    }

                    // Refresh overlays
                    MonitorWindow.Instance.RefreshOverlays();

                    monitorButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red
                    Console.WriteLine("MonitorWindow setup complete");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR in ToggleMonitorWindow: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
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
            chatBoxWindow = ChatBoxWindow.Instance;
            
            if (isChatBoxVisible && chatBoxWindow != null)
            {
                // Hide ChatBox
                chatBoxWindow.Hide();
                isChatBoxVisible = false;
                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 176, 105)); // Blue
                
                // Don't set chatBoxWindow to null here - we're just hiding it, not closing it
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
                string ocrMethod = GetSelectedOcrMethod();
                

                if (ocrMethod == "Windows OCR")
                {
                    System.Windows.MessageBox.Show($"{ocrMethod} doesn't require starting a server.", "Warning!!", MessageBoxButton.OK, MessageBoxImage.Information);
                    btnStartOcrServer.IsEnabled = true;
                    return;
                }
                
                SetStatus($"Starting {ocrMethod} server...");
                
                // Start the OCR server
                await OcrServerManager.Instance.StartOcrServerAsync(ocrMethod);
                SetStatus($"Starting {ocrMethod} server ...");
                var startTime = DateTime.Now;
                while (!OcrServerManager.Instance.serverStarted) 
                {
                    await Task.Delay(100); // Kiểm tra mỗi 100ms    
                    if (OcrServerManager.Instance.timeoutStartServer)
                    {
                        SetStatus($"Cannot start {ocrMethod} server");
                        System.Windows.MessageBox.Show($"Server startup timeout {ocrMethod}. Please check if the environment has been installed and try again.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                System.Windows.MessageBox.Show($"Error starting OCR server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Reactivate the button after the operation is complete
                btnStartOcrServer.IsEnabled = true;
            }
        }

        private void btnStopOcrServer_Click(object sender, RoutedEventArgs e)
        {
            if (ConfigManager.Instance.GetOcrMethod() == "PaddleOCR" || ConfigManager.Instance.GetOcrMethod() == "EasyOCR")
            {
                if (isStarted)
                {
                    OnStartButtonToggleClicked(toggleButton, new RoutedEventArgs());
                }
                try
                {
                    // Stop OCR server
                    OcrServerManager.Instance.StopOcrServer();
                    SetStatus("OCR server has been stopped");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error when stopping OCR server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                System.Windows.MessageBox.Show("This OCR doesn't require stopping a server.", "Warning!!", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    System.Windows.MessageBox.Show($"{ocrMethod} doesn't require installing a environment.", "Warning!!", MessageBoxButton.OK, MessageBoxImage.Information);
                    btnSetupOcrServer.IsEnabled = true;
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
                    // Show status message
                    SetStatus($"Setting up environment for {ocrMethod}...");
                    
                    // Run setup
                    await Task.Run(() => {
                        OcrServerManager.Instance.SetupOcrEnvironment(ocrMethod);
                    });
                    
                    SetStatus($"{ocrMethod} environment setup completed");
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error installing OCR server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Reactivate the button after the operation is complete
                btnSetupOcrServer.IsEnabled = true;
            }
        }

        private void btnClearSelectionArea_Click(object sender, RoutedEventArgs e)
        {
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
                System.Windows.MessageBox.Show($"Cannot open discord link: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool isListening = false;
        private OpenAIRealtimeAudioServiceWhisper? openAIRealtimeAudioService = null;

        private void ListenButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = (System.Windows.Controls.Button)sender;
            if (isListening)
            {
                isListening = false;
                btn.Content = "Listen";
                btn.Background = new SolidColorBrush(Color.FromRgb(69, 119, 176)); // Blue
                openAIRealtimeAudioService?.Stop();
            }
            else
            {
                isListening = true;
                btn.Content = "Stop Listening";
                btn.Background = new SolidColorBrush(Color.FromRgb(220, 0, 0)); // Red
                if (openAIRealtimeAudioService == null)
                    openAIRealtimeAudioService = new OpenAIRealtimeAudioServiceWhisper();
                openAIRealtimeAudioService.StartRealtimeAudioService(OnOpenAITranscriptionReceived);
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
    }
}