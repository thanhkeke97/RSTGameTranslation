using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
using RST;


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
        private const double CAPTURE_INTERVAL_SECONDS = 1;
        private const int TITLE_BAR_HEIGHT = 50; // Height of our custom title bar (includes 10px for resize)
        private const int FOOTER_BAR_HEIGHT = 30; // Height of our custom title bar (includes 10px for resize)

        bool _bOCRCheckIsWanted = false;
        public void SetOCRCheckIsWanted(bool bCaptureIsWanted) { _bOCRCheckIsWanted = bCaptureIsWanted; }
        public bool GetOCRCheckIsWanted() { return _bOCRCheckIsWanted; }
        private bool isStarted = false;
        public bool isStopOCR = false;

        // Central gate deciding whether the capture loop should run OCR this tick.
        // Semantics:
        //   - Auto-OCR on  -> run every tick (continuous mode).
        //   - Auto-OCR off -> single-shot: OCR runs until one pass completes,
        //     then the isStopOCR latch is set and OCR stops. The latch is set
        //     at the end of a completed pass: in Logic.TranslateTextObjectsAsync
        //     when translation runs, or in Logic's translate-disabled branch
        //     (auto-translate off = OCR once per trigger without translating).
        //     The Start button clears the latch to arm the next single shot.
        private bool ShouldPerformOcrNow()
        {
            if (!GetIsStarted() || !GetOCRCheckIsWanted())
                return false;

            return !isStopOCR || ConfigManager.Instance.IsAutoOCREnabled();
        }
        private DispatcherTimer _captureTimer;
        // Skips OCR of frames that look the same as the last OCR'd one
        private readonly FrameChangeDetector _frameChangeDetector = new FrameChangeDetector();
        private DateTime _lastOcrStartTime = DateTime.MinValue;
        private static readonly TimeSpan UnchangedFrameMaxSkip = TimeSpan.FromSeconds(2);
        private WindowInteropHelper helper;
        private System.Drawing.Rectangle captureRect;

        // Store translate area information
        private bool isSelectingTranslationArea = false;
        public bool hasSelectedTranslationArea = false;
        public List<TranslationAreaInfo> savedTranslationAreas = new List<TranslationAreaInfo>();
        public int currentAreaIndex = 0;
        // Select Window mode: where the game window was when the areas were last placed, so the areas
        // can move with it. Null until the next capture tick records it.
        private System.Drawing.Point? _areaWindowAnchor;

        // Exclude regions for masking, in physical screen pixels. Always the same list instance
        // (other windows hold on to it); listen to ExcludeRegionsChanged for updates.
        public readonly List<Rect> excludeRegions = new List<Rect>();
        public event EventHandler? ExcludeRegionsChanged;
        private const int MaxExcludeRegions = 5;
        private bool isSelectingExcludeRegion = false;
        private bool _showExcludeRegions = true;

        // Force update prompt
        private int isForceUpdatePrompt = 8; //increase to force update prompt


        // Store previous capture position to calculate offset
        private int previousCaptureX;
        private int previousCaptureY;

        public bool isCapturingWindow = false;
        private IntPtr capturedWindowHandle = IntPtr.Zero;
        private string capturedWindowTitle = string.Empty;
        private GraphicsCaptureService? _graphicsCaptureService;
        // private System.Windows.Controls.Button? selectWindowButton;

        // Auto translation
        private bool isAutoTranslateEnabled = true;

        // ChatBox management
        private ChatBoxWindow? chatBoxWindow;
        private bool isChatBoxVisible = false;
        private bool isSelectingChatBoxArea = false;
        // Compact view (hotkeys/details) state
        private bool _isCompactView = true;
        // Saved window height to restore after expanding from compact view
        private double _savedWindowHeightForCompact = double.NaN;
        // private bool _chatBoxEventsAttached = false;

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
            // Only built-in OCR (OneOCR, Windows OCR) is supported now.
            // Third-party Python OCR engines (EasyOCR, PaddleOCR, RapidOCR) have been removed.
            Console.WriteLine($"Setting OCR method during initialization: {method}");
            selectedOcrMethod = method;
            // Update status text
            SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_UsingBuiltInOcr"], method));
            // return;
            // }

            // Only process if actually changing the method
            if (selectedOcrMethod != method)
            {
                Console.WriteLine($"MainWindow changing OCR method from {selectedOcrMethod} to {method}");
                selectedOcrMethod = method;
                // Only built-in OCR (OneOCR, Windows OCR) is supported now.
                if (isStarted)
                {
                    OnStartButtonToggleClicked(toggleButton, new RoutedEventArgs());
                }

                SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_UsingBuiltInOcr"], method));
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
            LanguageCatalog.FillComboBox(sourceLanguageComboBox, includeAuto: true);
            LanguageCatalog.FillComboBox(targetLanguageComboBox, includeAuto: false);
            GetVersionWindows();

            // Initialize console but keep it hidden initially
            InitializeConsole();

            // Hide the console window initially
            consoleWindow = GetConsoleWindow();
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

            // Audio service hotkey
            KeyboardShortcuts.AudioServiceToggleRequested += async (s, e) =>
            {
                try
                {
                    await ToggleAudioServiceAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error toggling audio service via hotkey: {ex.Message}");
                }
            };

            // Swap languages hotkey
            KeyboardShortcuts.SwapLanguagesRequested += (s, e) =>
            {
                try
                {
                    Dispatcher.Invoke(() => SwapLanguages());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error swapping languages via hotkey: {ex.Message}");
                }
            };

            // Retry translation hotkey
            KeyboardShortcuts.RetryTranslationRequested += (s, e) =>
            {
                try
                {
                    Logic.Instance.RetryCurrentTranslation();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error retrying translation via hotkey: {ex.Message}");
                }
            };

            // Select exclude region hotkey
            KeyboardShortcuts.ToggleExcludeRegionsRequested += (s, e) =>
            {
                try
                {
                    ToggleExcludeRegionSelector();
                    Console.WriteLine("Exclude region selector toggled via hotkey");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error selecting exclude region via hotkey: {ex.Message}");
                }
            };

            // Set up global keyboard hook to handle shortcuts even when console has focus
            KeyboardShortcuts.InitializeGlobalHook();

            // Load exclude regions from config
            LoadExcludeRegions();

            // Update audio button label when localization strings change
            LocalizationManager.Instance.PropertyChanged += (s, args) =>
            {
                try
                {
                    if (args.PropertyName == "Strings")
                    {
                        UpdateAudioServiceButtonUI(ConfigManager.Instance.IsAudioServiceAutoTranslateEnabled());
                        UpdateTtsButtonUI();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating localized audio button: {ex.Message}");
                }
            };
        }

        // Load exclude regions from config
        private void LoadExcludeRegions()
        {
            excludeRegions.Clear();
            excludeRegions.AddRange(ConfigManager.Instance.GetExcludeRegions());
            _showExcludeRegions = ConfigManager.Instance.GetShowExcludeRegions();
            Console.WriteLine($"Loaded {excludeRegions.Count} exclude regions from config");
        }

        // Re-read the exclude regions after the config changed underneath (e.g. a game profile was loaded)
        public void ReloadExcludeRegionsFromConfig()
        {
            LoadExcludeRegions();
            OnExcludeRegionsChanged();
        }

        private void OnExcludeRegionsChanged()
        {
            MonitorWindow.Instance.RefreshOverlays();
            ExcludeRegionsChanged?.Invoke(this, EventArgs.Empty);
        }

        // Save exclude regions to config
        public void SaveExcludeRegions()
        {
            ConfigManager.Instance.SaveExcludeRegions(excludeRegions);
            ConfigManager.Instance.SetShowExcludeRegions(_showExcludeRegions);
        }

        // Toggle exclude region selector
        public void ToggleExcludeRegionSelector()
        {
            if (isSelectingExcludeRegion)
            {
                // Second press cancels: close the open selector
                isSelectingExcludeRegion = false;
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window is TranslationAreaSelectorWindow selector)
                    {
                        selector.WasCancelled = true;
                        selector.Close();
                        break;
                    }
                }
                return;
            }

            if (this.WindowState != WindowState.Minimized)
            {
                this.WindowState = WindowState.Minimized;
            }

            TranslationAreaSelectorWindow selectorWindow = TranslationAreaSelectorWindow.GetInstance();
            selectorWindow.ConfigureForExcludeRegion();
            selectorWindow.SelectionComplete += ExcludeAreaSelector_SelectionComplete;
            selectorWindow.Closed += (s, e) =>
            {
                isSelectingExcludeRegion = false;
            };
            selectorWindow.Show();

            isSelectingExcludeRegion = true;
        }

        // Handle exclude region selection complete
        private void ExcludeAreaSelector_SelectionComplete(object? sender, TranslationAreaInfo areaInfo)
        {
            isSelectingExcludeRegion = false;

            // Add the selected area to exclude regions (only its bounds are used)
            excludeRegions.Add(areaInfo.Bounds);

            // Keep the newest regions only, and say so instead of dropping one silently
            bool droppedOldest = excludeRegions.Count > MaxExcludeRegions;
            if (droppedOldest)
            {
                excludeRegions.RemoveRange(0, excludeRegions.Count - MaxExcludeRegions);
            }

            SaveExcludeRegions();
            OnExcludeRegionsChanged();

            if (droppedOldest)
            {
                ShowFastNotification(
                    LocalizationManager.Instance["Tab_ExcludeRegions"],
                    string.Format(LocalizationManager.Instance["Msg_ExcludeRegionLimit"], MaxExcludeRegions));
            }

            Console.WriteLine($"Exclude region added: X={areaInfo.X}, Y={areaInfo.Y}, Width={areaInfo.Width}, Height={areaInfo.Height}");
            Console.WriteLine($"Total exclude regions: {excludeRegions.Count}");
        }

        // Remove one exclude region
        public void RemoveExcludeRegion(int index)
        {
            if (index < 0 || index >= excludeRegions.Count)
                return;

            excludeRegions.RemoveAt(index);
            SaveExcludeRegions();
            OnExcludeRegionsChanged();
            Console.WriteLine($"Exclude region {index + 1} removed");
        }

        // Clear all exclude regions
        public void ClearExcludeRegions()
        {
            excludeRegions.Clear();
            SaveExcludeRegions();
            OnExcludeRegionsChanged();
            Console.WriteLine("All exclude regions cleared");
        }

        // Get/Set show exclude regions
        public bool GetShowExcludeRegions()
        {
            return _showExcludeRegions;
        }

        public void SetShowExcludeRegions(bool show=false)
        {
            _showExcludeRegions = show;
            SaveExcludeRegions();
            MonitorWindow.Instance.RefreshOverlays();
        }

        public bool GetIsCapturingWindow()
        {
            return isCapturingWindow;
        }

        // Shared tail of every capture: mask the exclude regions, refresh the monitor, then run OCR when
        // it is due. (originX, originY) is the screen position of the bitmap's top-left pixel.
        private void ProcessCapturedBitmap(Bitmap bitmap, int originX, int originY)
        {
            using Bitmap? masked = ApplyCaptureMask(bitmap, originX, originY);
            Bitmap image = masked ?? bitmap;

            if (MonitorWindow.Instance.IsVisible)
            {
                MonitorWindow.Instance.UpdateScreenshotFromBitmap();
            }

            if (!ShouldPerformOcrNow())
                return;

            // Nothing on screen changed since the last OCR and its result is fully handled: OCR would
            // read the same text again. It still re-reads now and then, in case a setting that shapes
            // the result (language, ignore phrases, ...) changed meanwhile.
            bool sameFrame = _frameChangeDetector.IsSameAsOcrFrame(image, originX, originY);
            if (sameFrame && ConfigManager.Instance.IsAutoOCREnabled() && Logic.Instance.IsOcrResultSettled
                && DateTime.UtcNow - _lastOcrStartTime < UnchangedFrameMaxSkip)
            {
                return;
            }
            _frameChangeDetector.MarkOcrFrame();
            _lastOcrStartTime = DateTime.UtcNow;

            Stopwatch stopwatch = Stopwatch.StartNew();
            SetOCRCheckIsWanted(false);
            OcrTiming.MarkCaptureSent();

            // The overlay background colour is sampled from this frame once its OCR result is in.
            // Clone() copies lazily, so this costs almost nothing unless the frame is sampled.
            Logic.Instance.SetOcrSourceFrame(ConfigManager.Instance.IsAutoSetOverlayBackground() ? (Bitmap)image.Clone() : null);

            string sourceLanguage = (sourceLanguageComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString()!;
            if (GetSelectedOcrMethod() == "Windows OCR")
            {
                Logic.Instance.ProcessWithWindowsOCR(image, sourceLanguage);
            }
            else
            {
                Logic.Instance.ProcessWithOneOCR(image, sourceLanguage);
            }

            stopwatch.Stop();
            Console.WriteLine($"OCR processing completed in {stopwatch.ElapsedMilliseconds}ms");
        }

        // Black out, on a copy of the bitmap whose top-left pixel is at (originX, originY):
        // - the exclude regions (screen pixels), clipped to the bitmap, so one that only partly overlaps
        //   the capture area is still masked;
        // - with several active translation areas, everything between them: the bitmap is the box
        //   around all of them, and only the areas themselves may be read. Masking it out also keeps
        //   animations between the areas from counting as a changed frame.
        // Returns null when nothing needs masking.
        private Bitmap? ApplyCaptureMask(Bitmap bitmap, int originX, int originY)
        {
            List<TranslationAreaInfo> activeAreas = GetActiveAreas();
            bool maskBetweenAreas = activeAreas.Count > 1;
            if (excludeRegions.Count == 0 && !maskBetweenAreas)
                return null;

            try
            {
                var bitmapBounds = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
                var masks = new List<System.Drawing.Rectangle>();

                using Region? outsideAreas = maskBetweenAreas ? new Region(bitmapBounds) : null;
                if (outsideAreas != null)
                {
                    foreach (var area in activeAreas)
                    {
                        outsideAreas.Exclude(new System.Drawing.Rectangle(
                            (int)area.X - originX, (int)area.Y - originY, (int)area.Width, (int)area.Height));
                    }
                }
                foreach (Rect region in excludeRegions)
                {
                    int left = (int)Math.Floor(region.X) - originX;
                    int top = (int)Math.Floor(region.Y) - originY;
                    int right = (int)Math.Ceiling(region.X + region.Width) - originX;
                    int bottom = (int)Math.Ceiling(region.Y + region.Height) - originY;

                    var mask = System.Drawing.Rectangle.FromLTRB(left, top, right, bottom);
                    mask.Intersect(bitmapBounds);
                    if (mask.Width > 0 && mask.Height > 0)
                    {
                        masks.Add(mask);
                    }
                }

                if (masks.Count == 0 && outsideAreas == null)
                    return null;

                Bitmap result = new Bitmap(bitmap);
                using (Graphics g = Graphics.FromImage(result))
                using (SolidBrush brush = new SolidBrush(System.Drawing.Color.Black))
                {
                    if (outsideAreas != null)
                        g.FillRegion(brush, outsideAreas);
                    if (masks.Count > 0)
                        g.FillRectangles(brush, masks.ToArray());
                }
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error applying exclude mask: {ex.Message}");
                return null;
            }
        }

        private void SelectAreaButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleTranslationAreaSelector();
        }

        private void ToggleTranslationAreaSelector()
        {
            // Select Area pressed while a startup dialog is still up: minimizing this window would hide the
            // dialog with it while it keeps this window blocked, so the app could not be brought back.
            // Close the dialog now and show it again once the window is restored.
            if (_openStartupDialog != null)
            {
                _startupDialogInterrupted = true;
                _openStartupDialog.Close();
            }

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
            selectAreaButton.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
        }

        // Toggle Audio Service (Start/Stop local Whisper) from main window button
        private async void AudioServiceButton_Click(object sender, RoutedEventArgs e)
        {
            await ToggleAudioServiceAsync();
        }

        // Shared toggle method so UI button and hotkey can call same logic
        public async System.Threading.Tasks.Task ToggleAudioServiceAsync()
        {
            try
            {
                bool enabled = !ConfigManager.Instance.IsAudioServiceAutoTranslateEnabled();
                ConfigManager.Instance.SetAudioServiceAutoTranslateEnabled(enabled);
                Console.WriteLine($"MainWindow: Audio service auto-translate set to {enabled}");

                UpdateAudioServiceButtonUI(enabled);

                if (enabled && !localWhisperService.Instance.IsRunning)
                {
                    try
                    {
                        await localWhisperService.Instance.StartServiceAsync((original, translated) =>
                        {
                            Console.WriteLine($"Whisper detected: {original}");
                        });
                        // Not running without an exception means the user turned it off while
                        // the model was loading — the off branch already updated the UI.
                        if (localWhisperService.Instance.IsRunning)
                        {
                            Console.WriteLine("Local Whisper Service started");
                            AudioStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(20, 180, 20)); // Green
                            AudioStatusText.Text = LocalizationManager.Instance.Strings["Btn_On"];
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error starting Whisper service: {ex.Message}");
                        ConfigManager.Instance.SetAudioServiceAutoTranslateEnabled(false);
                        UpdateAudioServiceButtonUI(false);
                        AudioStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                        AudioStatusText.Text = LocalizationManager.Instance.Strings["Btn_Off"];
                        System.Windows.MessageBox.Show(
                            string.Format(LocalizationManager.Instance.Strings["Msg_ErrorStartingAudioService"], ex.Message),
                            LocalizationManager.Instance.Strings["Title_Error"],
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
                else if (!enabled)
                {
                    // Stop unconditionally: the service may still be loading its model, in which
                    // case IsRunning is false but the pending start must be cancelled.
                    localWhisperService.Instance.Stop();
                    Console.WriteLine("Local Whisper Service stopped");
                    AudioStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                    AudioStatusText.Text = LocalizationManager.Instance.Strings["Btn_Off"];
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error toggling audio service: {ex.Message}");
            }
        }

        private void TtsToggleButton_Click(object sender, RoutedEventArgs e)
        {
            bool enabled = !ConfigManager.Instance.IsTtsEnabled();
            ConfigManager.Instance.SetTtsEnabled(enabled);
            // Turning TTS off also silences the line being read and everything queued behind it
            if (!enabled) StopAllSpeech();
            UpdateTtsButtonUI();
            SettingsWindow.SyncTtsEnabled();
        }

        /// <summary>Stop the speech queue and every TTS engine, dropping anything not yet spoken.</summary>
        public static void StopAllSpeech()
        {
            // Stop speech queue in ChatBoxWindow first
            ChatBoxWindow.StopSpeechQueue();

            // Stop all TTS services
            WindowsTTSService.StopAllTTS();
            GoogleTTSService.StopAllTTS();
            ElevenLabsService.StopAllTTS();
            SupertonicTTSService.StopAllTTS();
        }

        /// <summary>Show the TTS on/off state on the toolbar button (also called when Settings changes it).</summary>
        public void UpdateTtsButtonUI()
        {
            try
            {
                if (ttsToggleButton == null) return;
                Dispatcher.Invoke(() =>
                {
                    bool enabled = ConfigManager.Instance.IsTtsEnabled();
                    try
                    {
                        var baseLabel = LocalizationManager.Instance.Strings["Btn_TTS"];
                        var stateLabel = LocalizationManager.Instance.Strings[enabled ? "Btn_AudioService_On" : "Btn_AudioService_Off"];
                        ttsToggleButton.Content = $"{baseLabel}: {stateLabel}";
                    }
                    catch
                    {
                        // Fallback to simple english text if localization keys are missing
                        ttsToggleButton.Content = enabled ? "TTS: On" : "TTS: Off";
                    }

                    ttsToggleButton.Background = enabled ? new SolidColorBrush(Color.FromRgb(20, 180, 20)) : new SolidColorBrush(Color.FromRgb(95, 95, 95));
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS button UI: {ex.Message}");
            }
        }

        private void UpdateAudioServiceButtonUI(bool enabled)
        {
            try
            {
                if (audioServiceButton == null) return;
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var baseLabel = LocalizationManager.Instance.Strings["Btn_AudioService"];
                        var stateLabelKey = enabled ? "Btn_AudioService_On" : "Btn_AudioService_Off";
                        var stateLabel = LocalizationManager.Instance.Strings[stateLabelKey];
                        audioServiceButton.Content = $"{baseLabel}: {stateLabel}";
                    }
                    catch
                    {
                        // Fallback to simple english text if localization keys are missing
                        audioServiceButton.Content = enabled ? "Audio STT: On" : "Audio STT: Off";
                    }

                    audioServiceButton.Background = enabled ? new SolidColorBrush(Color.FromRgb(20, 180, 20)) : new SolidColorBrush(Color.FromRgb(95, 95, 95));
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating audio button UI: {ex.Message}");
            }
        }

        // Handle the event when translation region is selected
        private void TranslationAreaSelector_SelectionComplete(object? sender, TranslationAreaInfo areaInfo)
        {
            // Check if multiple areas are allowed
            bool allowMultipleAreas = ConfigManager.Instance.IsMultiSelectionAreaEnabled();

            List<TranslationAreaInfo> areas;
            bool droppedOldest = false;
            if (allowMultipleAreas)
            {
                // Add new selection area to the list, keeping the 5 most recent
                areas = new List<TranslationAreaInfo>(savedTranslationAreas) { areaInfo };
                if (areas.Count > MaxTranslationAreas)
                {
                    areas.RemoveRange(0, areas.Count - MaxTranslationAreas);
                    droppedOldest = true;
                    Console.WriteLine($"Maximum of {MaxTranslationAreas} areas allowed. Keeping only the {MaxTranslationAreas} most recent selections.");
                }
            }
            else
            {
                // Save lastest selection area if multiple areas are not allowed
                areas = new List<TranslationAreaInfo> { areaInfo };
            }

            SetTranslationAreas(areas);
            ShowAreaBordersAfterSelection();

            Console.WriteLine($"The translation area has been selected: X={areaInfo.X}, Y={areaInfo.Y}, Width={areaInfo.Width}, Height={areaInfo.Height}, Screen={areaInfo.ScreenIndex}, DPI={areaInfo.DpiScaleX:F2}x/{areaInfo.DpiScaleY:F2}y");
            Console.WriteLine($"Total saved areas: {savedTranslationAreas.Count}, Current index: {currentAreaIndex + 1}");

            // A notification instead of a message box: selecting areas is done over the game
            if (droppedOldest)
            {
                ShowFastNotification(
                    LocalizationManager.Instance.Strings["Title_MaximumSelectionArea"],
                    LocalizationManager.Instance.Strings["Msg_MaximumAreasAllowed"]);
            }

            selectAreaButton.Background = new SolidColorBrush(Color.FromRgb(20, 180, 20)); // Green

            CoachMarkTour.Current?.OnAreaChosen();
        }

        private const int MaxTranslationAreas = 5;

        // The "outlines hide when you press Start" hint was shown since the last Start / clear
        private bool _areaBorderHintShown;

        /// <summary>
        /// Outline the areas so the user sees the selection worked (and the area numbers). Before
        /// Start they stay until Start is pressed; while translating they would never be cleared that
        /// way, so they only flash briefly.
        /// </summary>
        private void ShowAreaBordersAfterSelection()
        {
            bool multi = ConfigManager.Instance.IsMultiSelectionAreaEnabled();
            // The newest area is the current one: last in the list with multi area on, the only one otherwise
            List<TranslationAreaInfo> areas = GetActiveAreas();
            int newest = areas.Count - 1;
            if (isStarted)
            {
                AreaBorderOverlay.Show(areas, multi, newest, TimeSpan.FromSeconds(2));
                return;
            }

            AreaBorderOverlay.Show(areas, multi, newest, autoHide: null);

            // Told once per round (until Start or clearing the areas); later selections show
            // their count on the outlines themselves
            if (!_areaBorderHintShown)
            {
                _areaBorderHintShown = true;
                string message = multi
                    ? string.Format(LocalizationManager.Instance.Strings["Msg_AreaBordersMulti"], savedTranslationAreas.Count, MaxTranslationAreas)
                    : LocalizationManager.Instance.Strings["Msg_AreaBordersSingle"];
                ShowFastNotification(LocalizationManager.Instance.Strings["Title_AreaSelected"], message);
            }
        }

        /// <summary>
        /// Replace the translation areas (new selection, profile load). The newest one becomes the
        /// current area, all areas are active again, and Select Window mode re-anchors them to where
        /// the game window is now.
        /// </summary>
        public void SetTranslationAreas(List<TranslationAreaInfo> areas)
        {
            if (areas.Count == 0)
            {
                ClearTranslationAreas();
                return;
            }

            // Outlines of the previous set would no longer match
            AreaBorderOverlay.Hide();

            savedTranslationAreas = areas;
            currentAreaIndex = areas.Count - 1;
            _areaWindowAnchor = null;
            hasSelectedTranslationArea = true;

            // Old overlays belong to the previous capture rectangle
            if (MonitorWindow.Instance.textOverlayCanvas != null)
            {
                Logic.Instance.ClearAllTextObjects();
                MonitorWindow.Instance.RefreshOverlays();
            }

            UpdateCustomCaptureRect();
            selectAreaButton.Background = new SolidColorBrush(Color.FromRgb(20, 180, 20)); // Green
        }

        /// <summary>Forget every translation area and go back to capturing the default area.</summary>
        public void ClearTranslationAreas()
        {
            AreaBorderOverlay.Hide();
            _areaBorderHintShown = false;
            savedTranslationAreas.Clear();
            hasSelectedTranslationArea = false;
            currentAreaIndex = -1;
            _areaWindowAnchor = null;
            selectAreaButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
            UpdateCaptureRect();
        }

        /// <summary>
        /// Multi area was turned on or off (toolbar or Settings): recapture with the new set of areas.
        /// Turning it off keeps the saved areas but only the newest one is used, so turning it back
        /// on brings the others back.
        /// </summary>
        public void OnMultiAreaModeChanged()
        {
            multiAreaCheckBox.IsChecked = ConfigManager.Instance.IsMultiSelectionAreaEnabled();
            if (!hasSelectedTranslationArea) return;
            AreaBorderOverlay.Hide();
            if (MonitorWindow.Instance.textOverlayCanvas != null)
            {
                Logic.Instance.ClearAllTextObjects();
                MonitorWindow.Instance.RefreshOverlays();
            }
            UpdateCustomCaptureRect();
        }

        // Multi area checkbox on the toolbar (also in Settings, kept in sync)
        private void MultiAreaCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool enabled = multiAreaCheckBox.IsChecked == true;
            ConfigManager.Instance.SetUseMultiSelectionArea(enabled);
            OnMultiAreaModeChanged();
            SettingsWindow.SyncMultiSelectionArea();
            if (enabled)
            {
                ShowFastNotification(LocalizationManager.Instance.Strings["Lbl_MultiSelection"],
                                     LocalizationManager.Instance.Strings["Tip_MultiSelection"]);
            }
        }

        /// <summary>
        /// The areas captured on each tick: every saved area with multi area on, otherwise just the
        /// current (newest) one.
        /// </summary>
        private List<TranslationAreaInfo> GetActiveAreas()
        {
            var active = new List<TranslationAreaInfo>();
            if (!hasSelectedTranslationArea || savedTranslationAreas.Count == 0)
                return active;

            if (ConfigManager.Instance.IsMultiSelectionAreaEnabled())
            {
                active.AddRange(savedTranslationAreas);
            }
            else if (currentAreaIndex >= 0 && currentAreaIndex < savedTranslationAreas.Count)
            {
                active.Add(savedTranslationAreas[currentAreaIndex]);
            }
            return active;
        }

        /// <summary>
        /// Bounding box of the active areas in physical screen pixels: the capture rectangle and the
        /// overlay window. With several areas, everything inside it but outside the areas is masked
        /// before OCR (see <see cref="ApplyCaptureMask"/>), so OCR, translation and the overlay work
        /// exactly as for one area.
        /// </summary>
        private System.Drawing.Rectangle GetActiveAreaBounds()
        {
            System.Drawing.Rectangle bounds = System.Drawing.Rectangle.Empty;
            foreach (var area in GetActiveAreas())
            {
                var rect = new System.Drawing.Rectangle((int)area.X, (int)area.Y, (int)area.Width, (int)area.Height);
                bounds = bounds.IsEmpty ? rect : System.Drawing.Rectangle.Union(bounds, rect);
            }
            return bounds;
        }

        // Set capture area to selected region
        private void UpdateCustomCaptureRect()
        {
            if (!hasSelectedTranslationArea) return;

            captureRect = GetActiveAreaBounds();

            previousCaptureX = captureRect.Left;
            previousCaptureY = captureRect.Top;

            // Update current capture position for Logic
            Logic.Instance.SetCurrentCapturePosition(captureRect.Left, captureRect.Top);
            UpdateMonitorWindowPosition();
        }

        /// <summary>
        /// Select Window mode: areas are stored in screen pixels, so when the game window moves they
        /// are moved by the same amount; otherwise they would stay behind and capture the wrong part
        /// of the window. Returns true when the areas moved.
        /// </summary>
        private bool MoveAreasWithCapturedWindow()
        {
            if (!hasSelectedTranslationArea || savedTranslationAreas.Count == 0 ||
                !isCapturingWindow || capturedWindowHandle == IntPtr.Zero ||
                !IsWindow(capturedWindowHandle) || IsIconic(capturedWindowHandle))
                return false;

            RECT windowRect;
            if (_graphicsCaptureService != null)
                GetVisibleWindowRect(capturedWindowHandle, out windowRect);
            else
                GetWindowRect(capturedWindowHandle, out windowRect);

            var position = new System.Drawing.Point(windowRect.Left, windowRect.Top);
            if (_areaWindowAnchor is not System.Drawing.Point anchor)
            {
                _areaWindowAnchor = position;
                return false;
            }
            if (anchor == position)
                return false;

            int dx = position.X - anchor.X;
            int dy = position.Y - anchor.Y;
            for (int i = 0; i < savedTranslationAreas.Count; i++)
            {
                var area = savedTranslationAreas[i];
                var bounds = area.Bounds;
                bounds.Offset(dx, dy);
                area.Bounds = bounds;
                savedTranslationAreas[i] = area;
            }
            _areaWindowAnchor = position;
            AreaBorderOverlay.MoveTo(GetActiveAreas());
            Console.WriteLine($"Game window moved by ({dx}, {dy}), translation areas moved with it");
            return true;
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
            try
            {
            // Play entrance animations
            PlayEntranceAnimations();
            
            // Update capture rectangle
            UpdateCaptureRect();
            SettingsWindow.Instance.ListHotKey_TextChanged();

            // Update app version on setup screen
            AppVersion.Text = LocalizationManager.Instance.Strings["App_Version"] + " " + SplashManager.FormatVersion(SplashManager.CurrentVersion);

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
            bool audioEnabled = false;
            ConfigManager.Instance.SetAudioServiceAutoTranslateEnabled(audioEnabled);
            UpdateAudioServiceButtonUI(audioEnabled);
            UpdateTtsButtonUI();
            multiAreaCheckBox.IsChecked = ConfigManager.Instance.IsMultiSelectionAreaEnabled();
            UpdateThemeToggleButtonUI();

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

            // status OCR
            if(savedOcrMethod == "Windows OCR" || savedOcrMethod == "OneOCR")
            {
                OCRStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(20, 180, 20)); // Green
                OCRStatusText.Text = LocalizationManager.Instance.Strings["Btn_On"];
            }
            else
            {
                OCRStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                OCRStatusText.Text = LocalizationManager.Instance.Strings["Btn_Off"];
            }

            // Load auto-translate setting from config
            isAutoTranslateEnabled = ConfigManager.Instance.IsAutoTranslateEnabled();

            // Initialize clipboard monitoring
            InitializeClipboardMonitor();

            // Register the LocationChanged event to update the position of the MonitorWindow when the MainWindow moves
            this.LocationChanged += MainWindow_LocationChanged;
            this.StateChanged += MainWindow_StateChanged;
            // ToggleMonitorWindow();
            CheckAndShowWelcome();

            // Load screen selection list
            LoadScreenList();

            // Warm up Supertonic TTS in the background so the first SpeakText
            // call returns instantly. Only kicks in when the user has actually
            // selected Supertonic as the TTS service and the model is on disk.
            // Wrapped in an isolated method to ensure that any failure here
            // (e.g. assembly load issue in the release build) cannot prevent
            // the rest of MainWindow_Loaded from running and the window from
            // appearing.
            try { TryWarmUpSupertonic(); }
            catch (Exception ex)
            {
                Console.WriteLine($"Supertonic warm-up trigger failed: {ex.Message}");
            }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during MainWindow_Loaded: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                _isInitializing = false;
            }
        }

        // Isolated so any failure (assembly load, ONNX init, etc.) cannot
        // prevent MainWindow_Loaded from completing and the window from
        // showing. All exceptions are swallowed and logged.
        private void TryWarmUpSupertonic()
        {
            try
            {
                if (!ConfigManager.Instance.IsTtsEnabled()) return;
                if (ConfigManager.Instance.GetTtsService() != "Supertonic") return;
                if (!SupertonicTTSService.IsModelInstalled()) return;

                var warmup = SupertonicTTSService.Instance.WarmUpAsync();
                if (warmup != null)
                {
                    warmup.ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                        {
                            Console.WriteLine($"Supertonic warm-up unobserved: {t.Exception.GetBaseException().Message}");
                        }
                    }, TaskScheduler.Default);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Supertonic warm-up outer error: {ex.Message}");
            }
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
            // Use SetWindowPos with physical pixel coordinates to bypass WPF's coordinate system.
            // WPF Left/Top division by DPI fails on non-primary monitors because the divided value
            // can land on the wrong screen (e.g., physical X=2000 / 1.5 = 1333, which is on screen 0 not screen 1).
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(MonitorWindow.Instance).Handle;
            if (hwnd == IntPtr.Zero)
            {
                Console.WriteLine("UpdateMonitorWindowPosition: MonitorWindow handle not available yet");
                return;
            }
            
            int physX = captureRect.Left;
            int physY = captureRect.Top;
            int physW = Math.Max(1, captureRect.Width);
            int physH = Math.Max(1, captureRect.Height);
            
            DpiHelper.PositionWindowPhysical(hwnd, physX, physY, physW, physH);

            // Save physical position for future reference
            monitorWindowLeft = physX;
            monitorWindowTop = physY;

            // Console.WriteLine($"MonitorWindow positioned at physical: X={physX}, Y={physY}, W={physW}, H={physH}");
        }

        private void UpdateCaptureRect()
        {
            // Areas that moved with the game window carry their text with them: like the overlay that
            // follows a captured window, their text must not be shifted back to the old screen spot
            bool areasFollowedWindow = false;

            // If a custom translation area has been selected, use that area (or the box around all
            // active areas)
            if (hasSelectedTranslationArea)
            {
                areasFollowedWindow = MoveAreasWithCapturedWindow();

                previousCaptureX = captureRect.Left;
                previousCaptureY = captureRect.Top;

                captureRect = GetActiveAreaBounds();
            }
            else if (isCapturingWindow && capturedWindowHandle != IntPtr.Zero)
            {
                // In Select Window mode, position MonitorWindow over the captured game window.
                // Use DwmGetWindowAttribute to get actual visible bounds (excludes invisible DWM shadow)
                // so that the overlay matches the WGC capture area exactly.
                if (IsWindow(capturedWindowHandle))
                {
                    RECT windowRect;
                    if (_graphicsCaptureService != null)
                        GetVisibleWindowRect(capturedWindowHandle, out windowRect);
                    else
                        GetWindowRect(capturedWindowHandle, out windowRect);

                    previousCaptureX = captureRect.Left;
                    previousCaptureY = captureRect.Top;

                    captureRect = new System.Drawing.Rectangle(
                        windowRect.Left,
                        windowRect.Top,
                        windowRect.Right - windowRect.Left,
                        windowRect.Bottom - windowRect.Top);
                }
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

            // If position changed and we have text objects, update their positions.
            // Not in Select Window mode: the overlay follows the game window and text is relative to it,
            // so shifting it would leave the text behind (e.g. after Alt+Enter or dragging the game).
            bool isFollowingGameWindow = areasFollowedWindow ||
                (!hasSelectedTranslationArea && isCapturingWindow && capturedWindowHandle != IntPtr.Zero);
            if (!isFollowingGameWindow &&
                (previousCaptureX != captureRect.Left || previousCaptureY != captureRect.Top) &&
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

        private async void OnStartButtonToggleClicked(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button btn = (System.Windows.Controls.Button)sender;
            String method = ConfigManager.Instance.GetOcrMethod();
            // Only built-in OCR (OneOCR, Windows OCR) is supported now - always ready
            bool isReady = true;

            if (isStarted)
            {
                Logic.Instance.ResetHash();
                isStarted = false;
                btn.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("Strings[Btn_Start]") { Source = LocalizationManager.Instance });
                btn.Background = new SolidColorBrush(Color.FromRgb(20, 180, 20)); // Green                                                   
                Logic.Instance.ClearAllTextObjects();
                MonitorWindow.Instance.RefreshOverlays();
                MonitorWindow.Instance.HideTranslationStatus();
                if (ChatBoxWindow.Instance != null && ConfigManager.Instance.IsAutoClearChatboxHistoryEnabled())
                {
                    ChatBoxWindow.Instance.chatHistoryText.Document.Blocks.Clear();
                }
                if (ConfigManager.Instance.IsTtsEnabled())
                {
                    StopAllSpeech();
                }
                ShowFastNotification(LocalizationManager.Instance.Strings["NotificationTitle_TranslationStopped"], LocalizationManager.Instance.Strings["NotificationMessage_TranslationStopped_Details"]);
                StartStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                StartStatusText.Text = LocalizationManager.Instance.Strings["Btn_Off"];
            }
            else
            {
                if (isReady)
                {
                    isStarted = true;
                    isStopOCR = false;
                    // Area outlines are only for checking the selection; translating hides them
                    AreaBorderOverlay.Hide();
                    _areaBorderHintShown = false;
                    // Prepare the translation service so the first OCR line is not slower
                    TranslationWarmUp.Start();
                    btn.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("Strings[Btn_Stop]") { Source = LocalizationManager.Instance });
                    UpdateCaptureRect();
                    SetOCRCheckIsWanted(true);
                    btn.Background = new SolidColorBrush(Color.FromRgb(220, 0, 0)); // Red
                    ShowFastNotification(LocalizationManager.Instance.Strings["NotificationTitle_TranslationStarted"], LocalizationManager.Instance.Strings["NotificationMessage_TranslationStarted_Details"]);
                    StartStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(20, 180, 20)); // Green
                    StartStatusText.Text = LocalizationManager.Instance.Strings["Btn_On"];

                    CoachMarkTour.Current?.OnTranslationStarted();
                    OnboardingTips.OnTranslationStarted(hasSelectedTranslationArea);
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
            localWhisperService.Instance.StopForExit();
            this.Close();
        }

        // Override OnClosing to ensure the application shuts down completely
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Dispose WGC capture service
            _graphicsCaptureService?.Dispose();
            _graphicsCaptureService = null;

            // Stop local whisper service
            localWhisperService.Instance.StopForExit();

            // Clean up Logic resources
            Logic.Instance.Finish();

            // Dispose TaskbarIcon to remove tray icon and close any balloons
            if (MyNotifyIcon != null)
            {
                MyNotifyIcon.CloseBalloon();
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

        // Must be called on the UI thread
        public void ShowFastNotification(string title, string message, int durationMs = 4000)
        {
            MyNotifyIcon.CloseBalloon();

            // The balloon closes itself after durationMs (paused while hovered), so no timeout is passed here
            FancyBalloon balloon = new FancyBalloon(title, message, MyNotifyIcon, durationMs);
            MyNotifyIcon.ShowCustomBalloon(balloon, System.Windows.Controls.Primitives.PopupAnimation.Slide, null);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Remove global keyboard hook
            KeyboardShortcuts.CleanupGlobalHook();

            // Clean up MouseManager resources
            MouseManager.Instance.Cleanup();

            // Clean up clipboard monitor
            ClipboardMonitor.Instance.StopListening();

            Logic.Instance.Finish();

            // Make sure the console is closed
            if (consoleWindow != IntPtr.Zero)
            {
                ShowWindow(consoleWindow, SW_HIDE);
            }
            localWhisperService.Instance.StopForExit();

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

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        /// <summary>
        /// Get the actual visible frame bounds of a window, excluding the invisible DWM shadow border.
        /// Falls back to GetWindowRect if DwmGetWindowAttribute fails.
        /// </summary>
        private static void GetVisibleWindowRect(IntPtr hwnd, out RECT rect)
        {
            int hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>());
            if (hr != 0)
                GetWindowRect(hwnd, out rect);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);


        private Rectangle _lastLoggedWindowRegion;

        private Bitmap CaptureWindow(IntPtr handle)
        {
            try
            {
                RECT rect;
                if (_graphicsCaptureService != null)
                    GetVisibleWindowRect(handle, out rect);
                else
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

                // The box around the active areas (just the area itself when there is one)
                System.Drawing.Rectangle areaBounds = GetActiveAreaBounds();
                bool cropToAreas = !areaBounds.IsEmpty;
                if (cropToAreas)
                {
                    // Areas are stored in physical screen pixels, the same space as the window rect,
                    // so no DPI adjustment is needed here (the DPI saved with the area is informational).
                    captureX = areaBounds.X - rect.Left;
                    captureY = areaBounds.Y - rect.Top;
                    captureWidth = areaBounds.Width;
                    captureHeight = areaBounds.Height;


                    if (captureX < 0) captureX = 0;
                    if (captureY < 0) captureY = 0;
                    if (captureX + captureWidth > windowWidth) captureWidth = windowWidth - captureX;
                    if (captureY + captureHeight > windowHeight) captureHeight = windowHeight - captureY;

                    // Logged once per change: this runs every capture tick
                    var region = new Rectangle(captureX, captureY, captureWidth, captureHeight);
                    if (region != _lastLoggedWindowRegion)
                    {
                        _lastLoggedWindowRegion = region;
                        Console.WriteLine($"Capturing region in window: X={captureX}, Y={captureY}, Width={captureWidth}, Height={captureHeight}");
                    }

                    Logic.Instance.SetCurrentCapturePosition(rect.Left + captureX, rect.Top + captureY);
                }
                else
                {
                    // Console.WriteLine($"Capturing entire window: Width={windowWidth}, Height={windowHeight}");

                    Logic.Instance.SetCurrentCapturePosition(rect.Left, rect.Top);
                }


                // Try Windows Graphics Capture first (GPU-friendly, no flickering).
                Bitmap? fullWindowBmp = null;
                if (_graphicsCaptureService != null)
                {
                    fullWindowBmp = _graphicsCaptureService.CaptureFrame();
                    // WGC active but no frame yet (e.g. first tick) — skip this capture tick
                    // instead of falling back to CopyFromScreen which captures overlapping windows.
                    if (fullWindowBmp == null)
                        return new Bitmap(1, 1);
                }
                else
                {
                    // WGC not available — use screen capture as fallback.
                    fullWindowBmp = FallbackCaptureWindow(handle);
                }

                if (cropToAreas)
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
                // With WGC the overlay is not excluded from screen copies, so skip this tick instead
                if (_graphicsCaptureService != null)
                    return new Bitmap(1, 1);
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
                        if (_graphicsCaptureService != null)
                            GetVisibleWindowRect(capturedWindowHandle, out windowRect);
                        else
                            GetWindowRect(capturedWindowHandle, out windowRect);

                        // With areas, CaptureWindow records the position of the cropped region itself
                        if (!hasSelectedTranslationArea)
                        {
                            Logic.Instance.SetCurrentCapturePosition(windowRect.Left, windowRect.Top);
                        }


                        using (Bitmap bitmap = CaptureWindow(capturedWindowHandle))
                        {
                            // A 1x1 bitmap means no frame this tick (WGC not ready yet, or capture failed)
                            if (bitmap.Width > 1 && bitmap.Height > 1)
                            {
                                // CaptureWindow records the screen position of the bitmap's top-left pixel,
                                // which differs from captureRect when the translation area is clipped to the window
                                var (originX, originY) = Logic.Instance.CurrentCapturePosition;
                                ProcessCapturedBitmap(bitmap, originX, originY);
                            }
                        }

                        return;
                    }
                    else
                    {
                        StopWindowCapture(windowLost: true);
                        Console.WriteLine("Captured window no longer exists, reverting to normal capture mode");
                        // Capture the screen area from the next tick, once the overlay is hidden (Windows 10)
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error capturing window: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");

                    StopWindowCapture(windowLost: false);
                    return;
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

                    ProcessCapturedBitmap(bitmap, captureRect.Left, captureRect.Top);
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
                SettingsWindow.SyncLanguageSelection();
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
                SettingsWindow.SyncLanguageSelection();
            }

            // Reset the OCR hash to force a fresh comparison after changing target language
            Logic.Instance.ClearAllTextObjects();
        }

        private void SwapLanguagesButton_Click(object sender, RoutedEventArgs e)
        {
            SwapLanguages();
        }

        // Find the item for a language code; codes saved by older versions (ch_sim, bg-BG, he, ...) match their current code
        public static ComboBoxItem? FindLanguageItem(System.Windows.Controls.ComboBox comboBox, string? code)
        {
            if (string.IsNullOrEmpty(code))
                return null;
            code = LanguageCatalog.Normalize(code, code, allowAuto: true);

            return comboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Content?.ToString(), code, StringComparison.OrdinalIgnoreCase));
        }

        // Swap source and target languages
        public void SwapLanguages()
        {
            // Get current languages from config (always reliable)
            string currentSource = ConfigManager.Instance.GetSourceLanguage();
            string currentTarget = ConfigManager.Instance.GetTargetLanguage();

            // Auto-detect can't become the target language
            if (LanguageCatalog.IsAuto(currentSource))
            {
                Console.WriteLine("Languages not swapped: the source language is auto-detect");
                return;
            }

            // Swap in config
            ConfigManager.Instance.SetSourceLanguage(currentTarget);
            ConfigManager.Instance.SetTargetLanguage(currentSource);

            // Update MainWindow ComboBoxes if they exist
            if (sourceLanguageComboBox != null && targetLanguageComboBox != null)
            {
                sourceLanguageComboBox.SelectedItem = FindLanguageItem(sourceLanguageComboBox, currentTarget) ?? sourceLanguageComboBox.SelectedItem;
                targetLanguageComboBox.SelectedItem = FindLanguageItem(targetLanguageComboBox, currentSource) ?? targetLanguageComboBox.SelectedItem;
            }

            // Update SettingsWindow ComboBoxes if visible
            if (SettingsWindow.Instance.IsVisible)
            {
                SettingsWindow.Instance.ReloadSetting();
            }

            // Clear text objects to force re-translation with new language pair
            Logic.Instance.ClearAllTextObjects();
            Logic.Instance.ResetHash();

            // Show notification with new language pair
            string title = LocalizationManager.Instance["Title_LanguageSwapped"] ?? "Language Swapped";
            string sourceLabel = LocalizationManager.Instance["Lbl_SourceLanguage"] ?? "Source";
            string targetLabel = LocalizationManager.Instance["Lbl_TargetLanguage"] ?? "Target";
            string message = $"{sourceLabel}: {LanguageDisplay.GetShortName(currentTarget)}\n{targetLabel}: {LanguageDisplay.GetShortName(currentSource)}";
            ShowFastNotification(title, message);

            Console.WriteLine($"Languages swapped via hotkey: {currentSource} ⇄ {currentTarget}");
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

                    SetStatus(string.Format(LocalizationManager.Instance.Strings["Status_UsingBuiltInOcr"], ocrMethod));
                    OCRStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(69, 176, 105)); // Green
                    OCRStatusText.Text = LocalizationManager.Instance.Strings["Btn_On"];
                }
            }
        }

        // Keep track of selected OCR method (default: OneOCR)
        private string selectedOcrMethod = "OneOCR";

        public string GetSelectedOcrMethod()
        {
            return selectedOcrMethod;
        }

        // Toggle the monitor window
        private void MonitorButton_Click(object sender, RoutedEventArgs e)
        {
            // Hiding is always allowed; showing needs a game window on Windows 10 (see HideOverlayIfItWouldBeCaptured)
            if (!IsOverlayKeptOutOfCapture && !IsOverlayShown)
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
                if (IsTranslationResultVisible)
                {
                    CoachMarkTour.Current?.OnResultViewOpened();
                }
            }
        }

        /// <summary>
        /// True when translations are visible to the user, either as the overlay or in the ChatBox.
        /// </summary>
        public bool IsTranslationResultVisible =>
            (MonitorWindow.Instance.imageScrollViewer.Visibility == Visibility.Visible && MonitorWindow.Instance.IsVisible)
            || isChatBoxVisible;

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
                // Quick Edit only changes together with ENABLE_EXTENDED_FLAGS: a plain 0 left it on, and a
                // click in the console then starts a selection that blocks every Console.WriteLine
                uint newMode = ENABLE_EXTENDED_FLAGS;

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

        private void HideOverlay(bool notify)
        {
            MonitorWindow.Instance.imageScrollViewer.Visibility = Visibility.Collapsed;
            Console.WriteLine("Monitor window hidden from MainWindow toggle");
            monitorButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
            if (notify)
            {
                ShowFastNotification(LocalizationManager.Instance.Strings["NotificationTitle_OverlayHidden"], LocalizationManager.Instance.Strings["NotificationMessage_OverlayHidden_Details"]);
            }
            OverlayStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
            OverlayStatusText.Text = LocalizationManager.Instance.Strings["Btn_Off"];
        }

        private void ToggleMonitorWindow()
        {
            if (IsOverlayShown)
            {
                HideOverlay(notify: true);
            }
            // else 
            // {
            //     MonitorWindow.Instance.Show();
            //     Console.WriteLine("Monitor window shown from MainWindow toggle");
            //     monitorButton.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
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

                    monitorButton.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                    Console.WriteLine("MonitorWindow setup complete");
                    ShowFastNotification(LocalizationManager.Instance.Strings["NotificationTitle_OverlayVisible"], LocalizationManager.Instance.Strings["NotificationMessage_OverlayVisible_Details"]);
                    OverlayStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(69, 176, 105)); // Green
                    OverlayStatusText.Text = LocalizationManager.Instance.Strings["Btn_On"];
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

        // Load available screens into the screen selection ComboBox
        private void LoadScreenList()
        {
            try
            {
                // Clear existing items
                screenSelectionComboBox.Items.Clear();

                // Get all screens
                var screens = System.Windows.Forms.Screen.AllScreens;

                // Add each screen to the combo box
                for (int i = 0; i < screens.Length; i++)
                {
                    var screen = screens[i];

                    // Get native resolution
                    int width = screen.Bounds.Width;
                    int height = screen.Bounds.Height;

                    // Create simple display info (Screen N - Resolution)
                    string screenLabel = LocalizationManager.Instance.Strings["Grp_Screen"];
                    string screenInfo = $"{screenLabel} {i + 1} ({width}x{height})";
                    if (screen.Primary)
                    {
                        string primaryLabel = LocalizationManager.Instance.Strings["Lbl_Primary"];
                        screenInfo += $" - {primaryLabel}";
                    }

                    // Create combo box item
                    ComboBoxItem item = new ComboBoxItem
                    {
                        Content = screenInfo,
                        Tag = i  // Store screen index as Tag
                    };

                    screenSelectionComboBox.Items.Add(item);
                    Console.WriteLine($"Added screen {i}: {screenInfo}");
                }

                // Select the saved screen from config
                int selectedScreenIndex = ConfigManager.Instance.GetSelectedScreenIndex();
                if (selectedScreenIndex >= 0 && selectedScreenIndex < screenSelectionComboBox.Items.Count)
                {
                    screenSelectionComboBox.SelectedIndex = selectedScreenIndex;
                }
                else if (screenSelectionComboBox.Items.Count > 0)
                {
                    // Default to first screen if saved index is invalid
                    screenSelectionComboBox.SelectedIndex = 0;
                }

                Console.WriteLine($"Screen list loaded. Selected index: {screenSelectionComboBox.SelectedIndex}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading screen list: {ex.Message}");
            }
        }

        // Screen selection ComboBox changed handler
        private void ScreenSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;

                if (screenSelectionComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    // Get the screen index from the Tag
                    if (selectedItem.Tag is int screenIndex)
                    {
                        // Save to config
                        ConfigManager.Instance.SetSelectedScreenIndex(screenIndex);
                        Console.WriteLine($"Screen selection changed to index: {screenIndex}");

                        // Invalidate DPI cache since we changed screens
                        DpiHelper.InvalidateCache();

                        // Update capture rectangle for the new screen
                        UpdateCaptureRect();

                        // Clear translation areas since they may be on a different screen
                        if (savedTranslationAreas.Count > 0)
                        {
                            // Optionally clear areas or show warning
                            Console.WriteLine("Warning: Translation areas were selected on a different screen. Consider re-selecting areas.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling screen selection change: {ex.Message}");
            }
        }

        // ChatBox Button click handler
        private void ChatBoxButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleChatBox();
            // The first time, the ChatBox opens a selector to place it; that also counts as turning it on
            if (isChatBoxVisible || isSelectingChatBoxArea)
            {
                CoachMarkTour.Current?.OnResultViewOpened();
            }
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
                // If recreate-on-show is enabled, fully close the chat box so next show recreates it.
                if (recreateOnShow)
                {
                    try
                    {
                        chatBoxWindow.ForceClose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error force-closing ChatBox: {ex.Message}");
                    }
                    chatBoxWindow = null;
                    // _chatBoxEventsAttached = false;
                }
                else
                {
                    // Hide ChatBox (reuse later)
                    chatBoxWindow.Hide();
                }

                isChatBoxVisible = false;
                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 176, 105)); // Blue
            }
            else if (!isChatBoxVisible && !recreateOnShow)
            {
                // Check if we have a saved position (physical rect, or WPF units from older configs)
                bool hasSavedPosition = ConfigManager.Instance.TryGetChatboxPhysicalRect(out _, out _, out _, out _)
                    || ConfigManager.Instance.GetChatboxPositionLeft() > 0
                    || ConfigManager.Instance.GetChatboxPositionTop() > 0;

                // If we have a saved position, show or create chatbox at saved position
                if (hasSavedPosition)
                {
                    // If chatBoxWindow is null, create a new instance
                    if (chatBoxWindow == null)
                    {
                        // Create new ChatBox instance; ChatBoxWindow_Loaded restores the saved position
                        chatBoxWindow = new ChatBoxWindow();

                        // Attach event handlers
                        chatBoxWindow.Closed += ChatBox_Closed_Handler;
                        chatBoxWindow.IsVisibleChanged += ChatBox_IsVisibleChanged_Handler;
                    }
                    
                    // Show the chatbox
                    chatBoxWindow.Show();
                    isChatBoxVisible = true;
                    chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                }
                else
                {
                    // No saved position, show selector for first time
                    ShowChatBoxSelector();
                }
            }
            else if (!isChatBoxVisible && recreateOnShow)
            {
                // When recreate-on-show is enabled we should always ask the user to pick a region
                // so the ChatBox appears at a new location rather than reusing the old one.
                ShowChatBoxSelector();
            }
        }
        
        // Helper method to show chatbox selector
        private void ShowChatBoxSelector()
        {
            try
            {
                // If an existing chatBoxWindow exists, fully close it first to ensure Instance will recreate
                if (chatBoxWindow != null)
                {
                    try { chatBoxWindow.ForceClose(); } catch { }
                    chatBoxWindow = null;
                    // _chatBoxEventsAttached = false;
                }

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
                selectorWindow.Activate(); // Bring to front and ensure focus

                // Set button to red while selector is active
                isSelectingChatBoxArea = true;
                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating ChatBox selector: {ex.Message}");
            }
        }

        // Named handlers for ChatBox events so we can subscribe/unsubscribe deterministically
        private void ChatBox_Closed_Handler(object? sender, EventArgs e)
        {
            try
            {
                isChatBoxVisible = false;
                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
            }
            finally
            {
                // _chatBoxEventsAttached = false;
                try { chatBoxWindow = null; } catch { }
            }
        }

        private void ChatBox_IsVisibleChanged_Handler(object? sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                if (e.NewValue is bool visible && !visible)
                {
                    // If recreate-on-show is enabled, treat hiding as a close so next show triggers selector/new instance
                    if (ConfigManager.Instance.IsChatboxRecreateOnShowEnabled())
                    {
                        try
                        {
                            // Force close to ensure instance is disposed
                            chatBoxWindow?.ForceClose();
                        }
                        catch { }
                        try { chatBoxWindow = null; } catch { }
                        // _chatBoxEventsAttached = false;
                    }
                    else
                    {
                        isChatBoxVisible = false;
                        chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
                    }
                }
            }
            catch { }
        }

        // Handle selection completion
        private void ChatBoxSelector_SelectionComplete(object? sender, Rect selectionRect)
        {
            // Ensure a ChatBoxWindow instance exists; when recreateOnShow is enabled
            // we may not have created one yet, so create a fresh instance here.
            if (ChatBoxWindow.Instance == null)
            {
                try
                {
                    var tmp = new ChatBoxWindow();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating ChatBox instance: {ex.Message}");
                }
            }
            chatBoxWindow = ChatBoxWindow.Instance;
            // Ensure deterministic (re)attachment of named handlers
            if (chatBoxWindow != null)
            {
                try { chatBoxWindow.Closed -= ChatBox_Closed_Handler; } catch { }
                try { chatBoxWindow.IsVisibleChanged -= ChatBox_IsVisibleChanged_Handler; } catch { }

                chatBoxWindow.Closed += ChatBox_Closed_Handler;
                chatBoxWindow.IsVisibleChanged += ChatBox_IsVisibleChanged_Handler;
                // _chatBoxEventsAttached = true;
            }

            // Position and size the ChatBox
            // selectionRect is in physical screen pixels. Start from an approximate DIP position
            // (using the target monitor's DPI) and then place the window exactly with SetWindowPos,
            // because WPF's Left/Top mapping is unreliable on secondary monitors with another DPI.
            var targetScreen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(
                (int)(selectionRect.Left + selectionRect.Width / 2), (int)(selectionRect.Top + selectionRect.Height / 2)));
            DpiHelper.GetDpiForScreen(targetScreen, out double chatDpiX, out double chatDpiY);
            chatBoxWindow!.Left = selectionRect.Left / chatDpiX;
            chatBoxWindow.Top = selectionRect.Top / chatDpiY;
            chatBoxWindow.Width = selectionRect.Width / chatDpiX;
            chatBoxWindow.Height = selectionRect.Height / chatDpiY;

            var physicalRect = selectionRect;
            var placedWindow = chatBoxWindow;
            void PlaceChatBoxPhysical()
            {
                IntPtr chatHwnd = new System.Windows.Interop.WindowInteropHelper(placedWindow).Handle;
                DpiHelper.PositionWindowPhysical(chatHwnd,
                    (int)Math.Round(physicalRect.Left), (int)Math.Round(physicalRect.Top),
                    (int)Math.Round(physicalRect.Width), (int)Math.Round(physicalRect.Height));
                // Save the placed rect (physical pixels) so the ChatBox reopens exactly here
                placedWindow.SavePositionToConfig();
            }

            if (chatBoxWindow.IsLoaded)
            {
                chatBoxWindow.Show();
                PlaceChatBoxPhysical();
            }
            else
            {
                // ChatBoxWindow_Loaded may apply the saved position; run after it
                RoutedEventHandler? onLoaded = null;
                onLoaded = (s, e) =>
                {
                    placedWindow.Loaded -= onLoaded;
                    PlaceChatBoxPhysical();
                };
                chatBoxWindow.Loaded += onLoaded;
                chatBoxWindow.Show();
            }
            isChatBoxVisible = true;
            chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red when active

            // The ChatBox will get its data from MainWindow.GetTranslationHistory()
            // No need to manually load entries, just trigger an update
            if (chatBoxWindow != null)
            {
                Console.WriteLine($"Updating ChatBox with {_translationHistory.Count} translation entries");
                chatBoxWindow.UpdateChatHistory();
            }
        }

        // Work-area height (DIPs) of the monitor this window is on. SystemParameters.WorkArea is
        // always the primary monitor, which is wrong when the window sits on another screen.
        private double GetCurrentWorkAreaHeight()
        {
            try
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
                    double scaleY = VisualTreeHelper.GetDpi(this).DpiScaleY;
                    return screen.WorkingArea.Height / scaleY;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting work area: {ex.Message}");
            }
            return SystemParameters.WorkArea.Height;
        }

        public void AddTranslationToHistory(string originalText, string translatedText, bool fromClipboard = false)
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

            if (!fromClipboard && TryCompleteTypedOutEntry(originalText, translatedText))
                return;

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
            ChatBoxWindow.Instance!.OnTranslationWasAdded(originalText, translatedText, fromClipboard);
        }

        // Typewriter dialogue can be translated while it is still typing out, then again once complete.
        // The complete line replaces the half-typed entry instead of adding a second one, and TTS reads
        // only what the half-typed translation did not already say.
        private const int TypedOutEntryMaxAgeSeconds = 20;
        private const int TypedOutEntriesToCheck = 3;

        private bool TryCompleteTypedOutEntry(string originalText, string translatedText)
        {
            // Several text blocks (a name box, the dialogue) are added per translation, so look a few back
            var partial = _translationHistory.Reverse()
                .Take(TypedOutEntriesToCheck)
                .FirstOrDefault(e => (DateTime.Now - e.Timestamp).TotalSeconds <= TypedOutEntryMaxAgeSeconds &&
                                     TextGrowth.IsGrowth(e.OriginalText, originalText));
            if (partial == null)
                return false;

            string previousTranslation = partial.TranslatedText;
            partial.OriginalText = originalText;
            partial.TranslatedText = translatedText;
            partial.Timestamp = DateTime.Now;
            Console.WriteLine("Typed-out line completed: replacing its half-typed history entry");

            ChatBoxWindow.Instance!.OnTranslationWasAdded(originalText, translatedText, false,
                textToSpeak: GetUnspokenRemainder(previousTranslation, translatedText));
            return true;
        }

        // The part of the complete translation after the half-typed one, when it extends it; otherwise
        // null (read the whole line), since the half-typed one may have been worded differently
        private static string? GetUnspokenRemainder(string spoken, string complete)
        {
            char[] trim = { ' ', '.', ',', '…', '、', '。', '-', '—' };
            string spokenCore = spoken.Trim().TrimEnd(trim);
            string completeText = complete.TrimStart();
            if (spokenCore.Length > 0 && completeText.StartsWith(spokenCore, StringComparison.OrdinalIgnoreCase))
                return completeText.Substring(spokenCore.Length).TrimStart(trim);
            return null;
        }

        // Handle translation events from Logic
        private void Logic_TranslationCompleted(object? sender, TranslationEventArgs e)
        {
            AddTranslationToHistory(e.OriginalText, e.TranslatedText);
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
                // Remove the current area; the newest remaining one becomes current. With none left,
                // capture goes back to the default area instead of a stale rectangle.
                var remaining = new List<TranslationAreaInfo>(savedTranslationAreas);
                remaining.RemoveAt(Math.Clamp(currentAreaIndex, 0, remaining.Count - 1));
                SetTranslationAreas(remaining);
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
            // Clear all save selection areas and go back to the default capture area
            ClearTranslationAreas();

            Console.WriteLine("All translation areas have been cleared.");

            // Show notification
            System.Windows.MessageBox.Show("All translation areas have been cleared.",
                            "Areas Cleared",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
        }

        // ========== ENTRANCE ANIMATIONS ==========
        private void PlayEntranceAnimations()
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            // 1. Fade-in the entire window
            var windowFadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = ease
            };
            this.BeginAnimation(Window.OpacityProperty, windowFadeIn);

            // 2. Slide down + fade in the MainBorder (main content area)
            if (MainBorder != null)
            {
                MainBorder.RenderTransform = new TranslateTransform(0, -20);
                MainBorder.Opacity = 0;

                var slideDown = new DoubleAnimation
                {
                    From = -20,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(400),
                    EasingFunction = ease,
                    BeginTime = TimeSpan.FromMilliseconds(100)
                };
                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(400),
                    EasingFunction = ease,
                    BeginTime = TimeSpan.FromMilliseconds(100)
                };

                MainBorder.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideDown);
                MainBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }

            // 3. Staggered animation for toolbar buttons (tool groups)
            AnimateToolbarGroups();
        }

        private void AnimateToolbarGroups()
        {
            // Find toolbar WrapPanel
            var toolbar = FindVisualChild<WrapPanel>(this);
            if (toolbar == null) return;

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            int index = 0;

            foreach (var child in toolbar.Children)
            {
                if (child is Border border && border.Style == (Style)FindResource("ToolGroupStyle"))
                {
                    // Set initial state
                    border.Opacity = 0;
                    border.RenderTransform = new TranslateTransform(0, 15);
                    border.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);

                    // Staggered delay for each group
                    int delay = 200 + (index * 60);

                    var fadeIn = new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(350),
                        EasingFunction = ease,
                        BeginTime = TimeSpan.FromMilliseconds(delay)
                    };

                    var slideUp = new DoubleAnimation
                    {
                        From = 15,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(350),
                        EasingFunction = ease,
                        BeginTime = TimeSpan.FromMilliseconds(delay)
                    };

                    border.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                    border.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);

                    index++;
                }
            }
        }

        // Helper to find visual child of specific type
        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        // Toggle compact/hotkeys UI (MainWindow) ---------------------------------
        private void SetCompactView(bool isCompact)
        {
            _isCompactView = isCompact;
            // Always marshal to UI thread for layout/size changes
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetCompactView(isCompact));
                return;
            }

            // If window is not in normal state (maximized/minimized), just toggle visibility and skip resizing
            if (this.WindowState != WindowState.Normal)
            {
                CollapsibleMainContent.Visibility = _isCompactView ? Visibility.Collapsed : Visibility.Visible;
                if (compactToggleIcon != null) compactToggleIcon.Text = _isCompactView ? "▸" : "▾";
                if (compactToggleButton != null) compactToggleButton.ToolTip = LocalizationManager.Instance.Strings[_isCompactView ? "Btn_Compact_Show" : "Btn_Compact_Hide"];
                return;
            }

            // COLLAPSE: save current height then remove the collapsible content and shrink window height
            if (_isCompactView)
            {
                // ensure layout is up-to-date so ActualHeight is meaningful
                this.UpdateLayout();

                // Save current window height so we can restore on expand
                if (!double.IsNaN(this.Height) && this.Height > 0)
                {
                    _savedWindowHeightForCompact = this.Height;
                }

                double contentHeight = CollapsibleMainContent.ActualHeight;
                // If ActualHeight is zero (rare), try DesiredSize after Measure
                if (contentHeight <= 0)
                {
                    CollapsibleMainContent.Measure(new System.Windows.Size(this.ActualWidth, double.PositiveInfinity));
                    contentHeight = CollapsibleMainContent.DesiredSize.Height;
                }

                // Hide the collapsible content and shrink the window (respect MinHeight and screen work area)
                CollapsibleMainContent.Visibility = Visibility.Collapsed;

                // Ensure InfoSectionGrid remains visible after shrinking. Measure on the dispatcher once layout settles.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        this.UpdateLayout();

                        // Height of the collapsible content (we already measured above)
                        double collapseAmount = contentHeight;

                        // Conservative target: reduce by collapseAmount but ensure InfoSectionGrid + footer are visible
                        double tentative = Math.Max(this.MinHeight, this.ActualHeight - Math.Round(collapseAmount));

                        // Compute required client-space height to fully show InfoSectionGrid + footer
                        double infoTop = 0;
                        double infoHeight = 0;
                        if (InfoSectionGrid != null)
                        {
                            var t = InfoSectionGrid.TransformToAncestor(this);
                            var p = t.Transform(new System.Windows.Point(0, 0));
                            infoTop = p.Y;
                            infoHeight = InfoSectionGrid.ActualHeight;
                        }

                        double footerH = (FooterBorder != null) ? FooterBorder.ActualHeight : 40.0;
                        double requiredClient = infoTop + infoHeight + footerH + 32; // padding

                        // If tentative would hide the info section, expand to required size
                        double finalHeight = tentative;
                        if (this.ActualHeight < requiredClient || tentative < requiredClient)
                        {
                            finalHeight = Math.Min(GetCurrentWorkAreaHeight(), Math.Max(requiredClient, this.MinHeight));
                        }

                        this.Height = finalHeight;
                    }
                    catch { /* best-effort sizing; ignore errors */ }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else // EXPAND: show content and restore previous height if available
            {
                CollapsibleMainContent.Visibility = Visibility.Visible;

                // Allow layout to update so sizes are correct, then restore height (prefer saved height)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    this.UpdateLayout();
                    if (!double.IsNaN(_savedWindowHeightForCompact) && _savedWindowHeightForCompact > 0)
                    {
                        // Clamp to work area
                        this.Height = Math.Min(_savedWindowHeightForCompact, GetCurrentWorkAreaHeight());
                        _savedWindowHeightForCompact = double.NaN;
                    }
                    else
                    {
                        // If no saved height, ensure window is tall enough to show content
                        CollapsibleMainContent.Measure(new System.Windows.Size(this.ActualWidth, double.PositiveInfinity));
                        double desired = this.ActualHeight + CollapsibleMainContent.DesiredSize.Height;
                        this.Height = Math.Min(Math.Max(this.MinHeight, desired), GetCurrentWorkAreaHeight());
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }

            if (compactToggleIcon != null)
                compactToggleIcon.Text = _isCompactView ? "▸" : "▾";

            if (compactToggleButton != null)
                compactToggleButton.ToolTip = LocalizationManager.Instance.Strings[_isCompactView ? "Btn_Compact_Show" : "Btn_Compact_Hide"];
        }

        private void CompactToggleButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle UI and update state
            SetCompactView(!_isCompactView);

            // (Optional) persist preference later via ConfigManager
            // ConfigManager.Instance.SetValue("mainwindow_compact", _isCompactView.ToString());
        }

        private void SelectWindowButton_Click(object sender, RoutedEventArgs e)
        {
            if (isCapturingWindow)
            {
                StopWindowCapture(windowLost: false);
                Console.WriteLine("Window capture mode disabled");
            }
            else
            {
                WindowSelectorPopup popup = new WindowSelectorPopup { Owner = this };
                popup.WindowSelected += (handle, title) => OnWindowSelected(handle, title);
                popup.ShowDialog();
            }
        }

        public bool IsWindows10 => Windows_Version == "Windows 10";

        // For the welcome screen, which offers the game window choice on first run
        public bool IsCapturingGameWindow => isCapturingWindow;
        public string CapturedWindowTitle => capturedWindowTitle;
        public void SelectGameWindow(IntPtr windowHandle, string windowTitle) =>
            OnWindowSelected(windowHandle, windowTitle, showConfirmation: false);

        private bool IsOverlayShown =>
            MonitorWindow.Instance.imageScrollViewer.Visibility == Visibility.Visible && MonitorWindow.Instance.IsVisible;

        // Whether the overlay stays out of the captured image. Windows 11 excludes it from capture. Windows 10
        // cannot, so only Windows Graphics Capture of the game window leaves it out (the CopyFromScreen
        // fallback used when WGC is unavailable copies the screen, overlay included).
        private bool IsOverlayKeptOutOfCapture => !IsWindows10 || (isCapturingWindow && _graphicsCaptureService != null);

        // While the overlay would be captured, OCR would read the translations back. Hides it in that case.
        private bool HideOverlayIfItWouldBeCaptured()
        {
            if (IsOverlayKeptOutOfCapture || !IsOverlayShown)
                return false;

            HideOverlay(notify: false);
            Console.WriteLine("Overlay hidden: Windows 10 is capturing the screen area, which would include it");
            return true;
        }

        // Leaves window capture mode (turned off by the user, or the window went away) and goes back to
        // capturing the screen area. The overlay is part of the screen again: Windows 11 excludes it from
        // capture, Windows 10 cannot, so there it is hidden until a window is selected again.
        private void StopWindowCapture(bool windowLost)
        {
            isCapturingWindow = false;
            capturedWindowHandle = IntPtr.Zero;
            capturedWindowTitle = string.Empty;
            _areaWindowAnchor = null;
            _graphicsCaptureService?.Dispose();
            _graphicsCaptureService = null;

            Dispatcher.Invoke(() =>
            {
                selectWindowButton.SetBinding(ContentControl.ContentProperty,
                    new System.Windows.Data.Binding("Strings[Btn_SelectWindow]") { Source = LocalizationManager.Instance });
                selectWindowButton.Background = new SolidColorBrush(Color.FromRgb(69, 107, 160)); // Blue
                UpdateCaptureRect();
                if (!IsWindows10)
                {
                    MonitorWindow.Instance.EnableExcludeFromCapture();
                }
                bool overlayHidden = HideOverlayIfItWouldBeCaptured();

                if (windowLost)
                {
                    System.Windows.MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_WindowLost"],
                        LocalizationManager.Instance.Strings["Title_WindowLost"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                if (overlayHidden)
                {
                    System.Windows.MessageBox.Show(
                        LocalizationManager.Instance.Strings["Msg_Win10OverlayHiddenNoWindow"],
                        LocalizationManager.Instance.Strings["Title_Information"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            });
        }

        // At startup: ask for the game window, so that it (not the screen area) is captured from the start.
        // Skipping keeps screen-area capture; on Windows 10 the overlay then stays off.
        private void PromptForGameWindowAtStartup()
        {
            _startupPromptPending = false;
            if (isCapturingWindow)
                return;

            if (!CanShowStartupDialog())
            {
                _startupPromptPending = true;
                return;
            }

            var popup = new WindowSelectorPopup { Owner = this };
            popup.ConfigureForStartup(IsWindows10);
            popup.WindowSelected += (handle, title) => OnWindowSelected(handle, title, showConfirmation: false);
            if (!ShowStartupDialog(popup))
            {
                _startupPromptPending = true;
                return;
            }

            WarnIfWindows10WithoutGameWindow();
        }

        // Startup dialogs (welcome screen, game window choice) waiting for this window to be restored: the
        // area selector minimizes it, and a dialog owned by a minimized window is hidden along with it
        private bool _welcomePending;
        private bool _startupPromptPending;
        private Window? _openStartupDialog;
        private bool _startupDialogInterrupted;

        private bool CanShowStartupDialog() =>
            IsVisible && WindowState != WindowState.Minimized && !isSelectingTranslationArea;

        // Shows a startup dialog; false when Select Area closed it before the user answered
        private bool ShowStartupDialog(Window dialog)
        {
            _openStartupDialog = dialog;
            _startupDialogInterrupted = false;
            try
            {
                dialog.ShowDialog();
            }
            finally
            {
                _openStartupDialog = null;
            }
            return !_startupDialogInterrupted;
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized || (!_welcomePending && !_startupPromptPending))
                return;

            Dispatcher.BeginInvoke(() =>
            {
                if (_welcomePending)
                    CheckAndShowWelcome();
                else if (_startupPromptPending)
                    PromptForGameWindowAtStartup();
            }, DispatcherPriority.ApplicationIdle);
        }

        // After the startup window choice was skipped: on Windows 10 the overlay stays off until a window is chosen
        private void WarnIfWindows10WithoutGameWindow()
        {
            if (IsWindows10 && !isCapturingWindow)
            {
                System.Windows.MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_Win10RequiresSelectWindow"],
                    LocalizationManager.Instance.Strings["Title_Information"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OnWindowSelected(IntPtr windowHandle, string windowTitle, bool showConfirmation = true)
        {
            if (windowHandle != IntPtr.Zero)
            {
                capturedWindowHandle = windowHandle;
                capturedWindowTitle = windowTitle;
                isCapturingWindow = true;
                // Areas move with this window from where it is now
                _areaWindowAnchor = null;

                // Start Windows Graphics Capture for flicker-free window capture.
                _graphicsCaptureService?.Dispose();
                _graphicsCaptureService = new GraphicsCaptureService();
                if (!_graphicsCaptureService.StartCapture(windowHandle))
                {
                    Console.WriteLine("WGC not available, will use screen capture fallback");
                    _graphicsCaptureService.Dispose();
                    _graphicsCaptureService = null;
                    // The fallback copies the screen: on Windows 10 that includes the overlay
                    HideOverlayIfItWouldBeCaptured();
                }

                // WGC captures the window directly, so the overlay doesn't need to be excluded. The
                // screen-copy fallback does need it: without it the overlay's own translation was
                // OCR'd and translated again, over and over.
                if (Windows_Version != "Windows 10")
                {
                    if (_graphicsCaptureService != null)
                        MonitorWindow.Instance.DisableExcludeFromCapture();
                    else
                        MonitorWindow.Instance.EnableExcludeFromCapture();
                }

                selectWindowButton.Content = $"Window: {(capturedWindowTitle.Length > 10 ? capturedWindowTitle.Substring(0, 10) + "..." : capturedWindowTitle)}";
                selectWindowButton.Background = new SolidColorBrush(Color.FromRgb(220, 0, 0)); // Red

                Console.WriteLine($"Selected window: {capturedWindowTitle} (Handle: {capturedWindowHandle})");

                if (showConfirmation)
                {
                    System.Windows.MessageBox.Show(
                        $"Now capturing window: {capturedWindowTitle}",
                        "Window Selected",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
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
                if (FindLanguageItem(sourceLanguageComboBox, savedSourceLanguage) is ComboBoxItem sourceItem)
                {
                    // Temporarily remove event handler to prevent triggering changes
                    sourceLanguageComboBox.SelectionChanged -= SourceLanguageComboBox_SelectionChanged;
                    sourceLanguageComboBox.SelectedItem = sourceItem;
                    sourceLanguageComboBox.SelectionChanged += SourceLanguageComboBox_SelectionChanged;
                    Console.WriteLine($"Set source language to {savedSourceLanguage}");
                }

                // Set target language if found in config
                if (FindLanguageItem(targetLanguageComboBox, savedTargetLanguage) is ComboBoxItem targetItem)
                {
                    targetLanguageComboBox.SelectionChanged -= TargetLanguageComboBox_SelectionChanged;
                    targetLanguageComboBox.SelectedItem = targetItem;
                    targetLanguageComboBox.SelectionChanged += TargetLanguageComboBox_SelectionChanged;
                    Console.WriteLine($"Set target language to {savedTargetLanguage}");
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
            ShowWelcomeWindow();
        }

        // Factory reset of every setting (API keys and the interface language are kept), then a restart:
        // too much state is read once at startup to apply the defaults in place
        private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                LocalizationManager.Instance["Msg_ResetSettingsConfirm"],
                LocalizationManager.Instance["Btn_ResetSettings"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
                return;

            string? backupPath;
            try
            {
                backupPath = ConfigManager.Instance.ResetToDefaults();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Settings reset failed: {ex.Message}");
                System.Windows.MessageBox.Show(string.Format(LocalizationManager.Instance["Msg_ResetSettingsFailed"], ex.Message),
                    LocalizationManager.Instance["Btn_ResetSettings"], MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            System.Windows.MessageBox.Show(
                string.Format(LocalizationManager.Instance["Msg_ResetSettingsDone"], backupPath ?? "-"),
                LocalizationManager.Instance["Btn_ResetSettings"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            App.RestartApplication();
        }

        // False when Select Area interrupted the welcome screen, so it is shown again later
        private bool ShowWelcomeWindow()
        {
            var welcomeWindow = new WelcomeWindow { Owner = this };
            if (!ShowStartupDialog(welcomeWindow))
            {
                return false;
            }
            if (welcomeWindow.DialogResult != true)
            {
                return true;
            }

            // The welcome screen saves the language pair to config; OCR reads it from these combo boxes
            LoadLanguageSettingsFromConfig();
            Logic.Instance.ClearAllTextObjects();

            if (welcomeWindow.OpenSettingsRequested)
            {
                SettingsButton_Click(settingsButton, new RoutedEventArgs());
            }
            return true;
        }

        private void DonateButton_Click(object sender, RoutedEventArgs e)
        {
            // Open donation link in default browser. Change URL to your actual donation page if needed.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://buymeacoffee.com/thanhkeke97",
                UseShellExecute = true
            });
        }

        private void CheckAndShowWelcome()
        {
            _welcomePending = false;
            if (ConfigManager.Instance.IsNeedShowQuickStart())
            {
                // Select Area pressed during the splash screen: the window opens minimized
                if (!CanShowStartupDialog())
                {
                    _welcomePending = true;
                    return;
                }

                // The welcome screen includes the game window choice, so the separate prompt is not shown too
                if (!ShowWelcomeWindow())
                {
                    _welcomePending = true;
                    return;
                }
                WarnIfWindows10WithoutGameWindow();

                // Wait until the main window has been laid out so the tour can anchor to its buttons
                Dispatcher.BeginInvoke(() => CoachMarkTour.StartIfNeeded(this), DispatcherPriority.ApplicationIdle);
            }
            else
            {
                // Once the main window is on screen
                Dispatcher.BeginInvoke(PromptForGameWindowAtStartup, DispatcherPriority.ApplicationIdle);
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

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool enableDarkMode = !ConfigManager.Instance.IsDarkModeEnabled();
                ConfigManager.Instance.SetDarkModeEnabled(enableDarkMode);
                ThemeManager.ApplyTheme(enableDarkMode);
                UpdateThemeToggleButtonUI();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error toggling theme: {ex.Message}");
            }
        }

        private void UpdateThemeToggleButtonUI()
        {
            try
            {
                if (themeToggleIcon == null || themeToggleButton == null)
                {
                    return;
                }

                bool isDarkMode = ConfigManager.Instance.IsDarkModeEnabled();
                themeToggleIcon.Text = isDarkMode ? "☀" : "🌙";
                themeToggleButton.ToolTip = isDarkMode ? "Switch to Light Mode" : "Switch to Dark Mode";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating theme toggle UI: {ex.Message}");
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
                // status OCR
                string savedOcrMethod = ConfigManager.Instance.GetOcrMethod();
                if(savedOcrMethod == "Windows OCR" || savedOcrMethod == "OneOCR")
                {
                    OCRStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(20, 180, 20)); // Green
                    OCRStatusText.Text = LocalizationManager.Instance.Strings["Btn_On"];
                }
                else
                {
                    OCRStatusEllipse.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                    OCRStatusText.Text = LocalizationManager.Instance.Strings["Btn_Off"];
                }
                AppVersion.Text = LocalizationManager.Instance.Strings["App_Version"] + " " + SplashManager.FormatVersion(SplashManager.CurrentVersion);

                // Reload screen list to update localized text
                LoadScreenList();

                // Refresh dark/light toggle tooltip if language changed
                UpdateThemeToggleButtonUI();
            }
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

        #region Clipboard Auto-Translate

        /// <summary>
        /// Initializes clipboard monitoring if enabled in settings.
        /// </summary>
        private void InitializeClipboardMonitor()
        {
            try
            {
                // Load settings
                var monitor = ClipboardMonitor.Instance;
                monitor.DebounceMs = ConfigManager.Instance.GetClipboardDebounceMs();
                monitor.MaxCharacters = ConfigManager.Instance.GetClipboardMaxChars();

                // Subscribe to event
                monitor.ClipboardTextChanged -= OnClipboardTextChanged;
                monitor.ClipboardTextChanged += OnClipboardTextChanged;

                // Start listening if enabled
                if (ConfigManager.Instance.IsClipboardAutoTranslateEnabled())
                {
                    monitor.StartListening(this);
                    Console.WriteLine("[MainWindow] Clipboard monitor started.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainWindow] Error initializing clipboard monitor: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles clipboard text changes and triggers translation.
        /// </summary>
        private async void OnClipboardTextChanged(object? sender, ClipboardTextChangedEventArgs e)
        {
            if (!ConfigManager.Instance.IsClipboardAutoTranslateEnabled())
            {
                return;
            }

            string text = e.Text;
            int maxChars = ConfigManager.Instance.GetClipboardMaxChars();

            // Check text length
            if (text.Length > maxChars)
            {
                Console.WriteLine($"[ClipboardTranslate] Text too long ({text.Length} > {maxChars})");
                ShowFastNotification(
                    LocalizationManager.Instance.Strings["NotificationTitle_ClipboardError"],
                    LocalizationManager.Instance.Strings["NotificationMessage_ClipboardTooLong"]
                );
                return;
            }

            Console.WriteLine($"[ClipboardTranslate] Processing {text.Length} chars");

            // Show translating notification
            ShowFastNotification(
                LocalizationManager.Instance.Strings["NotificationTitle_ClipboardTranslating"],
                LocalizationManager.Instance.Strings["NotificationMessage_ClipboardTranslating"]
            );

            try
            {
                Console.WriteLine($"[ClipboardTranslate] Calling TranslateTextImmediateAsync...");
                
                // Perform translation
                string? translatedText = await Logic.Instance.TranslateTextImmediateAsync(text);

                Console.WriteLine($"[ClipboardTranslate] TranslateTextImmediateAsync returned: {(translatedText != null ? $"'{translatedText.Substring(0, Math.Min(50, translatedText.Length))}...'" : "null")}");

                if (!string.IsNullOrWhiteSpace(translatedText))
                {
                    Console.WriteLine($"[ClipboardTranslate] Translation successful, length={translatedText.Length}");

                    // Copy result to clipboard if enabled
                    if (ConfigManager.Instance.IsClipboardCopyResultEnabled())
                    {
                        ClipboardMonitor.Instance.SetClipboardTextSilently(translatedText);
                    }

                    // Add to translation history
                    AddTranslationToHistory(text, translatedText, fromClipboard: true);

                    // Update ChatBox if open
                    // ChatBoxWindow.Instance?.OnTranslationWasAdded(text, translatedText, fromClipboard: true);

                    // Show success notification
                    ShowFastNotification(
                        LocalizationManager.Instance.Strings["NotificationTitle_ClipboardTranslated"],
                        LocalizationManager.Instance.Strings["NotificationMessage_ClipboardTranslated"]
                    );
                }
                else
                {
                    Console.WriteLine($"[ClipboardTranslate] Translation returned empty");
                    ShowFastNotification(
                        LocalizationManager.Instance.Strings["NotificationTitle_ClipboardError"],
                        LocalizationManager.Instance.Strings["NotificationMessage_ClipboardError"]
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClipboardTranslate] Error: {ex.Message}");
                ShowFastNotification(
                    LocalizationManager.Instance.Strings["NotificationTitle_ClipboardError"],
                    LocalizationManager.Instance.Strings["NotificationMessage_ClipboardError"]
                );
            }
        }

        /// <summary>
        /// Starts or stops clipboard monitoring based on settings.
        /// Called from SettingsWindow when toggle changes.
        /// </summary>
        public void UpdateClipboardMonitorState()
        {
            var monitor = ClipboardMonitor.Instance;

            if (ConfigManager.Instance.IsClipboardAutoTranslateEnabled())
            {
                monitor.DebounceMs = ConfigManager.Instance.GetClipboardDebounceMs();
                monitor.MaxCharacters = ConfigManager.Instance.GetClipboardMaxChars();
                
                if (!monitor.IsListening)
                {
                    monitor.StartListening(this);
                    Console.WriteLine("[MainWindow] Clipboard monitor started via settings.");
                }
            }
            else
            {
                if (monitor.IsListening)
                {
                    monitor.StopListening();
                    Console.WriteLine("[MainWindow] Clipboard monitor stopped via settings.");
                }
            }
        }

        #endregion
    }
}