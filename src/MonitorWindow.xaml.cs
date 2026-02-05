using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;


namespace RSTGameTranslation
{
    public partial class MonitorWindow : Window
    {
        // Add P/Invoke declarations for window attribute
        private const int DWMWA_EXCLUDED_FROM_PEEK = 12;
        private const int DWMWA_CLOAK = 13;
        private const int DWMWA_CLOAKED = 14;
        private const int DWM_TNP_VISIBLE = 8;
        private const int DWM_TNP_OPACITY = 4;
        private const int DWM_TNP_RECTDESTINATION = 1;
        private const int WDA_NONE = 0x00000000;
        private const int WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        public double dpiScale = 1;

        [DllImport("user32.dll")]
        private static extern int SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        private double currentZoom = 1.0;
        private const double zoomIncrement = 0.1;
        private string lastImagePath = string.Empty;
        
        // Singleton pattern to match application style
        private static MonitorWindow? _instance;
        public static MonitorWindow Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MonitorWindow();
                }
                return _instance;
            }
        }

        public static void ResetInstance()
        {
            if (_instance != null)
            {
                _instance.Close();
                _instance = null;
            }
        }

        // Settle time is stored in ConfigManager, no need for a local variable

        public MonitorWindow()
        {
            // Make sure the initialization flag is set before anything else
            _isInitializing = true;
            Console.WriteLine("MonitorWindow constructor: Setting _isInitializing to true");

            InitializeComponent();

            Console.WriteLine("MonitorWindow constructor started");
            this.Topmost = true;
            this.Focusable = false;
            this.ShowActivated = false;
            this.ShowInTaskbar = false;

            // Subscribe to TextObject events from Logic
            //Logic.Instance.TextObjectAdded += CreateMonitorOverlayFromTextObject;

            // Set initial status
            UpdateStatus("Ready");

            // Add loaded event handler
            this.Loaded += MonitorWindow_Loaded;

            // Add size changed handler to update scrollbars
            // this.SizeChanged += MonitorWindow_SizeChanged;

            // // Manually connect events (to ensure we have control over when they're attached)
            // ocrMethodComboBox.SelectionChanged += OcrMethodComboBox_SelectionChanged;
            // autoTranslateCheckBox.Checked += AutoTranslateCheckBox_CheckedChanged;
            // autoTranslateCheckBox.Unchecked += AutoTranslateCheckBox_CheckedChanged;

            // // Add event handlers for the zoom TextBox
            // zoomTextBox.TextChanged += ZoomTextBox_TextChanged;
            // zoomTextBox.LostFocus += ZoomTextBox_LostFocus;

            // // Add KeyDown event handlers for TextBoxes to handle Enter key
            // zoomTextBox.KeyDown += TextBox_KeyDown;

            // SocketManager.Instance.ConnectionChanged += OnSocketConnectionChanged;


            // Set default size if not already set
            if (this.Width == 0)
                this.Width = 600;
            if (this.Height == 0)
                this.Height = 500;

            // Register application-wide keyboard shortcut handler
            this.PreviewKeyDown += Application_KeyDown;

            Console.WriteLine("MonitorWindow constructor completed");
            if (MainWindow.Instance.Windows_Version != "Windows 10")
            {
                // Add SourceInitialized event handler to set window attributes
                this.SourceInitialized += MonitorWindow_SourceInitialized;
                Console.WriteLine("Exclude MonitorWindow from capture success");
            }
        }
        
        // Add a new method to handle SourceInitialized event
        private void MonitorWindow_SourceInitialized(object? sender, EventArgs e)
        {
            EnableExcludeFromCapture();
        }

        public void EnableExcludeFromCapture()
        {
            // Get window handle
            IntPtr hwnd = new WindowInteropHelper(this).Handle;

            // Set the window to be excluded from capture
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

            Console.WriteLine("MonitorWindow set to be excluded from screen capture");
        }

        public void DisableExcludeFromCapture()
        {
            // Get window handle
            IntPtr hwnd = new WindowInteropHelper(this).Handle;

            // Set the window to be excluded from capture
            SetWindowDisplayAffinity(hwnd, WDA_NONE);

            Console.WriteLine("MonitorWindow set to be included in screen capture");
        }

        private void OnSocketConnectionChanged(object? sender, bool isConnected)
        {
            if (isConnected)
            {
                //set our status text
                UpdateStatus("Connected to Python backend");
            }
        }


        // Flag to prevent saving during initialization
        private static bool _isInitializing = true;
        
        // Timer for updating translation status
        private DispatcherTimer? _translationStatusTimer;
        private DateTime _translationStartTime;
        
        // OCR Method Selection Changed
        public void OcrMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip event if we're initializing
            Console.WriteLine($"MonitorWindow.OcrMethodComboBox_SelectionChanged called (isInitializing: {_isInitializing})");
            if (_isInitializing)
            {
                Console.WriteLine("Skipping OCR method change during initialization");
                return;
            }
            
            if (ocrMethodComboBox.SelectedItem == null) return;
            
            string? ocrMethod = (ocrMethodComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrEmpty(ocrMethod)) return;
            
            // Reset the OCR hash to force a fresh comparison after changing OCR method
            Logic.Instance.ResetHash();
            
            Console.WriteLine($"OCR method changed to: {ocrMethod}");
            
            // Clear any existing text objects
            Logic.Instance.ClearAllTextObjects();
            
            // Update the UI and connection state based on the selected OCR method
            if (ocrMethod == "Windows OCR" || ocrMethod == "OneOCR")
            {
                // Using Windows OCR, no need for socket connection
                _ = Task.Run(() => 
                {
                    try
                    {
                       SocketManager.Instance.Disconnect();
                        UpdateStatus("Using Windows OCR (built-in)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error disconnecting socket: {ex.Message}");
                    }
                });
            }
            else
            {
                try
                    {
                       SocketManager.Instance.Disconnect();
                        UpdateStatus($"Using {ocrMethod}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error disconnecting socket: {ex.Message}");
                    }
                // Using EasyOCR or PaddleOCR, check connection status first
                _ = Task.Run(async () => 
                {
                   
                        Console.WriteLine("Switching to new OCR, checking socket connection...");

                        // If already connected, we're good to go
                        if (SocketManager.Instance.IsConnected)
                        {
                            Console.WriteLine("Already connected to socket server");
                            UpdateStatus("Connected to Python backend");
                            return;
                        }

                        // Not connected yet, attempt to connect silently first
                        UpdateStatus("Connecting to Python backend...");

                        // Connect without disconnecting first (TryReconnectAsync handles cleanup)
                        bool reconnected = await SocketManager.Instance.TryReconnectAsync();
                   
                });
            }
            
            // Sync the OCR method selection with MainWindow
            if (MainWindow.Instance != null)
            {
                MainWindow.Instance.SetOcrMethod(ocrMethod);
            }
            
            // Only save if not initializing
            if (!_isInitializing)
            {
                Console.WriteLine($"Saving OCR method to config: '{ocrMethod}'");
                ConfigManager.Instance.SetOcrMethod(ocrMethod);
            }
            else
            {
                Console.WriteLine($"Skipping save during initialization for OCR method: '{ocrMethod}'");
            }
        }
        
        // Auto Translate Checkbox Changed
        private void AutoTranslateCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool isAutoTranslateEnabled = autoTranslateCheckBox.IsChecked ?? false;
            
            Console.WriteLine($"Auto-translate {(isAutoTranslateEnabled ? "enabled" : "disabled")}");
            
            // Clear text objects
            Logic.Instance.ClearAllTextObjects();
            Logic.Instance.ResetHash();
            
            // Force OCR to run again
            MainWindow.Instance.SetOCRCheckIsWanted(true);
            
            // Refresh overlays
            RefreshOverlays();
            
            // Sync the checkbox state with MainWindow
            if (MainWindow.Instance != null)
            {
                MainWindow.Instance.SetAutoTranslateEnabled(isAutoTranslateEnabled);
            }
        }
        
        private void MonitorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("MonitorWindow_Loaded: Starting initialization");
            
            // Set initialization flag to true to prevent saving during setup
            _isInitializing = true;
            
            // Make sure keyboard shortcuts work from this window too
            // PreviewKeyDown -= Application_KeyDown;
            // PreviewKeyDown += Application_KeyDown;

            UpdateScreenshotFromBitmap();

            // // Initialize controls from MainWindow
            // if (MainWindow.Instance != null)
            // {
            //     // Get OCR method from config
            //     string ocrMethod = ConfigManager.Instance.GetOcrMethod();
            //     Console.WriteLine($"MonitorWindow_Loaded: Loading OCR method from config: '{ocrMethod}'");

            //     // Temporarily remove the event handler to prevent triggering
            //     // a new connection while initializing
            //     ocrMethodComboBox.SelectionChanged -= OcrMethodComboBox_SelectionChanged;

            //     // Find the matching ComboBoxItem
            //     bool foundMatch = false;
            //     foreach (ComboBoxItem comboItem in ocrMethodComboBox.Items)
            //     {
            //         string itemText = comboItem.Content.ToString() ?? "";
            //         Console.WriteLine($"Comparing OCR method: '{itemText}' with config value: '{ocrMethod}'");

            //         if (string.Equals(itemText, ocrMethod, StringComparison.OrdinalIgnoreCase))
            //         {
            //             Console.WriteLine($"Found matching OCR method: '{itemText}'");
            //             ocrMethodComboBox.SelectedItem = comboItem;
            //             foundMatch = true;
            //             break;
            //         }
            //     }

            //     if (!foundMatch)
            //     {
            //         Console.WriteLine($"WARNING: Could not find OCR method '{ocrMethod}' in ComboBox. Available items:");
            //         foreach (ComboBoxItem listItem in ocrMethodComboBox.Items)
            //         {
            //             Console.WriteLine($"  - '{listItem.Content}'");
            //         }
            //     }

            //     // Log what we actually set
            //     if (ocrMethodComboBox.SelectedItem is ComboBoxItem selectedItem)
            //     {
            //         Console.WriteLine($"OCR ComboBox is now set to: '{selectedItem.Content}'");
            //     }

            //     // Re-attach the event handler
            //     ocrMethodComboBox.SelectionChanged += OcrMethodComboBox_SelectionChanged;

            //     // Make sure MainWindow has the same OCR method
            //     if (ocrMethodComboBox.SelectedItem is ComboBoxItem selectedComboItem)
            //     {
            //         string selectedOcrMethod = selectedComboItem.Content.ToString() ?? "";
            //         MainWindow.Instance.SetOcrMethod(selectedOcrMethod);
            //     }

            //     // Get auto-translate state from MainWindow
            //     bool isTranslateEnabled = MainWindow.Instance.GetTranslateEnabled();

            //     // Initialization complete, now we can save settings changes
            //     _isInitializing = false;
            //     Console.WriteLine("MonitorWindow initialization complete. Settings changes will now be saved.");

            //     // Force the OCR method to match the config again
            //     // This ensures the config value is preserved and not overwritten
            //     string configOcrMethod = ConfigManager.Instance.GetOcrMethod();
            //     Console.WriteLine($"Ensuring config OCR method is preserved: {configOcrMethod}");

            //     // Now that initialization is complete, save the OCR method from config
            //     ConfigManager.Instance.SetOcrMethod(configOcrMethod);

            //     // Temporarily remove event handler
            //     autoTranslateCheckBox.Checked -= AutoTranslateCheckBox_CheckedChanged;
            //     autoTranslateCheckBox.Unchecked -= AutoTranslateCheckBox_CheckedChanged;

            //     autoTranslateCheckBox.IsChecked = isTranslateEnabled;

            //     // Re-attach event handlers
            //     autoTranslateCheckBox.Checked += AutoTranslateCheckBox_CheckedChanged;
            //     autoTranslateCheckBox.Unchecked += AutoTranslateCheckBox_CheckedChanged;

            GetDpiScale();
            // }
            
            Console.WriteLine("MonitorWindow initialization complete");
        }
        
        // Handler for application-level keyboard shortcuts
        private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Forward to the central keyboard shortcuts handler
            KeyboardShortcuts.HandleKeyDown(e);
        }
        
        private void MonitorWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Update scrollbars when window size changes
            UpdateScrollViewerSettings();
        }
        
        // Update the monitor with a bitmap directly (no file saving required)
        public void UpdateScreenshotFromBitmap()
        {
            if (!Dispatcher.CheckAccess())
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(() => {
                        try
                        {
                            
                            // Show window if needed
                            if (!IsVisible)
                            {
                                Show();
                            }
                            
                            // Update scrollbars
                            UpdateScrollViewerSettings();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error in UI thread bitmap update: {ex.Message}");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Render);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating bitmap source: {ex.Message}");
                }
                return;
            }

            // Direct UI thread path
            try
            {
                
                // Show the window if not visible
                if (!IsVisible)
                {
                    Show();
                }
                
                // Make sure scroll bars appear when needed
                UpdateScrollViewerSettings();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating screenshot from bitmap: {ex.Message}");
                UpdateStatus($"Error: {ex.Message}");
            }
        }
        
        
        // Ensure scroll bars appear when needed
        private void UpdateScrollViewerSettings()
        {
            if (captureImage.Source != null)
            {
                if (captureImage.Source is BitmapSource bitmapSource)
                {
                    double width = bitmapSource.PixelWidth;
                    double height = bitmapSource.PixelHeight;
                    
                    textOverlayCanvas.Width = width;
                    textOverlayCanvas.Height = height;
                    imageContainer.Width = width;
                    imageContainer.Height = height;
                    
                    captureImage.Visibility = Visibility.Collapsed;
                }
            }
        }

        public void GetDpiScale()
        {

            DpiHelper.GetCurrentScreenDpi(out double scaleX, out double scaleY);
            dpiScale = scaleX; 

            Console.WriteLine($"MonitorWindow: Got DPI scale from DpiHelper: {dpiScale:F2}x");

        }
        
        // Handle TextObject added event
        public void CreateMonitorOverlayFromTextObject(object? sender, TextObject textObject)
        {
            try
            {
                // Check for null references
                if (textObject == null || textOverlayCanvas == null)
                {
                    Console.WriteLine("Warning: TextObject or Canvas is null in OnTextObjectAdded");
                    return;
                }

                // We need to create a NEW UI element with positioning appropriate for Canvas
                // but we'll use the existing Border and TextBlock references so updates work
                if (textObject.Border != null)
                {
                    // Reset margin to zero - we'll position with Canvas instead
                    textObject.Border.Margin = new Thickness(0);
                    
                    System.Windows.Point logicalPoint = DpiHelper.PhysicalToLogical(new System.Windows.Point(textObject.X, textObject.Y));
                    
                    // Position the element on the canvas using Canvas.SetLeft/Top
                    Canvas.SetLeft(textObject.Border, logicalPoint.X);
                    Canvas.SetTop(textObject.Border, logicalPoint.Y);

                    // Add to canvas
                    textOverlayCanvas.Children.Add(textObject.Border);
                    textObject.Border.IsHitTestVisible = false;
                }
                else
                {
                    Console.WriteLine("Warning: TextObject.Border is null");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding text to monitor: {ex.Message}");
                UpdateStatus($"Error adding text overlay: {ex.Message}");
            }
        }
        
        // Zoom controls
        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            currentZoom += zoomIncrement;
            ApplyZoom();
        }
        
        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            currentZoom = Math.Max(0.1, currentZoom - zoomIncrement);
            ApplyZoom();
        }
        
        // Removed ResetZoomButton_Click as it's no longer needed with the TextBox
        
        private void ApplyZoom()
        {
            // Set the transform on the container to scale both image and overlays together
            ScaleTransform scaleTransform = new ScaleTransform(currentZoom, currentZoom);
            imageContainer.LayoutTransform = scaleTransform;
            
            // Make sure scroll bars are correctly shown after zoom change
            UpdateScrollViewerSettings();
            
            // Update zoom textbox
            zoomTextBox.Text = ((int)(currentZoom * 100)).ToString();
            UpdateStatus($"Zoom: {(int)(currentZoom * 100)}%");
            Console.WriteLine($"Zoom level changed to {(int)(currentZoom * 100)}%");
        }
        
        // Method to refresh text overlays
        public void RefreshOverlays()
        {
            try
            {
                // Check if we need to invoke on the UI thread
                if (!Dispatcher.CheckAccess())
                {
                    // Use Invoke to ensure we wait for completion
                    Dispatcher.Invoke(() => RefreshOverlays());
                    return;
                }
                
                // Check if canvas is initialized
                if (textOverlayCanvas == null)
                {
                    Console.WriteLine("Warning: textOverlayCanvas is null. Overlay refresh skipped.");
                    return;
                }
                
                // Now we're on the UI thread, safe to update UI elements
                
                // Clear canvas
                textOverlayCanvas.Children.Clear();
                
                // Check if Logic is initialized
                if (Logic.Instance == null)
                {
                    Console.WriteLine("Warning: Logic.Instance is null. Cannot refresh text objects.");
                    return;
                }
                
                var textObjects = Logic.Instance.GetTextObjects();
                if (textObjects == null)
                {
                    Console.WriteLine("Warning: Text objects collection is null.");
                    return;
                }
                
                // Re-add all current text objects
                foreach (TextObject textObj in textObjects)
                {
                    if (textObj != null)
                    {
                        // Call our OnTextObjectAdded method to add it to the canvas
                        CreateMonitorOverlayFromTextObject(this, textObj);
                    }
                }

                // Draw exclude regions if enabled
                DrawExcludeRegions();
                
                //UpdateStatus("Text overlays refreshed");
                //Console.WriteLine($"Monitor window refreshed {textOverlayCanvas.Children.Count} text overlays");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing overlays: {ex.Message}");
            }
        }

        // Draw exclude regions on the overlay
        private void DrawExcludeRegions()
        {
            try
            {
                // Check if show exclude regions is enabled
                if (!MainWindow.Instance.GetShowExcludeRegions())
                    return;

                // Get exclude regions from MainWindow
                var excludeRegions = MainWindow.Instance.excludeRegions;
                if (excludeRegions == null || excludeRegions.Count == 0)
                    return;

                // Draw each exclude region
                foreach (Rect region in excludeRegions)
                {
                    // Create a border for the exclude region
                    System.Windows.Shapes.Rectangle excludeRect = new System.Windows.Shapes.Rectangle
                    {
                        Width = region.Width,
                        Height = region.Height,
                        Stroke = System.Windows.Media.Brushes.LimeGreen,
                        StrokeThickness = 2,
                        Fill = System.Windows.Media.Brushes.Transparent,
                        Opacity = 0.5
                    };

                    // Position the rectangle
                    Canvas.SetLeft(excludeRect, region.X);
                    Canvas.SetTop(excludeRect, region.Y);

                    // Add to canvas
                    textOverlayCanvas.Children.Add(excludeRect);
                }

                Console.WriteLine($"Drew {excludeRegions.Count} exclude regions on overlay");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error drawing exclude regions: {ex.Message}");
            }
        }
        
        // Update status message
        private void UpdateStatus(string message)
        {
            // Check if we need to invoke on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                // We're on a background thread, marshal the call to the UI thread
                Dispatcher.Invoke(() => UpdateStatus(message));
                return;
            }
            
            // Now we're on the UI thread, safe to update
            if (statusText != null)
                statusText.Text = message;
        }
        
        // Initialize the translation status timer
        private void InitializeTranslationStatusTimer()
        {
            _translationStatusTimer = new DispatcherTimer();
            _translationStatusTimer.Interval = TimeSpan.FromSeconds(1);
            _translationStatusTimer.Tick += TranslationStatusTimer_Tick;
        }
        
        // Update the translation status timer
        private void TranslationStatusTimer_Tick(object? sender, EventArgs e)
        {
            TimeSpan elapsed = DateTime.Now - _translationStartTime;
            string service = ConfigManager.Instance.GetCurrentTranslationService();
            
            Dispatcher.Invoke(() =>
            {
                translationStatusLabel.Text = $"Waiting for {service}... {elapsed.Minutes:D1}:{elapsed.Seconds:D2}";
            });
        }

        // Show the translation status
        public void ShowTranslationStatus(bool bSettling)
        {

            if (bSettling)
            {
                Dispatcher.Invoke(() =>
                {
                    translationStatusLabel.Text = $"Settling...";
                    if (ConfigManager.Instance.IsShowIconSignalEnabled())
                    {
                        translationStatusBorder.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        translationStatusBorder.Visibility = Visibility.Collapsed;
                    }
                });

                return;
            }


            _translationStartTime = DateTime.Now;
            string service = ConfigManager.Instance.GetCurrentTranslationService();

            Dispatcher.Invoke(() =>
            {
                translationStatusLabel.Text = $"Waiting for {service}... 0:00";
                if (ConfigManager.Instance.IsShowIconSignalEnabled())
                {
                    translationStatusBorder.Visibility = Visibility.Visible;
                }
                else
                {
                    translationStatusBorder.Visibility = Visibility.Collapsed;
                }

                // Start the timer if not already running
                if (_translationStatusTimer == null)
                {
                    InitializeTranslationStatusTimer();
                }

                if (!_translationStatusTimer!.IsEnabled)
                {
                    _translationStatusTimer.Start();
                }
            });
        }

        public void HideOverlay()
        {
            if (imageScrollViewer != null)
            imageScrollViewer.Opacity = 0.0;
        }
        
        public void ShowOverlay()
        {
            if (imageScrollViewer != null)
            imageScrollViewer.Opacity = 1.0;
        }
        
        // Hide the translation status
        public void HideTranslationStatus()
        {
            Dispatcher.Invoke(() =>
            {
                translationStatusBorder.Visibility = Visibility.Collapsed;
                
                // Stop the timer
                if (_translationStatusTimer != null && _translationStatusTimer.IsEnabled)
                {
                    _translationStatusTimer.Stop();
                }
            });
        }
        
        private bool _forceClose = false;

        public void ForceClose()
        {
            _forceClose = true;
            Close();
        }

        // Override closing to hide instead
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_forceClose)
            {
                // Cho phép đóng cửa sổ
                e.Cancel = false;
                Console.WriteLine("Monitor window force closed");
            }
            else
            {
                // Chỉ ẩn cửa sổ thay vì đóng
                e.Cancel = true;
                Hide();
                Console.WriteLine("Monitor window closing operation converted to hide");
            }
        }
        
       
        
        // Handle Enter key press in TextBoxes
        private void TextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (sender == zoomTextBox)
                {
                    ApplyZoomFromTextBox();
                }
                
                // Remove focus from the TextBox
                System.Windows.Input.Keyboard.ClearFocus();
                
                // Mark the event as handled
                e.Handled = true;
            }
        }
        
        // Handle TextChanged event for zoom TextBox
        private void ZoomTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Only check format, but don't apply yet - will apply on LostFocus
            if (int.TryParse(zoomTextBox.Text, out int value))
            {
                // Valid number
                if (value < 10 || value > 1000)
                {
                    // Out of range - highlight but don't change yet
                    zoomTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 100, 100));
                }
                else
                {
                    // Valid range
                    zoomTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64));
                }
            }
            else if (!string.IsNullOrWhiteSpace(zoomTextBox.Text))
            {
                // Invalid number
                zoomTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 100, 100));
            }
        }
        
        // Apply zoom value when focus is lost
        private void ZoomTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyZoomFromTextBox();
        }
        
        // Apply zoom from TextBox value
        private void ApplyZoomFromTextBox()
        {
            if (int.TryParse(zoomTextBox.Text, out int value))
            {
                // Clamp to valid range
                value = Math.Max(10, Math.Min(1000, value));
                
                // Update value and display
                zoomTextBox.Text = value.ToString();
                zoomTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64));
                
                // Apply zoom
                currentZoom = value / 100.0;
                
                // Set the transform on the container to scale both image and overlays together
                ScaleTransform scaleTransform = new ScaleTransform(currentZoom, currentZoom);
                imageContainer.LayoutTransform = scaleTransform;
                
                // Make sure scroll bars are correctly shown after zoom change
                UpdateScrollViewerSettings();
                
                // Update status
                UpdateStatus($"Zoom: {value}%");
                Console.WriteLine($"Zoom level changed to {value}%");
            }
            else
            {
                // Invalid input, revert to 100%
                zoomTextBox.Text = "100";
                zoomTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64));
                
                // Apply default zoom
                currentZoom = 1.0;
                ScaleTransform scaleTransform = new ScaleTransform(currentZoom, currentZoom);
                imageContainer.LayoutTransform = scaleTransform;
                
                // Update status
                UpdateStatus("Zoom reset to default (100%)");
            }
        }
        
       
    }
}