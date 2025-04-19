using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;


namespace UGTLive
{
    public partial class MonitorWindow : Window
    {
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
        
        // Private field to store settle time in seconds
        private double _settleTimeSeconds = 0.5; // Default to 0.5 seconds
        
        public MonitorWindow()
        {
            // Make sure the initialization flag is set before anything else
            _isInitializing = true;
            Console.WriteLine("MonitorWindow constructor: Setting _isInitializing to true");
            
            InitializeComponent();
            
            Console.WriteLine("MonitorWindow constructor started");
            
            // Subscribe to TextObject events from Logic
            //Logic.Instance.TextObjectAdded += CreateMonitorOverlayFromTextObject;
            
            // Set initial status
            UpdateStatus("Ready");
            
            // Load block detection power and settle time from BlockDetectionManager
            // These values are already loaded from config in BlockDetectionManager's constructor
            _settleTimeSeconds = BlockDetectionManager.Instance.GetSettleTime();
            
            Console.WriteLine($"Loaded block detection power: {BlockDetectionManager.Instance.GetBlockDetectionScale()}");
            Console.WriteLine($"Loaded settle time: {_settleTimeSeconds} seconds");
            
            // Set textbox values - don't trigger the TextChanged events for these initial settings
            blockDetectionPowerTextBox.TextChanged -= BlockDetectionPowerTextBox_TextChanged;
            settleTimeTextBox.TextChanged -= SettleTimeTextBox_TextChanged;
            
            blockDetectionPowerTextBox.Text = BlockDetectionManager.Instance.GetBlockDetectionScale().ToString();
            settleTimeTextBox.Text = _settleTimeSeconds.ToString();
            
            // Reattach event handlers
            blockDetectionPowerTextBox.TextChanged += BlockDetectionPowerTextBox_TextChanged;
            settleTimeTextBox.TextChanged += SettleTimeTextBox_TextChanged;
            
            // Add loaded event handler
            this.Loaded += MonitorWindow_Loaded;
            
            // Add size changed handler to update scrollbars
            this.SizeChanged += MonitorWindow_SizeChanged;
            
            // Manually connect events (to ensure we have control over when they're attached)
            ocrMethodComboBox.SelectionChanged += OcrMethodComboBox_SelectionChanged;
            autoTranslateCheckBox.Checked += AutoTranslateCheckBox_CheckedChanged;
            autoTranslateCheckBox.Unchecked += AutoTranslateCheckBox_CheckedChanged;
            blockDetectionPowerTextBox.TextChanged += BlockDetectionPowerTextBox_TextChanged;
            blockDetectionPowerTextBox.LostFocus += BlockDetectionPowerTextBox_LostFocus;
            
            // Add event handlers for the zoom TextBox
            zoomTextBox.TextChanged += ZoomTextBox_TextChanged;
            zoomTextBox.LostFocus += ZoomTextBox_LostFocus;
            
            // Add event handlers for the settle time TextBox
            settleTimeTextBox.TextChanged += SettleTimeTextBox_TextChanged;
            settleTimeTextBox.LostFocus += SettleTimeTextBox_LostFocus;
            
            // Add KeyDown event handlers for TextBoxes to handle Enter key
            blockDetectionPowerTextBox.KeyDown += TextBox_KeyDown;
            zoomTextBox.KeyDown += TextBox_KeyDown;
            settleTimeTextBox.KeyDown += TextBox_KeyDown;

            SocketManager.Instance.ConnectionChanged += OnSocketConnectionChanged;


            // Set default size if not already set
            if (this.Width == 0)
                this.Width = 600;
            if (this.Height == 0)
                this.Height = 500;
                
            Console.WriteLine("MonitorWindow constructor completed");
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
            if (ocrMethod == "Windows OCR")
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
                // Using EasyOCR, check connection status first
                _ = Task.Run(async () => 
                {
                   
                        Console.WriteLine("Switching to EasyOCR, checking socket connection...");

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
            
            // Try to load the last screenshot if available
            if (!string.IsNullOrEmpty(lastImagePath) && File.Exists(lastImagePath))
            {
                UpdateScreenshot(lastImagePath);
            }
            
            // Initialize controls from MainWindow
            if (MainWindow.Instance != null)
            {
                // Get OCR method from config
                string ocrMethod = ConfigManager.Instance.GetOcrMethod();
                Console.WriteLine($"MonitorWindow_Loaded: Loading OCR method from config: '{ocrMethod}'");
                
                // Temporarily remove the event handler to prevent triggering
                // a new connection while initializing
                ocrMethodComboBox.SelectionChanged -= OcrMethodComboBox_SelectionChanged;
                
                // Find the matching ComboBoxItem
                bool foundMatch = false;
                foreach (ComboBoxItem comboItem in ocrMethodComboBox.Items)
                {
                    string itemText = comboItem.Content.ToString() ?? "";
                    Console.WriteLine($"Comparing OCR method: '{itemText}' with config value: '{ocrMethod}'");
                    
                    if (string.Equals(itemText, ocrMethod, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Found matching OCR method: '{itemText}'");
                        ocrMethodComboBox.SelectedItem = comboItem;
                        foundMatch = true;
                        break;
                    }
                }
                
                if (!foundMatch)
                {
                    Console.WriteLine($"WARNING: Could not find OCR method '{ocrMethod}' in ComboBox. Available items:");
                    foreach (ComboBoxItem listItem in ocrMethodComboBox.Items)
                    {
                        Console.WriteLine($"  - '{listItem.Content}'");
                    }
                }
                
                // Log what we actually set
                if (ocrMethodComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    Console.WriteLine($"OCR ComboBox is now set to: '{selectedItem.Content}'");
                }
                
                // Re-attach the event handler
                ocrMethodComboBox.SelectionChanged += OcrMethodComboBox_SelectionChanged;
                
                // Make sure MainWindow has the same OCR method
                if (ocrMethodComboBox.SelectedItem is ComboBoxItem selectedComboItem)
                {
                    string selectedOcrMethod = selectedComboItem.Content.ToString() ?? "";
                    MainWindow.Instance.SetOcrMethod(selectedOcrMethod);
                }
                
                // Get auto-translate state from MainWindow
                bool isTranslateEnabled = MainWindow.Instance.GetTranslateEnabled();
                
                // Initialization complete, now we can save settings changes
                _isInitializing = false;
                Console.WriteLine("MonitorWindow initialization complete. Settings changes will now be saved.");
                
                // Force the OCR method to match the config again
                // This ensures the config value is preserved and not overwritten
                string configOcrMethod = ConfigManager.Instance.GetOcrMethod();
                Console.WriteLine($"Ensuring config OCR method is preserved: {configOcrMethod}");
                
                // Now that initialization is complete, save the OCR method from config
                ConfigManager.Instance.SetOcrMethod(configOcrMethod);
                
                // Temporarily remove event handler
                autoTranslateCheckBox.Checked -= AutoTranslateCheckBox_CheckedChanged;
                autoTranslateCheckBox.Unchecked -= AutoTranslateCheckBox_CheckedChanged;
                
                autoTranslateCheckBox.IsChecked = isTranslateEnabled;
                
                // Re-attach event handlers
                autoTranslateCheckBox.Checked += AutoTranslateCheckBox_CheckedChanged;
                autoTranslateCheckBox.Unchecked += AutoTranslateCheckBox_CheckedChanged;
            }
            
            Console.WriteLine("MonitorWindow initialization complete");
        }
        
        private void MonitorWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Update scrollbars when window size changes
            UpdateScrollViewerSettings();
        }
        
        // Update the monitor with a bitmap directly (no file saving required)
        public void UpdateScreenshotFromBitmap(System.Drawing.Bitmap bitmap)
        {
            if (!Dispatcher.CheckAccess())
            {
                // Convert to BitmapSource on the calling thread to avoid UI thread bottleneck
                BitmapSource bitmapSource;
                try
                {
                    // Create a BitmapSource directly from the Bitmap handle - much faster than memory stream
                    IntPtr hBitmap = bitmap.GetHbitmap();
                    try
                    {
                        bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        
                        // Freeze for cross-thread use
                        bitmapSource.Freeze();
                    }
                    finally
                    {
                        // Always delete the HBitmap to prevent memory leaks
                        DeleteObject(hBitmap);
                    }
                    
                    // Use BeginInvoke with high priority for UI update
                    Dispatcher.BeginInvoke(new Action(() => {
                        try
                        {
                            captureImage.Source = bitmapSource;
                            
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
                // Convert bitmap to BitmapSource - faster than BitmapImage
                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    BitmapSource bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    
                    // Set the image source
                    captureImage.Source = bitmapSource;
                }
                finally
                {
                    // Always delete the HBitmap to prevent memory leaks
                    DeleteObject(hBitmap);
                }
                
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
        
        // P/Invoke call needed for proper HBitmap cleanup
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
        
        // Update the monitor with a new screenshot from file
        public void UpdateScreenshot(string imagePath)
        {
            if (!File.Exists(imagePath)) 
            {
                UpdateStatus($"File not found: {imagePath}");
                return;
            }
            
            try
            {
                lastImagePath = imagePath;
                
                // Get the absolute file path (fully qualified)
                string fullPath = Path.GetFullPath(imagePath);
                
                // Make a copy of the file to avoid access conflicts
                string tempCopyPath = Path.Combine(Path.GetTempPath(), 
                                                 $"monitor_copy_{Guid.NewGuid()}.png");
                
                // Copy the file to a temporary location
                try
                {
                    File.Copy(fullPath, tempCopyPath, true);
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"Error copying image file: {ex.Message}");
                    // Continue with the original file if copy fails
                    tempCopyPath = fullPath;
                }
                
                // Load the image using a FileStream to avoid URI issues
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                
                // Use a FileStream instead of UriSource
                try
                {
                    using (FileStream stream = new FileStream(tempCopyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze(); // Make it thread-safe and more efficient
                    }
                    
                    // Ensure we're on the UI thread when updating the image source
                    if (!Dispatcher.CheckAccess())
                    {
                        Dispatcher.Invoke(() => { captureImage.Source = bitmap; });
                    }
                    else
                    {
                        captureImage.Source = bitmap;
                    }
                    
                    // Clean up temp file if it's not the original
                    if (tempCopyPath != fullPath && File.Exists(tempCopyPath))
                    {
                        try
                        {
                            File.Delete(tempCopyPath);
                        }
                        catch
                        {
                            // Ignore deletion errors - temp files will be cleaned up by the OS eventually
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading image: {ex.Message}");
                    UpdateStatus($"Error loading image: {ex.Message}");
                }
                
                // Clear existing overlay elements
                //textOverlayCanvas.Children.Clear();
                
                // Ensure UI updates happen on the UI thread
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => {
                        // Show the window if not visible
                        if (!IsVisible)
                        {
                            Show();
                            Console.WriteLine("Monitor window shown during screenshot update");
                        }
                        
                        // Make sure scroll bars appear when needed
                        UpdateScrollViewerSettings();
                    });
                }
                else
                {
                    // Show the window if not visible
                    if (!IsVisible)
                    {
                        Show();
                        Console.WriteLine("Monitor window shown during screenshot update");
                    }
                    
                    // Make sure scroll bars appear when needed
                    UpdateScrollViewerSettings();
                }
                
                //UpdateStatus($"Screenshot updated: {Path.GetFileName(imagePath)}");
                //Console.WriteLine($"Monitor window updated with screenshot: {Path.GetFileName(imagePath)}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}");
                Console.WriteLine($"Error updating monitor: {ex.Message}");
            }
        }
        
        // Ensure scroll bars appear when needed
        private void UpdateScrollViewerSettings()
        {
            if (captureImage.Source != null)
            {
                // Make sure the image and canvas are sized correctly
                if (captureImage.Source is BitmapSource bitmapSource)
                {
                    // Set the canvas size to match the image
                    textOverlayCanvas.Width = bitmapSource.PixelWidth;
                    textOverlayCanvas.Height = bitmapSource.PixelHeight;
                    
                    // This ensures the scrollbars will appear when the image is larger
                    // than the available space in the ScrollViewer
                    imageContainer.Width = bitmapSource.PixelWidth;
                    imageContainer.Height = bitmapSource.PixelHeight;
                }
            }
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
                    
                    // Position the element on the canvas using Canvas.SetLeft/Top
                    Canvas.SetLeft(textObject.Border, textObject.X);
                    Canvas.SetTop(textObject.Border, textObject.Y);
                    
                    // Add to canvas
                    textOverlayCanvas.Children.Add(textObject.Border);
                    
                    // Add additional status update when text is copied
                    textObject.Border.MouseLeftButtonDown += (s, e) => {
                        UpdateStatus("Text copied to clipboard");
                    };
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
                
                //UpdateStatus("Text overlays refreshed");
                //Console.WriteLine($"Monitor window refreshed {textOverlayCanvas.Children.Count} text overlays");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing overlays: {ex.Message}");
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
        private void TranslationStatusTimer_Tick(object sender, EventArgs e)
        {
            TimeSpan elapsed = DateTime.Now - _translationStartTime;
            string service = ConfigManager.Instance.GetCurrentTranslationService();
            
            Dispatcher.Invoke(() =>
            {
                translationStatusLabel.Text = $"Waiting for {service}... {elapsed.Minutes:D1}:{elapsed.Seconds:D2}";
            });
        }
        
        // Show the translation status
        public void ShowTranslationStatus()
        {
            _translationStartTime = DateTime.Now;
            string service = ConfigManager.Instance.GetCurrentTranslationService();
            
            Dispatcher.Invoke(() =>
            {
                translationStatusLabel.Text = $"Waiting for {service}... 0:00";
                translationStatusBorder.Visibility = Visibility.Visible;
                
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
        
        // Override closing to hide instead
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;  // Cancel the close
            Hide();           // Hide the window instead
            Console.WriteLine("Monitor window closing operation converted to hide");
        }
        
        // Validate block detection power input and update when changed
        private void BlockDetectionPowerTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Only check format, but don't apply yet - will apply on LostFocus
            if (double.TryParse(blockDetectionPowerTextBox.Text, out double value))
            {
                // Valid number
                if (value < 0.1 || value > 1000)
                {
                    // Out of range - highlight but don't change yet
                    blockDetectionPowerTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 100, 100));
                }
                else
                {
                    // Valid range
                    blockDetectionPowerTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64));
                }
            }
            else if (!string.IsNullOrWhiteSpace(blockDetectionPowerTextBox.Text))
            {
                // Invalid number
                blockDetectionPowerTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 100, 100));
            }
        }
        
        // Handle Enter key press in TextBoxes
        private void TextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                // Handle the Enter key press for each TextBox
                if (sender == blockDetectionPowerTextBox)
                {
                    ApplyBlockDetectionPower();
                }
                else if (sender == zoomTextBox)
                {
                    ApplyZoomFromTextBox();
                }
                else if (sender == settleTimeTextBox)
                {
                    ApplySettleTime();
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
        
        // Apply block detection power value when focus is lost
        private void BlockDetectionPowerTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyBlockDetectionPower();
        }
        
        // Handle TextChanged event for settle time TextBox
        private void SettleTimeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Only check format, but don't apply yet - will apply on LostFocus
            if (double.TryParse(settleTimeTextBox.Text, out double value))
            {
                // Valid number
                if (value < 0 || value > 60)
                {
                    // Out of range - highlight but don't change yet
                    settleTimeTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 100, 100));
                }
                else
                {
                    // Valid range
                    settleTimeTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64));
                }
            }
            else if (!string.IsNullOrWhiteSpace(settleTimeTextBox.Text))
            {
                // Invalid number
                settleTimeTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 100, 100));
            }
        }
        
        // Apply settle time when focus is lost
        private void SettleTimeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplySettleTime();
        }
        
        // Apply settle time from TextBox value
        private void ApplySettleTime()
        {
            // Skip if initializing to prevent setting defaults over existing values
            if (_isInitializing)
            {
                Console.WriteLine("Skipping settle time change during initialization");
                return;
            }
            
            if (double.TryParse(settleTimeTextBox.Text, out double value))
            {
                // Clamp to valid range
                value = Math.Max(0, Math.Min(60, value));
                
                // Update value and display
                settleTimeTextBox.Text = value.ToString();
                settleTimeTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64));
                
                // Store the value
                _settleTimeSeconds = value;
                
                // Apply to block detection manager (this will save to config)
                BlockDetectionManager.Instance.SetSettleTime(value);
                
                // Update status
                UpdateStatus($"Settle time set to {value} seconds");
                
                Console.WriteLine($"Settle time set to {value} seconds");
            }
            else
            {
                // Invalid input, revert to default
                settleTimeTextBox.Text = "0";
                settleTimeTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64));
                
                // Store default value
                _settleTimeSeconds = 0;
                
                // Update status
                UpdateStatus("Settle time reset to default (0 seconds)");
            }
        }
        
        // Public getter for settle time
        public double GetSettleTimeSeconds()
        {
            return _settleTimeSeconds;
        }
        
        // Public setter for settle time
        public void SetSettleTimeSeconds(double seconds)
        {
            // Ensure we're on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetSettleTimeSeconds(seconds));
                return;
            }
            
            // Clamp and set the value
            _settleTimeSeconds = Math.Max(0, Math.Min(60, seconds));
            settleTimeTextBox.Text = _settleTimeSeconds.ToString();
            
            // Update UI (background color will be handled by the TextChanged event)
        }
        
        // Apply block detection power value
        private void ApplyBlockDetectionPower()
        {
            // Skip if initializing to prevent setting defaults over existing values
            if (_isInitializing)
            {
                Console.WriteLine("Skipping block detection power change during initialization");
                return;
            }
            
            if (double.TryParse(blockDetectionPowerTextBox.Text, out double value))
            {
                // Clamp to valid range
                value = Math.Max(0.1, Math.Min(1000, value));
                
                // Update value and display
                blockDetectionPowerTextBox.Text = value.ToString();
                blockDetectionPowerTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64));
                
                // Apply to block detection manager (this will save to config)
                BlockDetectionManager.Instance.SetBlockDetectionScale(value);
                
                // Update status
                UpdateStatus($"Block detection power set to {value}");
                
                // Clear any existing text objects and reset hash to force re-rendering
                Logic.Instance.ClearAllTextObjects();
                Logic.Instance.ResetHash();
              
            }
            else
            {
                // Invalid input, revert to default
                blockDetectionPowerTextBox.Text = "5";
                blockDetectionPowerTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64));
                
                // Apply default value
                BlockDetectionManager.Instance.SetBlockDetectionScale(5);
                
                // Update status
                UpdateStatus("Block detection power reset to default (5)");
            }
        }
    }
}