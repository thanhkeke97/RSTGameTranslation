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
    public struct TranslationAreaInfo
    {
        public Rect Bounds { get; set; }
        public int ScreenIndex { get; set; }
        public double DpiScaleX { get; set; }
        public double DpiScaleY { get; set; }
        
        public TranslationAreaInfo(Rect bounds, int screenIndex, double dpiScaleX, double dpiScaleY)
        {
            Bounds = bounds;
            ScreenIndex = screenIndex;
            DpiScaleX = dpiScaleX;
            DpiScaleY = dpiScaleY;
        }
        
        public double X => Bounds.X;
        public double Y => Bounds.Y;
        public double Width => Bounds.Width;
        public double Height => Bounds.Height;
    }
    
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

        // Width the translation may wrap at; the overlay layout pass widens it into free space
        public double LayoutWidth { get; private set; }
        // Lowest font size allowed, as a fraction of the estimated source font size
        public double MinSourceSizeRatio { get; private set; } = DefaultMinSourceSizeRatio;
        // Whether the text fit the original box height at the chosen font size
        public bool TextFitsBox { get; private set; } = true;

        // Feather width (DIPs) of the background edge when overlay colors are sampled automatically
        private const double SoftEdgeRadius = 6;
        // Glow around translated text drawn directly on the erased background
        private const double TextHaloRadius = 6;
        private System.Windows.Shapes.Rectangle? _softBackground;

        // Fallback chain used when no language font override is set
        private const string DefaultFontFamily = "Arial, Noto Sans SC, Noto Sans JP, Noto Sans, Noto Sans KR, Noto Sans Devanagari, MS Gothic, Malgun Gothic, Yu Gothic, Microsoft YaHei, Arial Unicode MS";

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
                    FontFamily = new FontFamily(DefaultFontFamily),
                    Margin = new Thickness(0), // Increased margin for better text display
                    TextTrimming = TextTrimming.None
                };
            }
            

            ResetLayout();
            ApplyBoxConstraints();

            // Background layer under the text, so its edges can be feathered without blurring the text
            _softBackground = new System.Windows.Shapes.Rectangle
            {
                // Extend past the box by the blur radius so the box itself stays fully covered
                Margin = new Thickness(-SoftEdgeRadius),
                Effect = new System.Windows.Media.Effects.BlurEffect { Radius = SoftEdgeRadius },
                IsHitTestVisible = false
            };
            var layers = new Grid();
            layers.Children.Add(_softBackground);
            layers.Children.Add(TextBlock);
            Border.Child = layers;
            ApplyBackground();

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
                ApplyBackground();
                // The overlay positions borders with Canvas.Left/Top; a margin would add the offset twice
                Border.Margin = new Thickness(0);
                Canvas.SetLeft(Border, X);
                Canvas.SetTop(Border, Y);

                // New text: start again from the original box; the overlay layout pass re-widens if needed
                ResetLayout();
                ApplyBoxConstraints();
                if(ConfigManager.Instance.IsLanguageFontOverrideEnabled())
                {
                    TextBlock.FontFamily = new FontFamily(ConfigManager.Instance.GetLanguageFontFamily());
                }
                else 
                {
                    TextBlock.FontFamily = new FontFamily(DefaultFontFamily);
                }

                // Reset font size to default and then adjust if needed
                TextBlock.FontSize = 24; // Increased from 18 to 24 for better initial size
                AdjustFontSize(Border, TextBlock);
            }
        }

        // Original background with the source text removed, placed on the overlay under all translations
        public System.Windows.Controls.Image? EraserElement { get; private set; }
        private Vector _eraserOffset;

        public void SetErasedBackground(ImageSource patch, Rect bounds)
        {
            EraserElement = new System.Windows.Controls.Image
            {
                Source = patch,
                Width = bounds.Width,
                Height = bounds.Height,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };
            // Relative to the text position, so it follows when text objects are shifted
            _eraserOffset = new Vector(bounds.X - X, bounds.Y - Y);
        }

        // The eraser stays over the original text even when the translation box is moved by the layout pass
        public void PositionEraser()
        {
            if (EraserElement == null)
                return;
            Canvas.SetLeft(EraserElement, X + _eraserOffset.X);
            Canvas.SetTop(EraserElement, Y + _eraserOffset.Y);
        }

        // With the original text erased, the translation needs no box: only a halo in the background
        // color keeps it legible over busy art. Otherwise sampled colors get soft edges, and a
        // user-chosen color keeps a crisp box.
        private void ApplyBackground()
        {
            bool erased = EraserElement != null;
            bool soft = !erased && _softBackground != null && ConfigManager.Instance.IsAutoSetOverlayBackground();
            Border.Background = erased || soft ? null : BackgroundColor;
            if (_softBackground != null)
            {
                _softBackground.Fill = BackgroundColor;
                _softBackground.Visibility = soft ? Visibility.Visible : Visibility.Collapsed;
            }

            TextBlock.Effect = erased
                ? new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = BackgroundColor.Color,
                    ShadowDepth = 0,
                    BlurRadius = TextHaloRadius,
                    Opacity = 1
                }
                : null;
        }

        private void ResetLayout()
        {
            LayoutWidth = Width;
            MinSourceSizeRatio = DefaultMinSourceSizeRatio;
        }

        private void ApplyBoxConstraints()
        {
            if (Width > 0)
            {
                // Cover at least the original text; wrap at the (possibly widened) layout width
                Border.MinWidth = Width;
                Border.MaxWidth = LayoutWidth + 20;
                TextBlock.MaxWidth = LayoutWidth;
            }

            if (Height > 0)
            {
                // Set MinHeight to ensure background covers original area
                Border.MinHeight = Height;
                // Allow Border to grow taller if text wraps to multiple lines
                Border.MaxHeight = double.PositiveInfinity;
            }
        }

        // Re-fit the text with a wider wrap width and/or a lower minimum font size (used by the overlay layout pass)
        public void Relayout(double layoutWidth, double minSourceSizeRatio)
        {
            if (TextBlock == null) return;
            LayoutWidth = Math.Max(Width, layoutWidth);
            MinSourceSizeRatio = minSourceSizeRatio;
            ApplyBoxConstraints();
            AdjustFontSize(Border, TextBlock);
        }

        // Static cache for font size calculations
        private static readonly Dictionary<string, (double Size, bool Fits)> _fontSizeCache = new Dictionary<string, (double Size, bool Fits)>();
        public static void ClearCache()
        {
            _fontSizeCache.Clear();
        }
        
        // Line spacing as a multiple of font size; WPF's default (~1.3x) wastes space in small boxes
        private const double LineHeightRatio = 1.15;
        // The translation may shrink to this fraction of the estimated source font size, no smaller
        public const double DefaultMinSourceSizeRatio = 0.6;

        private static void ApplyFontSize(TextBlock textBlock, double fontSize)
        {
            textBlock.FontSize = fontSize;
            textBlock.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            textBlock.LineHeight = fontSize * LineHeightRatio;
            textBlock.LayoutTransform = Transform.Identity;
        }

        // Whether the text, wrapped at the box width, fits inside the box at this font size
        private bool FitsAt(TextBlock textBlock, double fontSize)
        {
            ApplyFontSize(textBlock, fontSize);
            textBlock.Measure(new Size(LayoutWidth, double.PositiveInfinity));
            return textBlock.DesiredSize.Height <= Height && textBlock.DesiredSize.Width <= LayoutWidth;
        }

        // Binary search for the largest font size in [minSize, maxSize] that fits; minSize if none does
        private double FindLargestFittingSize(TextBlock textBlock, double minSize, double maxSize)
        {
            if (FitsAt(textBlock, maxSize)) return maxSize;
            if (!FitsAt(textBlock, minSize)) return minSize;

            double fits = minSize, tooBig = maxSize;
            while (tooBig - fits > 0.5)
            {
                double mid = (fits + tooBig) / 2;
                if (FitsAt(textBlock, mid)) fits = mid;
                else tooBig = mid;
            }
            return fits;
        }

        // Size the text to fill the original text box, staying close to the game's own font size
        private void AdjustFontSize(Border border, TextBlock textBlock)
        {
            try
            {
                // Check if auto sizing is enabled
                if (!ConfigManager.Instance.IsAutoSizeTextBlocksEnabled())
                {
                    // Fixed font size; still report whether it fits so the layout pass can widen the box
                    TextFitsBox = Width <= 0 || Height <= 0 || FitsAt(textBlock, 24);
                    ApplyFontSize(textBlock, 24);
                    return;
                }
                
                // Exit early if dimensions aren't set or text is empty
                if (Width <= 0 || Height <= 0 || string.IsNullOrWhiteSpace(textBlock.Text))
                {
                    TextFitsBox = true;
                    ApplyFontSize(textBlock, 24);
                    return;
                }

                double configMin = ConfigManager.Instance.GetLanguageFontSizeMin();
                double configMax = ConfigManager.Instance.GetLanguageFontSizeMax();
                if (configMin > configMax) (configMin, configMax) = (configMax, configMin);

                // Same length doesn't mean same rendered size, so key on the actual text and font
                string cacheKey = $"{textBlock.FontFamily.Source}_{Width}_{LayoutWidth}_{Height}_{MinSourceSizeRatio}_{configMin}_{configMax}_{Text}_{textBlock.Text}";
                
                // Check cache
                if (_fontSizeCache.TryGetValue(cacheKey, out var cached))
                {
                    TextFitsBox = cached.Fits;
                    ApplyFontSize(textBlock, cached.Size);
                    return;
                }

                // Estimate the game's font size: the largest size at which the source text fills its own box
                var sourceProbe = new TextBlock
                {
                    Text = Text,
                    FontFamily = textBlock.FontFamily,
                    FontWeight = textBlock.FontWeight,
                    TextWrapping = TextWrapping.Wrap
                };
                // The source filled the original box, so measure it at the original width
                double layoutWidth = LayoutWidth;
                LayoutWidth = Width;
                double sourceSize = FindLargestFittingSize(sourceProbe, 1, configMax);
                LayoutWidth = layoutWidth;

                // Keep the translation readable relative to the source and no larger than it
                double minSize = Math.Min(configMax, Math.Max(configMin, sourceSize * MinSourceSizeRatio));
                double maxSize = Math.Max(minSize, Math.Min(configMax, sourceSize));

                // If even minSize overflows, the border grows downward (MinHeight + infinite MaxHeight)
                double finalSize = FindLargestFittingSize(textBlock, minSize, maxSize);
                TextFitsBox = FitsAt(textBlock, finalSize);
                textBlock.VerticalAlignment = VerticalAlignment.Stretch;
                ApplyFontSize(textBlock, finalSize);
                
                // Cache result
                if (_fontSizeCache.Count > 100) _fontSizeCache.Clear();
                _fontSizeCache[cacheKey] = (finalSize, TextFitsBox);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AdjustFontSize: {ex.Message}");
                TextFitsBox = true;
                ApplyFontSize(textBlock, 24);
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
                            else if (ttsService == "Windows TTS")
                            {
                                success = await WindowsTTSService.Instance.SpeakText(text);
                            }
                            else if (ttsService == "Supertonic")
                            {
                                success = await SupertonicTTSService.Instance.SpeakText(text);
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