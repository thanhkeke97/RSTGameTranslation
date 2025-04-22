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


namespace UGTLive
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

        bool _bOCRCheckIsWanted = false;
        public void SetOCRCheckIsWanted(bool bCaptureIsWanted) { _bOCRCheckIsWanted = bCaptureIsWanted; }
        public bool GetOCRCheckIsWanted() { return _bOCRCheckIsWanted; }
        private bool isStarted = false;
        private DispatcherTimer _captureTimer;
        private string outputPath = DEFAULT_OUTPUT_PATH;
        private WindowInteropHelper helper;
        private System.Drawing.Rectangle captureRect;
        
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
                    SetStatus("Using Windows OCR (built-in)");
                }
                else
                {
                    SetStatus("Using EasyOCR");
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
                    SetStatus("Using Windows OCR (built-in)");
                }
                else
                {
                    SetStatus("Using EasyOCR");
                    
                    // Ensure we're connected when switching to EasyOCR
                    if (!SocketManager.Instance.IsConnected)
                    {
                        Console.WriteLine("Socket not connected when switching to EasyOCR");
                        _ = Task.Run(async () => {
                            try {
                                bool reconnected = await SocketManager.Instance.TryReconnectAsync();
                                
                                if (!reconnected || !SocketManager.Instance.IsConnected)
                                {
                                    // Only show an error message if explicitly requested by user action
                                    Console.WriteLine("Failed to connect to socket server - EasyOCR will not be available");
                                }

                                
                            }
                            catch (Exception ex) {
                                Console.WriteLine($"Error reconnecting: {ex.Message}");
                                
                                // Show an error message
                                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                                    System.Windows.MessageBox.Show($"Socket connection error: {ex.Message}",
                                        "Connection Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                                });
                            }
                        });
                    }
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
           
            // Add socket status to the header
            if (HeaderBorder != null && HeaderBorder.Child is Grid headerGrid)
            {
                // Find the StackPanel in the header
                var elements = headerGrid.Children;
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
            if (!MonitorWindow.Instance.IsVisible)
            {
                // Position to the right of the main window, only for initial startup
                PositionMonitorWindowToTheRight();
                MonitorWindow.Instance.Show();
                
                // Consider this the initial position for the monitor window toggle
                monitorWindowLeft = MonitorWindow.Instance.Left;
                monitorWindowTop = MonitorWindow.Instance.Top;
                
                // Update monitor button color to red since the monitor is now active
                monitorButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red
            }
            
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
          
        private void UpdateCaptureRect()
        {
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

            // Use the custom header's height
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
                (windowRect.Bottom - windowRect.Top) - customTitleBarHeight - bottomBorderThickness);
                
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

            if (isStarted)
            {
                Logic.Instance.ResetHash();
                isStarted = false;
                btn.Content = "Start";
                btn.Background = new SolidColorBrush(Color.FromRgb(20, 180, 20)); // Green
                //erase any active text objects
                Logic.Instance.ClearAllTextObjects();
                MonitorWindow.Instance.HideTranslationStatus();
            }
            else
            {
                isStarted = true;
                btn.Content = "Stop";
                UpdateCaptureRect();
                SetOCRCheckIsWanted(true);
                btn.Background = new SolidColorBrush(Color.FromRgb(220, 0, 0)); // Red

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

        protected override void OnClosed(EventArgs e)
        {
            // Clean up MouseManager resources
            MouseManager.Instance.Cleanup();
            
            Logic.Instance.Finish();
            
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
                settingsButton.Background = new SolidColorBrush(Color.FromRgb(176, 125, 69)); // Orange
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
                settingsButton.Background = new SolidColorBrush(Color.FromRgb(69, 125, 176)); // Blue-ish
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
                        Logic.Instance.SendImageToEasyOCR(outputPath);
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

        private void OcrMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
                    
                    // Update the UI and connection state based on the selected OCR method
                    if (ocrMethod == "Windows OCR")
                    {
                        // Using Windows OCR, no need for socket connection
                        SocketManager.Instance.Disconnect();
                        SetStatus("Using Windows OCR (built-in)");
                    }
                    else
                    {
                        // Using EasyOCR, try to connect to the socket server
                        if (!SocketManager.Instance.IsConnected)
                        {
                            _ = SocketManager.Instance.TryReconnectAsync();
                            SetStatus("Connecting to Python backend...");
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
            }
        }
        
        // Initialize console window with proper encoding and font
        private void InitializeConsole()
        {
            AllocConsole();
            
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
        }
        
        // Position the monitor window to the right of the main window
        private void PositionMonitorWindowToTheRight()
        {
            // Get the position of the main window
            double mainRight = this.Left + this.ActualWidth;
            double mainTop = this.Top;
            
            // Set the position of the monitor window
            MonitorWindow.Instance.Left = mainRight + 10; // 10px gap between windows
            MonitorWindow.Instance.Top = mainTop;
        }
        
        // Remember the monitor window position
        private double monitorWindowLeft = -1;
        private double monitorWindowTop = -1;
        
        // Show/hide the monitor window
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
                // Always use the remembered position if it has been set
                // We use Double.MinValue as our uninitialized flag
                if (monitorWindowLeft != -1 || monitorWindowTop != -1)
                {
                    // Restore previous position
                    MonitorWindow.Instance.Left = monitorWindowLeft;
                    MonitorWindow.Instance.Top = monitorWindowTop;
                    Console.WriteLine($"Restoring monitor position to: {monitorWindowLeft}, {monitorWindowTop}");
                }
                else
                {
                    // Only position to the right if we don't have a saved position yet
                    // This should only happen on first run
                    PositionMonitorWindowToTheRight();
                    Console.WriteLine("No saved position, positioning monitor window to the right");
                }
                
                MonitorWindow.Instance.Show();
                Console.WriteLine($"Monitor window shown at position {MonitorWindow.Instance.Left}, {MonitorWindow.Instance.Top}");
                monitorButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red
                
                // If we have a recent screenshot, load it
                if (File.Exists(outputPath))
                {
                    MonitorWindow.Instance.UpdateScreenshot(outputPath);
                    MonitorWindow.Instance.RefreshOverlays();
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
                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
                
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
                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
                
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
                        chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
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
    }
}