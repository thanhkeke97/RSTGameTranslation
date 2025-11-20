using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Media;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Clipboard = System.Windows.Forms.Clipboard;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using FontFamily = System.Windows.Media.FontFamily;

namespace RSTGameTranslation
{
    public class TextObject
    {
        // Properties
        public string Text { get; set; }
        public string ID { get; set; } = Guid.NewGuid().ToString();  // Initialize with a unique ID
        public string TextTranslated { get; set; } = string.Empty;  // Initialize with empty string
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public SolidColorBrush TextColor { get; set; }
        public SolidColorBrush BackgroundColor { get; set; }
        public UIElement? UIElement { get; set; }
        public TextBlock TextBlock { get; private set; } = null!;  // Will be initialized in CreateUIElement
        public Border Border { get; private set; } = new Border();  // Initialize with a new Border
        public System.Windows.FlowDirection FlowDirection { get; set; } = System.Windows.FlowDirection.LeftToRight;
        // Store the original capture position
        public double CaptureX { get; set; }
        public double CaptureY { get; set; }

        // Audio player for click sound
        private static SoundPlayer? _soundPlayer;

        // Constructor with default parameters
        public TextObject(
            string text = "New text added!",
            double x = 0,
            double y = 0,
            double width = 0,
            double height = 0,
            SolidColorBrush? textColor = null,
            SolidColorBrush? backgroundColor = null,
            double captureX = 0,
            double captureY = 0)
        {
            Text = text;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            TextColor = textColor ?? new SolidColorBrush(ConfigManager.Instance.GetOverlayTextColor());
            BackgroundColor = backgroundColor ?? new SolidColorBrush(ConfigManager.Instance.GetOverlayBackgroundColor()); // Half-transparent black
            CaptureX = captureX;
            CaptureY = captureY;

            // Initialize sound player if not already initialized
            _soundPlayer ??= new SoundPlayer();

            // Create the UI element that will be added to the overlay
            //UIElement = CreateUIElement();
        }

        // Create a UI element with the current properties
        // Public so it can be used by MonitorWindow
        public UIElement CreateUIElement(bool useRelativePosition = true)
        {
            // Create a border for the background
            Border = new Border()
            {
                Background = BackgroundColor,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(0, 0, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand  // Change cursor to hand to indicate clickable
            };
            
            // Position based on useRelativePosition parameter
            if (useRelativePosition)
            {
                // Use Margin for positioning in the parent container
                Border.Margin = new Thickness(X, Y, 0, 0);
            }
            if(ConfigManager.Instance.IsLanguageFontOverrideEnabled())
            {
                TextBlock = new TextBlock()
                {
                    Text = this.Text,
                    Foreground = this.TextColor,
                    FontWeight = FontWeights.Normal,
                    FontSize = 24, // Increased from 18 to 24 for better initial size
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Left, // Changed from Justify for better readability of merged blocks
                    FontStretch = FontStretches.Normal, // Changed from Expanded for better readability
                    FontFamily = new FontFamily(ConfigManager.Instance.GetLanguageFontFamily()),
                    Margin = new Thickness(0), // Increased margin for better text display
                    TextTrimming = TextTrimming.None
                };
            }
            else
            {
                // Create the text block with adjusted properties for better horizontal fill
                TextBlock = new TextBlock()
                {
                    Text = this.Text,
                    Foreground = this.TextColor,
                    FontWeight = FontWeights.Normal,
                    FontSize = 24, // Increased from 18 to 24 for better initial size
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Left, // Changed from Justify for better readability of merged blocks
                    FontStretch = FontStretches.Normal, // Changed from Expanded for better readability
                    FontFamily = new FontFamily("Arial, Noto Sans SC, Noto Sans JP, Noto Sans, Noto Sans KR, Noto Sans Devanagari, MS Gothic, Malgun Gothic, Yu Gothic, Microsoft YaHei, Arial Unicode MS"),
                    Margin = new Thickness(0), // Increased margin for better text display
                    TextTrimming = TextTrimming.None
                };
            }
            

            // Set explicit width and height if provided
            if (Width > 0)
            {
                Border.MaxWidth = Width + 20; // Increased padding for word wrap
                Border.MinWidth = Width; // Increased padding for word wrap
                TextBlock.MaxWidth = Border.MaxWidth - 20;
            }

            if (Height > 0)
            {
                Border.MaxHeight = Height;
            }

            // Add the text block to the border
            Border.Child = TextBlock;

            // Add click event handler
            // Border.MouseLeftButtonDown += Border_MouseLeftButtonDown;
            
            // Add context menu for right-click options
            // Border.ContextMenu = CreateContextMenu();

            // Scale font if needed after rendering
            Border.Loaded += (s, e) => AdjustFontSize(Border, TextBlock);

            return Border;
        }

        // Update the UI element with current properties
        public void UpdateUIElement()
        {
            // Check if we need to run on the UI thread
            if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => UpdateUIElement());
                return;
            }

            //if TextBox is null, create a new one
            if (TextBlock == null)
            {
                CreateUIElement();
            }


            if (TextBlock != null && Border != null)
            {
                if (this.TextTranslated != null && this.TextTranslated.Length > 0)
                {
                    // Use translated text if available
                    TextBlock.Text = this.TextTranslated;
                    TextBlock.FlowDirection = FlowDirection;
                }
                // else
                // {
                //     // Use original text
                //     TextBlock.Text = this.Text;
                // }
                TextBlock.Foreground = this.TextColor;
                Border.Background = this.BackgroundColor;
                // Position at the upper left of the rectangle
                Border.Margin = new Thickness(X, Y, 0, 0);

                if (Width > 0)
                {
                    Border.MaxWidth = Width + 20; // Increased padding for word wrap
                    Border.MinWidth = Width; // Increased padding for word wrap
                    TextBlock.MaxWidth = Border.MaxWidth - 20;
                }

                if (Height > 0)
                {
                    Border.MaxHeight = Height;
                }
                if(ConfigManager.Instance.IsLanguageFontOverrideEnabled())
                {
                    TextBlock.FontFamily = new FontFamily(ConfigManager.Instance.GetLanguageFontFamily());
                }
                else 
                {
                    TextBlock.FontFamily = new FontFamily("Arial, Noto Sans SC, ..."); // List font mặc định
                }

                // Reset font size to default and then adjust if needed
                TextBlock.FontSize = 24; // Increased from 18 to 24 for better initial size
                AdjustFontSize(Border, TextBlock);
            }
        }

        // Static cache for font size calculations
        private static readonly Dictionary<string, double> _fontSizeCache = new Dictionary<string, double>();
        public static void ClearCache()
        {
            _fontSizeCache.Clear();
        }
        
        // Adjust font size to fit within the container using binary search
        private void AdjustFontSize(Border border, TextBlock textBlock)
        {
            try
            {
                // Check if auto sizing is enabled
                if (!ConfigManager.Instance.IsAutoSizeTextBlocksEnabled())
                {
                    // Just set default font size and exit
                    textBlock.FontSize = 24; 
                    textBlock.LayoutTransform = Transform.Identity;
                    return;
                }
                
                // Exit early if dimensions aren't set or text is empty
                if (Width <= 0 || Height <= 0 || string.IsNullOrWhiteSpace(textBlock.Text))
                {
                    textBlock.FontSize = 24;
                    textBlock.LayoutTransform = Transform.Identity;
                    return;
                }

                // Create a cache key
                string cacheKey = $"{textBlock.Text.Length}_{Width}_{Height}_{ConfigManager.Instance.GetLanguageFontSizeMin()}_{ConfigManager.Instance.GetLanguageFontSizeMax()}";
                
                // Check cache
                if (_fontSizeCache.TryGetValue(cacheKey, out double cachedFontSize))
                {
                    textBlock.FontSize = cachedFontSize;
                    textBlock.LayoutTransform = Transform.Identity;
                    return;
                }

                double scaleFactor = 1.0;
                string methodOcr = ConfigManager.Instance.GetOcrMethod();
                if (methodOcr == "PaddleOCR")
                {
                    scaleFactor = 1;
                }
                
                // Get Min/Max from Config
                double minSize = ConfigManager.Instance.GetLanguageFontSizeMin() * scaleFactor;
                double maxSize = ConfigManager.Instance.GetLanguageFontSizeMax() * scaleFactor;
                
                // Safety check: ensure min <= max
                if (minSize > maxSize) 
                {
                    double temp = minSize;
                    minSize = maxSize;
                    maxSize = temp;
                }

                double currentSize = Math.Max(minSize, Math.Min(maxSize, 24 * scaleFactor));
                
                int maxIterations = 8;
                double lastDiff = double.MaxValue;
                bool needsMoreHeight = false;
                bool fitWidth = true;

                for (int i = 0; i < maxIterations; i++)
                {
                    textBlock.FontSize = currentSize;
                    textBlock.Measure(new Size(Width * 0.95, Double.PositiveInfinity));

                    double minRequiredHeight = Height;
                    needsMoreHeight = false;
                    
                    if (textBlock.DesiredSize.Width >= Width * 0.9)
                    {
                        needsMoreHeight = true;
                    }

                    double heightDiff = Math.Abs(textBlock.DesiredSize.Height - minRequiredHeight);
                    bool isWidthOk = textBlock.DesiredSize.Width <= Width;
                    fitWidth = isWidthOk;

                    double currentDiff = heightDiff + (isWidthOk ? 0 : 100);

                    if ((heightDiff < 2 && isWidthOk) || Math.Abs(lastDiff - currentDiff) < 0.5)
                    {
                        break;
                    }

                    lastDiff = currentDiff;

                    // Logic điều chỉnh tăng/giảm
                    if (textBlock.DesiredSize.Height > minRequiredHeight * 0.95 || !isWidthOk)
                    {
                        // Too big
                        maxSize = currentSize;
                        currentSize = (minSize + currentSize) / 2;
                    }
                    else if (textBlock.DesiredSize.Height < minRequiredHeight * 0.85 && isWidthOk)
                    {
                        // Too small
                        minSize = currentSize;
                        currentSize = (currentSize + maxSize) / 2;
                    }
                    else
                    {
                        break;
                    }
                }

                // Adjust border height logic (giữ nguyên)
                if (needsMoreHeight && fitWidth)
                {
                    double requiredHeight = Math.Max(Height * 2, textBlock.DesiredSize.Height + 10);
                    Border.MaxHeight = requiredHeight;
                    textBlock.VerticalAlignment = VerticalAlignment.Top;
                }
                else if (fitWidth)
                {
                    Border.MaxHeight = Math.Max(Height, textBlock.DesiredSize.Height + 5);
                }

                // Finalize
                double finalSize = Math.Max(minSize, Math.Min(maxSize, currentSize));
                textBlock.FontSize = finalSize;
                textBlock.LayoutTransform = Transform.Identity;
                
                // Cache result
                if (_fontSizeCache.Count > 100) _fontSizeCache.Clear();
                _fontSizeCache[cacheKey] = finalSize;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AdjustFontSize: {ex.Message}");
                textBlock.FontSize = 24;
                textBlock.LayoutTransform = Transform.Identity;
            }
        }

        // Handle click event
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border)
            {
                // Copy text to clipboard
                Clipboard.SetText(Text);

                // Play sound
                PlayClickSound();

                // Animate the border to provide visual feedback
                AnimateBorderOnClick(border);

                e.Handled = true;
            }
        }

        // Play a sound when clicked
        private void PlayClickSound()
        {
            // Set the sound file path
            string soundFile = "audio\\clipboard.wav";

            // Check if file exists
            if (System.IO.File.Exists(soundFile))
            {
                _soundPlayer!.SoundLocation = soundFile;
                _soundPlayer.Play();
            }
        }

        // Store original background color for each border to ensure we restore properly
        private static readonly ConditionalWeakTable<Border, SolidColorBrush> _originalBackgrounds = 
            new ConditionalWeakTable<Border, SolidColorBrush>();
            
        // Create the context menu with Copy Source, Copy Translated, Speak Source, and Learn Source options
        private ContextMenu CreateContextMenu()
        {
            ContextMenu contextMenu = new ContextMenu();
            
            // Copy Source menu item
            MenuItem copySourceMenuItem = new MenuItem();
            copySourceMenuItem.Header = "Copy Source";
            copySourceMenuItem.Click += CopySourceMenuItem_Click;
            contextMenu.Items.Add(copySourceMenuItem);
            
            // Copy Translated menu item
            MenuItem copyTranslatedMenuItem = new MenuItem();
            copyTranslatedMenuItem.Header = "Copy Translated";
            copyTranslatedMenuItem.Click += CopyTranslatedMenuItem_Click;
            contextMenu.Items.Add(copyTranslatedMenuItem);
            
            // Add a separator
            contextMenu.Items.Add(new Separator());
            
            // Learn Source menu item
            MenuItem learnSourceMenuItem = new MenuItem();
            learnSourceMenuItem.Header = "Learn Source";
            learnSourceMenuItem.Click += LearnSourceMenuItem_Click;
            contextMenu.Items.Add(learnSourceMenuItem);
            
            // Speak Source menu item
            MenuItem speakSourceMenuItem = new MenuItem();
            speakSourceMenuItem.Header = "Speak Source";
            speakSourceMenuItem.Click += SpeakSourceMenuItem_Click;
            contextMenu.Items.Add(speakSourceMenuItem);
            
            // Update menu item states when context menu is opened
            contextMenu.Opened += (s, e) => {
                copyTranslatedMenuItem.IsEnabled = !string.IsNullOrEmpty(this.TextTranslated);
            };
            
            return contextMenu;
        }
        
        // Click handler for Copy Source menu item
        private void CopySourceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(this.Text);
            PlayClickSound();
        }
        
        // Click handler for Copy Translated menu item
        private void CopyTranslatedMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(this.TextTranslated))
            {
                Clipboard.SetText(this.TextTranslated);
                PlayClickSound();
            }
        }
        
        // Click handler for Learn Source menu item
        private void LearnSourceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(this.Text))
                {
                    // Construct the ChatGPT URL with the selected text and instructions
                    string chatGptPrompt = $"Create a lesson to help me learn about this text and its translation: {this.Text}";
                    string encodedPrompt = System.Web.HttpUtility.UrlEncode(chatGptPrompt);
                    string chatGptUrl = $"https://chat.openai.com/?q={encodedPrompt}";
                    
                    // Open in default browser
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = chatGptUrl,
                        UseShellExecute = true
                    });
                    
                    Console.WriteLine($"Opening ChatGPT with text: {this.Text.Substring(0, Math.Min(50, this.Text.Length))}...");
                }
                else
                {
                    Console.WriteLine("No text available for Learn function");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Learn function: {ex.Message}");
            }
        }
        
        // Click handler for Speak Source menu item
        private async void SpeakSourceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(this.Text))
                {
                    string text = this.Text.Trim();
                    Console.WriteLine($"Speak function called with text: {text.Substring(0, Math.Min(50, text.Length))}...");
                    
                    // Check if TTS is enabled in config
                    if (ConfigManager.Instance.IsTtsEnabled())
                    {
                        string ttsService = ConfigManager.Instance.GetTtsService();
                        
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
                        catch (Exception ex)
                        {
                            Console.WriteLine($"TTS error: {ex.Message}");
                            System.Windows.MessageBox.Show($"Text-to-Speech error: {ex.Message}", 
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    Console.WriteLine("No text available for Speak function");
                    System.Windows.MessageBox.Show("No text available to speak.",
                        "No Text Available", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Speak function: {ex.Message}");
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Text-to-Speech Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        // Animate the border when clicked
        private void AnimateBorderOnClick(Border border)
        {
            try
            {
                // Get or store the original background color
                if (!_originalBackgrounds.TryGetValue(border, out SolidColorBrush? originalBrush))
                {
                    // Only store the original background on first click
                    originalBrush = border.Background.Clone() as SolidColorBrush;
                    if (originalBrush != null)
                    {
                        // Freeze it to ensure it doesn't change
                        originalBrush.Freeze();
                        _originalBackgrounds.Add(border, originalBrush);
                    }
                }

                // Get the color to return to (use stored value, not current which might be mid-animation)
                Color targetColor = originalBrush?.Color ?? ((SolidColorBrush)border.Background).Color;
                
                // Create a new transform for this animation
                var scaleTransform = new ScaleTransform(1.0, 1.0);
                border.RenderTransform = scaleTransform;
                border.RenderTransformOrigin = new Point(0.5, 0.5);

                // Create background color animation with proper completion action
                ColorAnimation colorAnimation = new ColorAnimation()
                {
                    From = Colors.Yellow,
                    To = targetColor,
                    Duration = TimeSpan.FromSeconds(0.3),
                    // Important: Ensure animation completes and doesn't get stuck
                    FillBehavior = FillBehavior.Stop
                };

                // Create scale animation
                DoubleAnimation scaleAnimation = new DoubleAnimation()
                {
                    From = 1.1,
                    To = 1.0,
                    Duration = TimeSpan.FromSeconds(0.2),
                    FillBehavior = FillBehavior.Stop
                };

                // Create a new brush for the animation
                SolidColorBrush animationBrush = new SolidColorBrush(Colors.Yellow);
                
                // When the color animation completes, reset to original background
                colorAnimation.Completed += (s, e) => 
                {
                    border.Background = originalBrush?.Clone() ?? new SolidColorBrush(targetColor);
                };

                // Start animations
                border.Background = animationBrush;
                animationBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Animation error: {ex.Message}");
                // In case of error, try to restore the original background
                if (_originalBackgrounds.TryGetValue(border, out SolidColorBrush? originalBrush) && originalBrush != null)
                {
                    border.Background = originalBrush.Clone();
                }
            }
        }
    }
}