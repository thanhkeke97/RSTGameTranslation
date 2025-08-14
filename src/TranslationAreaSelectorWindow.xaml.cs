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
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace RSTGameTranslation
{
    public partial class TranslationAreaSelectorWindow : Window
    {
        private Point startPoint;
        private bool isDrawing = false;
        
        // Event to notify when selection is complete
        public event EventHandler<Rect>? SelectionComplete;

        private static TranslationAreaSelectorWindow? _currentInstance;
        
        // Store the selected screen and its DPI scaling
        private Forms.Screen _selectedScreen;
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;
        
        // Win32 API for DPI awareness
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint dwFlags);
        
        [DllImport("Shcore.dll")]
        static extern IntPtr GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        
        // Constants for GetDpiForMonitor
        private const int MDT_EFFECTIVE_DPI = 0;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        public TranslationAreaSelectorWindow()
        {
            InitializeComponent();
            
            // Set up mouse events
            this.MouseLeftButtonDown += OnMouseLeftButtonDown;
            this.MouseMove += OnMouseMove;
            this.MouseLeftButtonUp += OnMouseLeftButtonUp;
            this.KeyDown += OnKeyDown;
            
            // Set window size to cover selected screen
            SetWindowToSelectedScreen();
        }
        
        // Track whether this window was cancelled
        public bool WasCancelled { get; set; } = false;
        
        // Static method to create or reuse the selector window
        public static TranslationAreaSelectorWindow GetInstance()
        {
            // If an instance already exists, close it and create a new one
            if (_currentInstance != null)
            {
                _currentInstance.WasCancelled = true; // Mark as cancelled
                _currentInstance.Close();
                _currentInstance = null;
            }
            
            _currentInstance = new TranslationAreaSelectorWindow();
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
        
        // Get DPI scaling for a screen
        private void GetDpiScaling(Forms.Screen screen)
        {
            try
            {
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
        
        // Win32 API for window positioning
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        // Constants for SetWindowPos
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOACTIVATE = 0x0010;

        // Set window to cover selected screen using Windows API
        private void SetWindowToSelectedScreen()
        {
            try
            {
                // Reset window to normal state initially
                this.WindowState = WindowState.Normal;
                
                // Get the selected screen index from config
                int selectedScreenIndex = ConfigManager.Instance.GetSelectedScreenIndex();
                
                // Get all screens
                var screens = Forms.Screen.AllScreens;
                
                // Validate index and set window to cover only the selected screen
                if (selectedScreenIndex >= 0 && selectedScreenIndex < screens.Length)
                {
                    _selectedScreen = screens[selectedScreenIndex];
                    
                    // Get DPI scaling for the selected screen
                    GetDpiScaling(_selectedScreen);
                    
                    // Set window style to ensure it covers exactly the screen area
                    this.WindowStyle = WindowStyle.None;
                    this.ResizeMode = ResizeMode.NoResize;
                    
                    // Get the window handle
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    
                    // If handle is not created yet, create it
                    if (hwnd == IntPtr.Zero)
                    {
                        hwnd = new WindowInteropHelper(this).EnsureHandle();
                    }
                    
                    // Set window position using Windows API
                    var bounds = _selectedScreen.Bounds;
                    SetWindowPos(
                        hwnd,
                        HWND_TOPMOST,
                        bounds.Left / (int)_dpiScaleX,
                        bounds.Top / (int)_dpiScaleX,
                        bounds.Width / (int)_dpiScaleX,
                        bounds.Height / (int)_dpiScaleX,
                        SWP_SHOWWINDOW | SWP_NOACTIVATE
                    );
                    
                    // Also set WPF properties for consistency
                    this.Left = bounds.Left / _dpiScaleX;
                    this.Top = bounds.Top / _dpiScaleX;
                    this.Width = bounds.Width / _dpiScaleX;
                    this.Height = bounds.Height / _dpiScaleX;
                    
                    Console.WriteLine($"Set selection window to cover screen {selectedScreenIndex}: " +
                        $"({bounds.Left}, {bounds.Top}, {bounds.Width}, {bounds.Height})");
                    Console.WriteLine($"DPI scaling: X={_dpiScaleX}, Y={_dpiScaleY}");
                    
                    // Set window to topmost to ensure it's visible
                    this.Topmost = true;
                }
                else
                {
                    // Fallback to all screens if selected screen index is invalid
                    Console.WriteLine($"Invalid screen index {selectedScreenIndex}, falling back to all screens");
                    this.Left = SystemParameters.VirtualScreenLeft;
                    this.Top = SystemParameters.VirtualScreenTop;
                    this.Width = SystemParameters.VirtualScreenWidth;
                    this.Height = SystemParameters.VirtualScreenHeight;
                    
                    // Use primary screen as fallback
                    _selectedScreen = Forms.Screen.PrimaryScreen;
                    GetDpiScaling(_selectedScreen);
                }
                
                // Position instruction text above the MainWindow
                Loaded += (s, e) => PositionInstructionText();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting window to selected screen: {ex.Message}");
                
                // Fallback to all screens in case of error
                this.Left = SystemParameters.VirtualScreenLeft;
                this.Top = SystemParameters.VirtualScreenTop;
                this.Width = SystemParameters.VirtualScreenWidth;
                this.Height = SystemParameters.VirtualScreenHeight;
                
                // Use primary screen as fallback
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
            instructionText.Text = $"Select translate area: {(int)width} x {(int)height}";
        }
        
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out System.Drawing.Point lpPoint);

        [DllImport("user32.dll")]
        static extern bool ScreenToClient(IntPtr hWnd, ref System.Drawing.Point lpPoint);

        [DllImport("user32.dll")]
        static extern bool ClientToScreen(IntPtr hWnd, ref System.Drawing.Point lpPoint);

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
                MessageBox.Show("Please select a larger area (at least 50x50 pixels).", 
                                "The selected area is too small", 
                                MessageBoxButton.OK, 
                                MessageBoxImage.Warning);
                return;
            }
            
            try
            {
                // Get window handle
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                
                // Convert window coordinates to screen coordinates using Windows API
                System.Drawing.Point topLeft = new System.Drawing.Point((int)left, (int)top);
                System.Drawing.Point bottomRight = new System.Drawing.Point((int)(left + width), (int)(top + height));
                
                // Convert to screen coordinates
                ClientToScreen(hwnd, ref topLeft);
                ClientToScreen(hwnd, ref bottomRight);
                
                // Calculate width and height in screen coordinates
                int screenWidth = bottomRight.X - topLeft.X;
                int screenHeight = bottomRight.Y - topLeft.Y;
                
                // Log the raw screen coordinates
                Console.WriteLine($"Raw screen coordinates: X={topLeft.X}, Y={topLeft.Y}, Width={screenWidth}, Height={screenHeight}");
                
                // Now adjust for DPI scaling if needed
                double screenX = topLeft.X;
                double screenY = topLeft.Y;
                
                // For high DPI screens, we need to adjust the coordinates
                // This is because the screen coordinates are in logical pixels, but we need physical pixels
                if (_dpiScaleX != 1.0 || _dpiScaleY != 1.0)
                {
                    // Adjust for DPI scaling
                    // The offset from screen origin needs to be scaled
                    double offsetX = screenX - _selectedScreen.Bounds.Left;
                    double offsetY = screenY - _selectedScreen.Bounds.Top;
                    
                    // Scale the offset
                    double scaledOffsetX = offsetX * _dpiScaleX;
                    double scaledOffsetY = offsetY * _dpiScaleY;
                    
                    // Calculate the new screen coordinates
                    screenX = _selectedScreen.Bounds.Left + scaledOffsetX;
                    screenY = _selectedScreen.Bounds.Top + scaledOffsetY;
                    
                    // Scale the width and height
                    screenWidth = (int)(screenWidth * _dpiScaleX);
                    screenHeight = (int)(screenHeight * _dpiScaleY);
                    
                    Console.WriteLine($"Adjusted for DPI: X={screenX}, Y={screenY}, Width={screenWidth}, Height={screenHeight}");
                }
                
                // Create rectangle for the selection using the calculated coordinates
                Rect selectionRect = new Rect(
                    screenX, 
                    screenY, 
                    screenWidth, 
                    screenHeight
                );
                
                // Notify listeners
                SelectionComplete?.Invoke(this, selectionRect);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calculating screen coordinates: {ex.Message}");
                
                // Try a different approach as fallback
                try
                {
                    // Get the actual screen coordinates using PointToScreen
                    Point screenPoint = this.PointToScreen(new Point(left, top));
                    Point screenBottomRight = this.PointToScreen(new Point(left + width, top + height));
                    
                    // Calculate width and height in screen coordinates
                    double screenWidth = screenBottomRight.X - screenPoint.X;
                    double screenHeight = screenBottomRight.Y - screenPoint.Y;
                    
                    Console.WriteLine($"Fallback screen coordinates: X={screenPoint.X}, Y={screenPoint.Y}, Width={screenWidth}, Height={screenHeight}");
                    
                    // Apply DPI scaling if needed
                    if (_dpiScaleX != 1.0 || _dpiScaleY != 1.0)
                    {
                        // Adjust for DPI scaling
                        double offsetX = screenPoint.X - _selectedScreen.Bounds.Left;
                        double offsetY = screenPoint.Y - _selectedScreen.Bounds.Top;
                        
                        double scaledOffsetX = offsetX * _dpiScaleX;
                        double scaledOffsetY = offsetY * _dpiScaleY;
                        
                        double adjustedX = _selectedScreen.Bounds.Left + scaledOffsetX;
                        double adjustedY = _selectedScreen.Bounds.Top + scaledOffsetY;
                        
                        screenWidth = screenWidth * _dpiScaleX;
                        screenHeight = screenHeight * _dpiScaleY;
                        
                        Console.WriteLine($"Fallback adjusted for DPI: X={adjustedX}, Y={adjustedY}, Width={screenWidth}, Height={screenHeight}");
                        
                        // Create rectangle with adjusted coordinates
                        Rect selectionRect = new Rect(
                            adjustedX, 
                            adjustedY, 
                            screenWidth, 
                            screenHeight
                        );
                        
                        // Notify listeners
                        SelectionComplete?.Invoke(this, selectionRect);
                    }
                    else
                    {
                        // Create rectangle without DPI adjustment
                        Rect selectionRect = new Rect(
                            screenPoint.X, 
                            screenPoint.Y, 
                            screenWidth, 
                            screenHeight
                        );
                        
                        // Notify listeners
                        SelectionComplete?.Invoke(this, selectionRect);
                    }
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"Fallback approach also failed: {ex2.Message}");
                    
                    // Last resort: use the raw coordinates without any adjustment
                    Rect selectionRect = new Rect(
                        _selectedScreen.Bounds.Left + left,
                        _selectedScreen.Bounds.Top + top,
                        width,
                        height
                    );
                    
                    // Notify listeners
                    SelectionComplete?.Invoke(this, selectionRect);
                }
            }

            // Close this window
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