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

namespace UGTLive
{
    public partial class ChatBoxSelectorWindow : Window
    {
        private Point startPoint;
        private bool isDrawing = false;
        
        // Event to notify when selection is complete
        public event EventHandler<Rect>? SelectionComplete;

        private static ChatBoxSelectorWindow? _currentInstance;

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
            // Reset window to normal state initially
            this.WindowState = WindowState.Normal;
            
            // Get all screens
            var allScreens = Forms.Screen.AllScreens;
            
            // Calculate the full virtual screen bounds
            int left = int.MaxValue;
            int top = int.MaxValue;
            int right = int.MinValue;
            int bottom = int.MinValue;
            
            foreach (var screen in allScreens)
            {
                left = Math.Min(left, screen.Bounds.Left);
                top = Math.Min(top, screen.Bounds.Top);
                right = Math.Max(right, screen.Bounds.Right);
                bottom = Math.Max(bottom, screen.Bounds.Bottom);
            }
            
            // Set window position and size to cover all screens
            this.Left = left;
            this.Top = top;
            this.Width = right - left;
            this.Height = bottom - top;
            
            // Position instruction text above the MainWindow
            Loaded += (s, e) => PositionInstructionText();
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
            instructionText.Text = $"Selection size: {(int)width} x {(int)height}";
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
                MessageBox.Show("Please select a larger area (at least 50x50 pixels).", 
                                "Selection too small", 
                                MessageBoxButton.OK, 
                                MessageBoxImage.Warning);
                return;
            }
            
            // Convert window coordinates to screen coordinates
            // This ensures we use the actual screen coordinates for positioning
            Point screenPoint = this.PointToScreen(new Point(left, top));
            
            // Get the screen containing this point
            Forms.Screen targetScreen = Forms.Screen.FromPoint(new System.Drawing.Point(
                (int)screenPoint.X, 
                (int)screenPoint.Y
            ));
            
            // Create rectangle for the selection
            Rect selectionRect = new Rect(
                screenPoint.X, 
                screenPoint.Y, 
                width, 
                height
            );
            
            // Notify listeners
            SelectionComplete?.Invoke(this, selectionRect);
            
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