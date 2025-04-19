using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Media;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Clipboard = System.Windows.Forms.Clipboard;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using FontFamily = System.Windows.Media.FontFamily;

namespace UGTLive
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
            TextColor = textColor ?? new SolidColorBrush(Colors.White);
            BackgroundColor = backgroundColor ?? new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)); // Half-transparent black
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
                FontFamily = new FontFamily("Noto Sans JP, MS Gothic, Yu Gothic, Microsoft YaHei, Arial Unicode MS, Arial"),
                Margin = new Thickness(0), // Increased margin for better text display
                TextTrimming = TextTrimming.None // Prevent text trimming for wrapped text
            };

            // Set explicit width and height if provided
            if (Width > 0)
            {
                TextBlock.MaxWidth = Width;
                Border.MaxWidth = Width + 20; // Increased padding for word wrap
            }

            if (Height > 0)
            {
                Border.Height = Height;
            }

            // Add the text block to the border
            Border.Child = TextBlock;

            // Add click event handler
            Border.MouseLeftButtonDown += Border_MouseLeftButtonDown;

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
                }
                else
                {
                    // Use original text
                    TextBlock.Text = this.Text;
                }
                TextBlock.Foreground = this.TextColor;
                Border.Background = this.BackgroundColor;
                // Position at the upper left of the rectangle
                Border.Margin = new Thickness(X, Y, 0, 0);

                if (Width > 0)
                {
                    TextBlock.MaxWidth = Width;
                    Border.MaxWidth = Width + 20; // Increased padding for word wrap
                }

                if (Height > 0)
                {
                    Border.Height = Height;
                }

                // Reset font size to default and then adjust if needed
                TextBlock.FontSize = 24; // Increased from 18 to 24 for better initial size
                AdjustFontSize(Border, TextBlock);
            }
        }

        // Static cache for font size calculations
        private static readonly Dictionary<string, double> _fontSizeCache = new Dictionary<string, double>();
        
        // Adjust font size to fit within the container using binary search
        private void AdjustFontSize(Border border, TextBlock textBlock)
        {
            try
            {
                // Check if auto sizing is enabled
                if (!ConfigManager.Instance.IsAutoSizeTextBlocksEnabled())
                {
                    // Just set default font size and exit
                    textBlock.FontSize = 24; // Increased from 18 to 24 for better initial size
                    textBlock.LayoutTransform = Transform.Identity;
                    return;
                }
                
                // Exit early if dimensions aren't set or text is empty
                if (Width <= 0 || Height <= 0 || string.IsNullOrWhiteSpace(textBlock.Text))
                {
                    // Reset to defaults and exit
                    textBlock.FontSize = 24; // Increased from 18 to 24 for better initial size
                    textBlock.LayoutTransform = Transform.Identity;
                    return;
                }

                // Basic text settings
                textBlock.TextWrapping = TextWrapping.Wrap;
                textBlock.VerticalAlignment = VerticalAlignment.Center;
                textBlock.TextAlignment = TextAlignment.Left;
                
                // Create a cache key based on text length, width and height
                // Using length instead of full text to increase cache hits for similar-sized texts
                string cacheKey = $"{textBlock.Text.Length}_{Width}_{Height}";
                
                // Check if we have a cached font size for similar dimensions
                if (_fontSizeCache.TryGetValue(cacheKey, out double cachedFontSize))
                {
                    textBlock.FontSize = cachedFontSize;
                    textBlock.LayoutTransform = Transform.Identity;
                    return;
                }
                
                // Binary search for the best font size
                double minSize = 10;
                double maxSize = 48; // Increased from 36 to 48 to allow for larger text
                double currentSize = 24; // Increased from 18 to 24 for better initial size
                int maxIterations = 6; // Reduced from 10 to 6 iterations for performance
                double lastDiff = double.MaxValue;
                
                for (int i = 0; i < maxIterations; i++)
                {
                    textBlock.FontSize = currentSize;
                    textBlock.Measure(new Size(Width * 0.95, Double.PositiveInfinity));
                    
                    double currentDiff = Math.Abs(textBlock.DesiredSize.Height - Height);
                    
                    // Early termination if we're close enough
                    if (currentDiff < 2 || Math.Abs(lastDiff - currentDiff) < 0.5)
                    {
                        break;
                    }
                    
                    lastDiff = currentDiff;
                    
                    // If text is too tall, decrease font size more aggressively
                    if (textBlock.DesiredSize.Height > Height * 0.90)
                    {
                        maxSize = currentSize;
                        currentSize = (minSize + currentSize) / 2;
                    }
                    // If text is too short, increase font size
                    // Using 0.85 for a more balanced fit that prevents overflow
                    else if (textBlock.DesiredSize.Height < Height * 0.85)
                    {
                        minSize = currentSize;
                        currentSize = (currentSize + maxSize) / 2;
                    }
                    // Good enough fit
                    else
                    {
                        break;
                    }
                }
                
                // Verify final size is within min/max range
                double finalSize = Math.Max(minSize, Math.Min(maxSize, currentSize));
                textBlock.FontSize = finalSize;
                textBlock.LayoutTransform = Transform.Identity;
                
                // Cache the result for future use
                if (_fontSizeCache.Count > 100) // Limit cache size
                {
                    _fontSizeCache.Clear(); // Simple strategy: clear all when too many entries
                }
                _fontSizeCache[cacheKey] = finalSize;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AdjustFontSize: {ex.Message}");
                // Reset to defaults in case of any exception
                textBlock.FontSize = 24; // Increased from 18 to 24 for better initial size
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