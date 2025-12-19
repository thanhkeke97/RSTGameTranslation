using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Forms; // For Screen class
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using MessageBox = System.Windows.MessageBox;
using Forms = System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace RSTGameTranslation
{
    public partial class ChatBoxSelectorWindow : Window
    {
        private Point startPoint;
        private bool isDrawing = false;
        
        // Event to notify when selection is complete
        public event EventHandler<Rect>? SelectionComplete;

        private static ChatBoxSelectorWindow? _currentInstance;

        public double _dpiScaleX = 1.0;
        public double _dpiScaleY = 1.0;
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint dwFlags);
        [DllImport("Shcore.dll")]
        static extern IntPtr GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private const int MDT_EFFECTIVE_DPI = 0;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOACTIVATE = 0x0010;
        private Forms.Screen? _selectedScreen = Forms.Screen.PrimaryScreen;

        public ChatBoxSelectorWindow()
        {
            InitializeComponent();
            
            // Set up mouse events
            this.MouseLeftButtonDown += OnMouseLeftButtonDown;
            this.MouseMove += OnMouseMove;
            this.MouseLeftButtonUp += OnMouseLeftButtonUp;
            this.KeyDown += OnKeyDown;
            
            // Set window size to cover all monitors
            SetWindowToAllScreens();
        }

        private Forms.Screen GetActiveScreen()
        {
            if (_selectedScreen != null) return _selectedScreen;
            
            var primary = Forms.Screen.PrimaryScreen;
            if (primary != null) { _selectedScreen = primary; return primary; }
            
            var screens = Forms.Screen.AllScreens;
            if (screens.Length > 0) { _selectedScreen = screens[0]; return screens[0]; }
            
            throw new InvalidOperationException("No monitors detected.");
        }

        // Get DPI scaling for a screen
        private void GetDpiScaling(Forms.Screen? screen)
        {
            try
            {
                if (screen == null)
                {
                    _dpiScaleX = 1.0;
                    _dpiScaleY = 1.0;
                    return;
                }
                
                // Get the monitor handle from screen
                System.Drawing.Point point = new System.Drawing.Point(
                    screen.Bounds.Left + screen.Bounds.Width / 2,
                    screen.Bounds.Top + screen.Bounds.Height / 2
                );
                
                IntPtr monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
                
                // Get DPI for the monitor
                uint dpiX, dpiY;
                GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
                
                // Calculate scaling factor (96 is the default DPI)
                _dpiScaleX = dpiX / 96.0;
                _dpiScaleY = dpiY / 96.0;
                
                Console.WriteLine($"Screen DPI: X={dpiX}, Y={dpiY}, Scale: X={_dpiScaleX}, Y={_dpiScaleY}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting DPI scaling: {ex.Message}");
                // Default to 1.0 if there's an error
                _dpiScaleX = 1.0;
                _dpiScaleY = 1.0;
            }
        }
        
        // Track whether this window was cancelled
        public bool WasCancelled { get; set; } = false;
        
        // Static method to create or reuse the selector window
        public static ChatBoxSelectorWindow GetInstance()
        {
            // If an instance already exists, close it and create a new one
            if (_currentInstance != null)
            {
                _currentInstance.WasCancelled = true; // Mark as cancelled
                _currentInstance.Close();
                _currentInstance = null;
            }
            
            _currentInstance = new ChatBoxSelectorWindow();
            return _currentInstance;
        }
        
        // Handle Escape key to cancel
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.WasCancelled = true;
                this.Close();
            }
        }
        
        // Set window to cover all screens
        private void SetWindowToAllScreens()
        {
            try
            {
                this.WindowState = WindowState.Normal;
                
                int selectedScreenIndex = ConfigManager.Instance.GetSelectedScreenIndex();
                var screens = Forms.Screen.AllScreens;
                
                if (selectedScreenIndex >= 0 && selectedScreenIndex < screens.Length)
                {
                    _selectedScreen = screens[selectedScreenIndex];
                    
                    GetDpiScaling(_selectedScreen);
                    
                    this.WindowStyle = WindowStyle.None;
                    this.ResizeMode = ResizeMode.NoResize;
                    
                    IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                    
                    var activeScreen = GetActiveScreen();
                    var bounds = activeScreen.Bounds;
                    
                    SetWindowPos(
                        hwnd,
                        HWND_TOPMOST,
                        (int)(bounds.Left / _dpiScaleX),    
                        (int)(bounds.Top / _dpiScaleY),
                        (int)(bounds.Width / _dpiScaleX),
                        (int)(bounds.Height / _dpiScaleY),
                        SWP_SHOWWINDOW | SWP_NOACTIVATE
                    );
                    
                    this.Left = bounds.Left / _dpiScaleX;
                    this.Top = bounds.Top / _dpiScaleY;
                    this.Width = bounds.Width / _dpiScaleX;
                    this.Height = bounds.Height / _dpiScaleY;
                    
                    this.Topmost = true;
                }
                else
                {
                    this.Left = SystemParameters.VirtualScreenLeft;
                    this.Top = SystemParameters.VirtualScreenTop;
                    this.Width = SystemParameters.VirtualScreenWidth;
                    this.Height = SystemParameters.VirtualScreenHeight;
                    _selectedScreen = Forms.Screen.PrimaryScreen;
                    GetDpiScaling(_selectedScreen);
                }
                
                Loaded += (s, e) => PositionInstructionText();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting window to selected screen: {ex.Message}");
                this.Left = SystemParameters.VirtualScreenLeft;
                this.Top = SystemParameters.VirtualScreenTop;
                _selectedScreen = Forms.Screen.PrimaryScreen;
                GetDpiScaling(_selectedScreen);
            }
        }
        
        private void PositionInstructionText()
        {
            try
            {
                // Find the MainWindow
                Window? mainWindow = null;
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window is MainWindow)
                    {
                        mainWindow = window;
                        break;
                    }
                }
                
                if (mainWindow != null)
                {
                    // Get the screen that contains the main window
                    System.Drawing.Point mainWindowPoint = new System.Drawing.Point(
                        (int)(mainWindow.Left + (mainWindow.Width / 2)),
                        (int)(mainWindow.Top + (mainWindow.Height / 2))
                    );
                    Forms.Screen mainWindowScreen = Forms.Screen.FromPoint(mainWindowPoint);
                    
                    // Calculate position above MainWindow
                    double centerX = mainWindow.Left + (mainWindow.Width / 2);
                    
                    // Position the instruction text above the MainWindow
                    double textWidth = instructionText.ActualWidth > 0 ? 
                        instructionText.ActualWidth : 450; // Estimate if not yet measured
                    
                    double leftPosition = centerX - (textWidth / 2);
                    double topPosition = mainWindow.Top - 80; // 80px above main window
                    
                    // Make sure it's visible on the screen containing the MainWindow
                    leftPosition = Math.Max(mainWindowScreen.Bounds.Left + 10, leftPosition);
                    leftPosition = Math.Min(mainWindowScreen.Bounds.Right - textWidth - 10, leftPosition);
                    topPosition = Math.Max(mainWindowScreen.Bounds.Top + 10, topPosition);
                    
                    // Update position (convert to window coordinates)
                    Point screenPoint = new Point(leftPosition, topPosition);
                    Point windowPoint = this.PointFromScreen(screenPoint);
                    
                    // Apply new position
                    instructionText.Margin = new Thickness(
                        windowPoint.X, 
                        windowPoint.Y, 
                        0, 
                        0);
                    instructionText.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                    instructionText.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                    
                    // Make the text more visible
                    instructionText.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0, 0, 0));
                    instructionText.Foreground = new SolidColorBrush(System.Windows.Media.Colors.White);
                    instructionText.FontWeight = FontWeights.Bold;
                    instructionText.Padding = new Thickness(20, 10, 20, 10);
                    
                    // Also position the cancel button near the main window
                    PositionCancelButton(mainWindow, mainWindowScreen);
                    
                    Console.WriteLine($"Positioned instruction text at {leftPosition}, {topPosition} on screen {mainWindowScreen.DeviceName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error positioning instruction text: {ex.Message}");
            }
        }
        
        private void PositionCancelButton(Window mainWindow, Forms.Screen mainWindowScreen)
        {
            try
            {
                // Position cancel button to the left of the instruction text
                double textMarginLeft = instructionText.Margin.Left;
                double textMarginTop = instructionText.Margin.Top;
                
                // Position button to the left of the text with a small gap
                double buttonLeft = Math.Max(10, textMarginLeft - cancelButton.Width - 10); // 10px gap
                double buttonTop = textMarginTop; // Same vertical position
                
                // Make sure it's visible on the screen
                buttonLeft = Math.Max(mainWindowScreen.Bounds.Left + 10, buttonLeft);
                buttonTop = Math.Max(mainWindowScreen.Bounds.Top + 10, buttonTop);
                
                // Update button position
                cancelButton.Margin = new Thickness(buttonLeft, buttonTop, 0, 0);
                cancelButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                cancelButton.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                
                // Ensure the button is visible and clickable
                cancelButton.Visibility = Visibility.Visible;
                cancelButton.IsEnabled = true;
                cancelButton.Focus();
                
                Console.WriteLine($"Positioned cancel button at {buttonLeft}, {buttonTop} (to the left of text)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error positioning cancel button: {ex.Message}");
            }
        }
        
        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Capture mouse and start drawing
            startPoint = e.GetPosition(this);
            isDrawing = true;
            
            // Reset and make visible
            selectionRectangle.Width = 0;
            selectionRectangle.Height = 0;
            selectionRectangle.Visibility = Visibility.Visible;
            
            // Set initial position
            Canvas.SetLeft(selectionRectangle, startPoint.X);
            Canvas.SetTop(selectionRectangle, startPoint.Y);
            
            // Capture mouse
            this.CaptureMouse();
        }
        
        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!isDrawing) return;
            
            // Get current position
            Point currentPoint = e.GetPosition(this);
            
            // Calculate rectangle dimensions
            double width = Math.Abs(currentPoint.X - startPoint.X);
            double height = Math.Abs(currentPoint.Y - startPoint.Y);
            
            // Calculate top-left corner
            double left = Math.Min(currentPoint.X, startPoint.X);
            double top = Math.Min(currentPoint.Y, startPoint.Y);
            
            // Update rectangle position and size
            selectionRectangle.Width = width;
            selectionRectangle.Height = height;
            
            // Use margins to position since we don't have a Canvas
            selectionRectangle.Margin = new Thickness(left, top, 0, 0);
            selectionRectangle.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            selectionRectangle.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            
            // Display size information in the instruction text
            instructionText.Text = string.Format(
                LocalizationManager.Instance.Strings["ChatBoxSelector_SelectionSize"],
                (int)width,
                (int)height
            );
        }
        
        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isDrawing) return;
            
            // Release mouse capture
            this.ReleaseMouseCapture();
            isDrawing = false;
            
            // If cancelled, don't proceed with validation
            if (WasCancelled) return;
            
            // Get final dimensions
            Point currentPoint = e.GetPosition(this);
            double width = Math.Abs(currentPoint.X - startPoint.X);
            double height = Math.Abs(currentPoint.Y - startPoint.Y);
            double left = Math.Min(currentPoint.X, startPoint.X);
            double top = Math.Min(currentPoint.Y, startPoint.Y);
            
            // Verify minimum size
            if (width < 50 || height < 50)
            {
                MessageBox.Show(
                    LocalizationManager.Instance.Strings["ChatBoxSelector_Msg_TooSmall"], 
                    LocalizationManager.Instance.Strings["ChatBoxSelector_Title_TooSmall"], 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning
                );
                return;
            }
            DpiHelper.SetKnownDpiScale(_dpiScaleX, _dpiScaleY);
            
            // Convert window coordinates to screen coordinates
            // This ensures we use the actual screen coordinates for positioning
            Point screenPoint = this.PointToScreen(new Point(left, top));
            double logicalX = screenPoint.X / _dpiScaleX;
            double logicalY = screenPoint.Y / _dpiScaleY;
            
            // Get the screen containing this point
            Forms.Screen targetScreen = Forms.Screen.FromPoint(new System.Drawing.Point(
                (int)logicalX, 
                (int)logicalY
            ));
            
            // // Create rectangle for the selection
            // Rect physicalRect  = new Rect(
            //     logicalX, 
            //     logicalY, 
            //     width, 
            //     height
            // );
            
            // Notify listeners
            Rect selectionRect = new Rect(logicalX, logicalY, width, height);
            SelectionComplete?.Invoke(this, selectionRect);
            this.Close();
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Cancel selection
            this.WasCancelled = true;
            this.Close();
        }
    }
}