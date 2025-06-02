using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Media;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using FontFamily = System.Windows.Media.FontFamily;
using Brushes = System.Windows.Media.Brushes;
using TextBox = System.Windows.Controls.TextBox;
using WinCursors = System.Windows.Input.Cursors;
using WinForms = System.Windows.Forms;

namespace RSTGameTranslation
{
    public partial class ChatBoxWindow : Window
    {
        // Constants
        private const int MAX_CONTEXT_HISTORY_SIZE = 100; // Max entries to keep for context purposes
        
        // We'll use MainWindow.Instance.translationHistory instead of maintaining our own
        private int _maxHistorySize; // Display history size from config
        private int _displayMode = 0; // 0 = both, 1 = target only, 2 = source only

        public static ChatBoxWindow? Instance { get; private set; }

        // Animation timer for translation status
        private DispatcherTimer? _animationTimer;
        private int _animationStep = 0;
        
        public ChatBoxWindow()
        {
            Instance = this;
            InitializeComponent();
            
            // Register application-wide keyboard shortcut handler
            this.PreviewKeyDown += Application_KeyDown;

            // Get max history size from configuration for display purposes
            _maxHistorySize = ConfigManager.Instance.GetChatBoxHistorySize();
            
            // Initialize the RichTextBox with a properly configured document
            chatHistoryText.Document = new FlowDocument()
            {
                // Set basic document properties
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Left,
                
                // Make sure the document is visible
                IsEnabled = true,
                IsHyphenationEnabled = false,
                
                // Ensure text wraps properly
                PageWidth = 350,
                
                // Standard margins
                PagePadding = new Thickness(5)
            };
            
            // Ensure the document is visible
            chatHistoryText.IsDocumentEnabled = true;
            
            // Set up context menu
            SetupContextMenu();
            
            // Apply custom styling from configuration
            ApplyConfigurationStyling();
            
            // Set up animation timer
            _animationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _animationTimer.Tick += AnimationTimer_Tick;
            
            // Subscribe to events
            this.Loaded += ChatBoxWindow_Loaded;
            this.Closing += ChatBoxWindow_Closing;
            
            // Listen for Logic's translation in progress status
            Logic.Instance.TranslationCompleted += OnTranslationCompleted;
        }
        
        private void SetupContextMenu()
        {
            // Create a context menu
            ContextMenu contextMenu = new ContextMenu();
            
            // Add standard menu items
            MenuItem cutItem = new MenuItem() { Header = "Cut" };
            cutItem.Command = ApplicationCommands.Cut;
            contextMenu.Items.Add(cutItem);
            
            MenuItem copyItem = new MenuItem() { Header = "Copy" };
            copyItem.Command = ApplicationCommands.Copy;
            contextMenu.Items.Add(copyItem);
            
            MenuItem pasteItem = new MenuItem() { Header = "Paste" };
            pasteItem.Command = ApplicationCommands.Paste;
            contextMenu.Items.Add(pasteItem);
            
            // Add a separator
            contextMenu.Items.Add(new Separator());
            
            // Add Learn menu item
            MenuItem learnItem = new MenuItem() { Header = "Learn" };
            learnItem.Click += LearnMenuItem_Click;
            contextMenu.Items.Add(learnItem);
            
            // Add Speak menu item
            MenuItem speakItem = new MenuItem() { Header = "Speak" };
            speakItem.Click += SpeakMenuItem_Click;
            contextMenu.Items.Add(speakItem);
            
            // Assign the context menu to the RichTextBox
            chatHistoryText.ContextMenu = contextMenu;
        }
        
        private void LearnMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get the selected text
                TextRange selectedText = new TextRange(
                    chatHistoryText.Selection.Start, 
                    chatHistoryText.Selection.End);
                
                if (!string.IsNullOrWhiteSpace(selectedText.Text))
                {
                    // Construct the ChatGPT URL with the selected text and instructions
                    string chatGptPrompt = $"Create a lesson to help me learn about this text and its translation: {selectedText.Text}";
                    string encodedPrompt = HttpUtility.UrlEncode(chatGptPrompt);
                    string chatGptUrl = $"https://chat.openai.com/?q={encodedPrompt}";
                    
                    // Open in default browser
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = chatGptUrl,
                        UseShellExecute = true
                    });
                    
                    Console.WriteLine($"Opening ChatGPT with selected text: {selectedText.Text.Substring(0, Math.Min(50, selectedText.Text.Length))}...");
                }
                else
                {
                    Console.WriteLine("No text selected for Learn function");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Learn function: {ex.Message}");
            }
        }
        
        private async void SpeakMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get the selected text
                TextRange selectedText = new TextRange(
                    chatHistoryText.Selection.Start, 
                    chatHistoryText.Selection.End);
                
                if (!string.IsNullOrWhiteSpace(selectedText.Text))
                {
                    string text = selectedText.Text.Trim();
                    Console.WriteLine($"Speak function called with text: {text.Substring(0, Math.Min(50, text.Length))}...");
                    
                    // Check if TTS is enabled in config
                    if (ConfigManager.Instance.IsTtsEnabled())
                    {
                        string ttsService = ConfigManager.Instance.GetTtsService();
                        
                        // Show a small notification that we're processing the speech request
                        var currentCursor = this.Cursor;
                        this.Cursor = WinCursors.Wait;
                        
                        try
                        {
                            bool success = false;
                            
                            if (ttsService == "ElevenLabs")
                            {
                                success = await ElevenLabsService.Instance.SpeakText(text);
                            }
                            else if (ttsService == "Google Cloud TTS")
                            {
                                success = await GoogleTTSService.Instance.SpeakText(text);
                            }
                            else
                            {
                                System.Windows.MessageBox.Show($"Text-to-Speech service '{ttsService}' is not supported yet.",
                                    "Unsupported Service", MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            
                            if (!success)
                            {
                                System.Windows.MessageBox.Show($"Failed to generate speech using {ttsService}. Please check the API key and settings.",
                                    "Text-to-Speech Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                        finally
                        {
                            // Reset cursor back
                            this.Cursor = currentCursor;
                        }
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Text-to-Speech is disabled in settings. Please enable it first.",
                            "TTS Disabled", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    Console.WriteLine("No text selected for Speak function");
                    System.Windows.MessageBox.Show("Please select some text to speak first.",
                        "No Text Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Speak function: {ex.Message}");
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Text-to-Speech Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ChatBoxWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Don't actually close the window, just hide it
            Console.WriteLine("ChatBox window closing intercepted - hiding instead");
            
            // Cancel the closing operation
            e.Cancel = true;
            
            // Hide the window instead
            this.Hide();
            
            // Note: We maintain timer and event subscriptions since the window instance stays alive
        }
        
        public void ApplyConfigurationStyling()
        {
            try
            {
                // Get styling from ConfigManager
                var fontFamily = ConfigManager.Instance.GetChatBoxFontFamily();
                var fontSize = ConfigManager.Instance.GetChatBoxFontSize();
                var fontColor = ConfigManager.Instance.GetChatBoxFontColor();
                var backgroundColor = ConfigManager.Instance.GetChatBoxBackgroundColor();
                var bgOpacity = ConfigManager.Instance.GetChatBoxBackgroundOpacity();
                var windowOpacity = ConfigManager.Instance.GetChatBoxWindowOpacity();
                
                // Apply background color with its opacity to window
                if (bgOpacity <= 0)
                {
                    // Make all backgrounds completely transparent when opacity is 0
                    this.Background = Brushes.Transparent;
                    
                    // Also make the ScrollViewer, RichTextBox and resize grip transparent
                    if (chatScrollViewer != null)
                    {
                        chatScrollViewer.Background = Brushes.Transparent;
                    }
                    if (chatHistoryText != null)
                    {
                        chatHistoryText.Background = Brushes.Transparent;
                    }
                    
                    if (resizeGrip != null)
                    {
                        resizeGrip.Fill = Brushes.Transparent;
                    }
                }
                else
                {
                    // Calculate opacity value that:
                    // - At 0%, stays 0%
                    // - At 50%, is more like true 50%
                    // - At 100%, is actually 100%
                    double scaledOpacity;
                    if (bgOpacity >= 0.95) {
                        // Ensure full opacity when set to maximum
                        scaledOpacity = 1.0;
                    } else {
                        // Use a blend of linear and square-root for intermediate values
                        scaledOpacity = 0.7 * Math.Sqrt(bgOpacity) + 0.3 * bgOpacity;
                    }
                    
                    // Set main window background
                    Color bgColorWithOpacity = Color.FromArgb(
                        (byte)(scaledOpacity * 255), // Full opacity when slider is at 100%
                        backgroundColor.R,
                        backgroundColor.G,
                        backgroundColor.B);
                    this.Background = new SolidColorBrush(bgColorWithOpacity);
                    
                    // Set the RichTextBox background directly to match
                    if (chatHistoryText != null)
                    {
                        chatHistoryText.Background = new SolidColorBrush(Color.FromArgb(
                            (byte)(scaledOpacity * 255),
                            0, 0, 0)); // Black background
                    }
                    
                    // Set resize grip
                    if (resizeGrip != null)
                    {
                        Color gripColor = Color.FromArgb(
                            (byte)(bgOpacity * 128), // Half opacity of background
                            128, 128, 128);          // Gray color
                        resizeGrip.Fill = new SolidColorBrush(gripColor);
                    }
                }
                
                // Apply window opacity
                this.Opacity = windowOpacity;
                
                // Ensure header bar is always visible (at least 50% opacity)
                if (headerBar != null)
                {
                    // Calculate header opacity - always at least 50% opaque
                    byte headerOpacity = (byte)Math.Max(128, (int)(bgOpacity * 255));
                    
                    // Make header always visible
                    Color headerColor = Color.FromArgb(
                        headerOpacity,  // At least 50% opaque
                        0x20, 0x20, 0x20);  // Dark gray
                    headerBar.Background = new SolidColorBrush(headerColor);
                }
                
                // Store values for use when creating text entries
                this.FontFamily = new FontFamily(fontFamily);
                ChatFontSize = fontSize;  // Set the chat-specific font size
                this.Foreground = new SolidColorBrush(fontColor);
                
                // Apply updated styling to existing entries
                UpdateChatHistory();
                
                Console.WriteLine($"Applied ChatBox styling: Font={fontFamily}, Size={fontSize}, Color={fontColor}, BG={backgroundColor}, Window Opacity={windowOpacity}, BG Opacity={bgOpacity}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error applying ChatBox styling: {ex.Message}");
            }
        }
        
        private void OptionsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create options window
                var optionsWindow = new ChatBoxOptionsWindow();
                
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
                    CreateFlashAnimation(optionsButton);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error showing options dialog: {ex.Message}");
            }
        }

        private void ChatBoxWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Position the window based on screen bounds if not already positioned
            if (this.Left == 0 && this.Top == 0)
            {
                var screenWidth = SystemParameters.PrimaryScreenWidth;
                var screenHeight = SystemParameters.PrimaryScreenHeight;
                
                // Default to bottom right corner
                this.Left = screenWidth - this.Width - 20;
                this.Top = screenHeight - this.Height - 40;
            }
            
            // Add SizeChanged event handler for reflowing text when window is resized
            this.SizeChanged += ChatBoxWindow_SizeChanged;
        }
        
        // Handler for application-level keyboard shortcuts
        private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Forward to the central keyboard shortcuts handler
            KeyboardShortcuts.HandleKeyDown(e);
        }
        
        private void ChatBoxWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Reflow text when window size changes
            UpdateChatHistory();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Instead of closing, hide the window (to match behavior with Log window)
            this.Hide();
            
            // The MainWindow will handle setting isChatBoxVisible to false in its event handler
        }
        
        // Store chat font size separately from window fonts
        private double _chatFontSize = 14;  // Default chat font size
        
        public double ChatFontSize
        {
            get { return _chatFontSize; }
            set 
            { 
                _chatFontSize = Math.Max(8, Math.Min(48, value));  // Clamp between 8 and 48
            }
        }
        
        private void FontIncreaseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Increase font size and update
                ChatFontSize += 1;
                UpdateChatHistory();
                
                // Save the new font size to config
                ConfigManager.Instance.SetValue(ConfigManager.CHATBOX_FONT_SIZE, ChatFontSize.ToString());
                ConfigManager.Instance.SaveConfig();
                
                // Create and start the flash animation for visual feedback
                CreateFlashAnimation(fontIncreaseButton);
                
                Console.WriteLine($"Chat font size increased to {ChatFontSize} and saved to config");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error increasing font size: {ex.Message}");
            }
        }
        
        private void FontDecreaseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Decrease font size and update
                ChatFontSize -= 1;
                UpdateChatHistory();
                
                // Save the new font size to config
                ConfigManager.Instance.SetValue(ConfigManager.CHATBOX_FONT_SIZE, ChatFontSize.ToString());
                ConfigManager.Instance.SaveConfig();
                
                // Create and start the flash animation for visual feedback
                CreateFlashAnimation(fontDecreaseButton);
                
                Console.WriteLine($"Chat font size decreased to {ChatFontSize} and saved to config");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error decreasing font size: {ex.Message}");
            }
        }
        
        
        // Create and apply a flash animation for the button
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
                    Color originalColor = currentBrush.Color;
                    
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
        
        // Play the clipboard sound
        private void PlayClipboardSound()
        {
            try
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string soundPath = System.IO.Path.Combine(appDirectory, "audio", "clipboard.wav") ?? "";
                
                if (System.IO.File.Exists(soundPath))
                {
                    var player = new System.Media.SoundPlayer(soundPath);
                    player.Play();
                }
                else
                {
                    Console.WriteLine($"Clipboard sound file not found: {soundPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing clipboard sound: {ex.Message}");
            }
        }

        public void OnTranslationWasAdded(string originalText, string translatedText)
        {
            // Hide translation status indicator if it was visible
            HideTranslationStatus();
            
            // Update UI with existing history
            UpdateChatHistory();
        }
        
        // Handle animation timer tick
        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            // Update the animation step (0, 1, 2, 3)
            _animationStep = (_animationStep + 1) % 4;
            
            // Update dot visibility based on animation step
            if (dot1 != null && dot2 != null && dot3 != null)
            {
                // Reset all dots
                dot1.Opacity = 0.3;
                dot2.Opacity = 0.3;
                dot3.Opacity = 0.3;
                
                // Highlight dots based on animation step
                switch (_animationStep)
                {
                    case 0:
                        dot1.Opacity = 1.0;
                        break;
                    case 1:
                        dot2.Opacity = 1.0;
                        break;
                    case 2:
                        dot3.Opacity = 1.0;
                        break;
                    case 3:
                        // All dots dim
                        break;
                }
            }
        }
        
        // Show translation status indicator with animation
        public void ShowTranslationStatus(bool bSettling)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ShowTranslationStatus(bSettling));
                return;
            }
            
            if (translationStatusPanel != null)
            {
                // Hiển thị panel trạng thái dịch
                translationStatusPanel.Visibility = Visibility.Visible;
                
                // Cập nhật nội dung dựa vào trạng thái settling
                if (bSettling && translationStatusText != null)
                {
                    // Nếu đang trong trạng thái settling, hiển thị thông báo "Settling..."
                    translationStatusText.Text = "Settling...";
                }
                else if (translationStatusText != null)
                {
                    // Nếu đang dịch, hiển thị thông báo dịch với tên dịch vụ
                    string service = ConfigManager.Instance.GetCurrentTranslationService();
                    translationStatusText.Text = $"Waiting for {service}...";
                }
                
                // Bắt đầu animation timer trong mọi trường hợp
                if (_animationTimer != null && !_animationTimer.IsEnabled)
                {
                    _animationStep = 0;
                    _animationTimer.Start();
                }
            }
        }
        
        // Hide translation status indicator
        public void HideTranslationStatus()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => HideTranslationStatus());
                return;
            }
            
            if (translationStatusPanel != null)
            {
                translationStatusPanel.Visibility = Visibility.Collapsed;
                
                // Stop the animation timer
                if (_animationTimer != null && _animationTimer.IsEnabled)
                {
                    _animationTimer.Stop();
                }
            }
        }
        
        // Handle translation completed event
        private void OnTranslationCompleted(object? sender, TranslationEventArgs e)
        {
            // Hide the translation status indicator
            HideTranslationStatus();
        }
        
        // Get recent original texts for context
        public List<string> GetRecentOriginalTexts(int maxCount, int minContextSize)
        {
            var result = new List<string>();
            
            // Get access to MainWindow's translation history
            var mainWindowHistory = MainWindow.Instance.GetTranslationHistory();
            
            // If no history or count is zero, return empty list
            if (mainWindowHistory.Count == 0 || maxCount <= 0)
            {
                return result;
            }
            
            // Copy the queue to a list so we can access by index, most recent first
            var historyList = mainWindowHistory.Reverse().ToList();
            int collected = 0;
            
            // Collect entries until we have the requested number
            for (int i = 0; i < historyList.Count && collected < maxCount; i++)
            {
                if (!string.IsNullOrEmpty(historyList[i].OriginalText))
                {
                    if (historyList[i].OriginalText.Length >= minContextSize)
                    {
                        result.Add(historyList[i].OriginalText);
                        collected++;
                    }
                }
            }
            
            // Reverse the list so older entries come first (chronological order)
            result.Reverse();
            
            return result;
        }

        private void ModeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Cycle through display modes: 0 (both) -> 1 (target only) -> 2 (source only) -> 0 (both)
                _displayMode = (_displayMode + 1) % 3;
                if (_displayMode == 0)
                {
                    // Both
                    modeButton.Content = "Source&Translated Text";
                }
                else if (_displayMode == 1)
                {
                    // Target only
                    modeButton.Content = "Translated Text";
                }
                else if (_displayMode == 2)
                {
                    // Source only
                    modeButton.Content = "Source Text";
                }
                
                // Update the UI
                UpdateChatHistory();
                
                // Create and start the flash animation for visual feedback
                CreateFlashAnimation(modeButton);
                
                // string[] modes = { "both languages", "target language only", "source language only" };
                Console.WriteLine($"Display mode changed to: {modeButton.Content}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error changing display mode: {ex.Message}");
            }
        }
        
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Clear the translation history queue in MainWindow
                MainWindow.Instance.ClearTranslationHistory();
                
                // Update the UI to show empty history
                UpdateChatHistory();
                
                // Create and start the flash animation for visual feedback
                CreateFlashAnimation(clearButton);
                
                Console.WriteLine("Translation history cleared");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing translation history: {ex.Message}");
            }
        }

        public void UpdateChatHistory()
        {
            // Only update if window is visible
            if (!this.IsVisible)
                return;
                
            // Run on UI thread
            this.Dispatcher.Invoke(() => 
            {
                // Get styling from window's properties and config
                var fontFamily = this.FontFamily;
                var fontSize = this.ChatFontSize;
                var originalTextColor = ConfigManager.Instance.GetOriginalTextColor();
                var translatedTextColor = ConfigManager.Instance.GetTranslatedTextColor();
                var bgOpacity = ConfigManager.Instance.GetChatBoxBackgroundOpacity();
                
                // Clear existing content
                chatHistoryText.Document.Blocks.Clear();
                
                // Set up document properties to enable text wrapping
                // PageWidth should match the viewport width of the ScrollViewer (minus padding)
                // Subtract extra pixels to ensure text doesn't get too close to the scrollbar
                chatHistoryText.Document.PageWidth = chatScrollViewer.ActualWidth > 0 
                    ? (chatScrollViewer.ActualWidth - 30) : 320;
                
                // Set the background opacity
                if (bgOpacity <= 0)
                {
                    chatHistoryText.Background = Brushes.Transparent;
                }
                else
                {
                    // Calculate opacity value with our scaling formula
                    double scaledOpacity;
                    if (bgOpacity >= 0.95) {
                        scaledOpacity = 1.0;
                    } else {
                        scaledOpacity = 0.7 * Math.Sqrt(bgOpacity) + 0.3 * bgOpacity;
                    }
                    
                    // Apply the background color to the ScrollViewer instead of RichTextBox
                    chatScrollViewer.Background = new SolidColorBrush(Color.FromArgb(
                        (byte)(scaledOpacity * 255),
                        0, 0, 0));
                    chatHistoryText.Background = Brushes.Transparent;
                }
                
                // Get the history from MainWindow
                var mainWindowHistory = MainWindow.Instance.GetTranslationHistory();
                
                // Get only the most recent entries for display (based on _maxHistorySize)
                var displayHistory = mainWindowHistory.Reverse().Take(_maxHistorySize).Reverse();
                
                // Get Min ChatBox Text Size setting
                int minChatBoxTextSize = ConfigManager.Instance.GetChatBoxMinTextSize();
                
                // Create a paragraph for each entry to display
                foreach (var entry in displayHistory)
                {
                    // Skip entries with source text smaller than minimum size
                    if (!string.IsNullOrEmpty(entry.OriginalText) && entry.OriginalText.Length < minChatBoxTextSize)
                    {
                        continue;
                    }
                    
                    // Create a new paragraph for this entry
                    Paragraph para = new Paragraph();
                    
                    // Set paragraph properties - regular margins now that scrollbar is outside
                    para.Margin = new Thickness(5, 10, 5, 10); // Add vertical spacing
                    para.TextIndent = 0;
                    para.LineHeight = Double.NaN; // Use default line height
                    
                    // Add original text based on display mode
                    if ((_displayMode == 0 || _displayMode == 2) && !string.IsNullOrEmpty(entry.OriginalText))
                    {
                        // Create a run for the original text
                        Run originalRun = new Run(entry.OriginalText);
                        
                        // Format it appropriately
                        originalRun.Foreground = new SolidColorBrush(originalTextColor);
                        originalRun.FontFamily = fontFamily;
                        originalRun.FontSize = Math.Max(fontSize - 2, 10);
                        
                        // Add it to the paragraph
                        para.Inlines.Add(originalRun);
                        
                        // Add line breaks if there's also translated text to be shown
                        if ((_displayMode == 0) && !string.IsNullOrEmpty(entry.TranslatedText))
                        {
                            para.Inlines.Add(new LineBreak());
                            para.Inlines.Add(new LineBreak());
                        }
                    }
                    
                    // Add translated text based on display mode
                    if ((_displayMode == 0 || _displayMode == 1) && !string.IsNullOrEmpty(entry.TranslatedText))
                    {
                        // Create a run for the translated text
                        Run translatedRun = new Run(entry.TranslatedText);
                        
                        // Format it appropriately
                        translatedRun.Foreground = new SolidColorBrush(translatedTextColor);
                        translatedRun.FontFamily = fontFamily;
                        translatedRun.FontSize = fontSize;
                        translatedRun.FontWeight = FontWeights.SemiBold;
                        
                        // Add it to the paragraph
                        para.Inlines.Add(translatedRun);
                    }
                    
                    // Add the paragraph to the document
                    chatHistoryText.Document.Blocks.Add(para);
                    
                    // Debug output
                  //  Console.WriteLine($"Added entry: {para.Inlines.Count} inlines, " +
                    //    $"Orig: {(entry.OriginalText?.Length ?? 0)} chars, " + 
                      //  $"Trans: {(entry.TranslatedText?.Length ?? 0)} chars");
                }
                
                // Scroll to the bottom to see newest entries
                chatScrollViewer.ScrollToEnd();
            });
        }
    }

    public class TranslationEntry
    {
        public string OriginalText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}