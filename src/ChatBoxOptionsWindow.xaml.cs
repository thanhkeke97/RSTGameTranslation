using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Forms;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using FontFamily = System.Windows.Media.FontFamily;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;

namespace UGTLive
{
    public partial class ChatBoxOptionsWindow : Window
    {
        // Store original values for cancel operation
        private Color _originalBackgroundColor;
        private double _originalBackgroundOpacity;
        private double _originalWindowOpacity;
        private string _originalFontFamily = "Segoe UI";  // Initialize with default value
        private double _originalFontSize;
        private Color _originalTextColor;
        private Color _translatedTextColor;
        
        // Store current values for apply operation
        private Color _currentBackgroundColor;
        private double _currentBackgroundOpacity;
        private double _currentWindowOpacity;
        private string _currentFontFamily = "Segoe UI";  // Initialize with default value
        private double _currentFontSize;
        private Color _currentOriginalTextColor;
        private Color _currentTranslatedTextColor;
        
        // Default values
        private readonly Color DEFAULT_BACKGROUND_COLOR = Color.FromArgb(128, 0, 0, 0); // Dark background
        private readonly double DEFAULT_BACKGROUND_OPACITY = 0.5;  // 50% background opacity
        private readonly double DEFAULT_WINDOW_OPACITY = 1.0;  // 100% window opacity
        private readonly string DEFAULT_FONT_FAMILY = "Segoe UI";
        private readonly double DEFAULT_FONT_SIZE = 14;
        private readonly Color DEFAULT_ORIGINAL_TEXT_COLOR = Colors.LightGoldenrodYellow;
        private readonly Color DEFAULT_TRANSLATED_TEXT_COLOR = Colors.White;

        public ChatBoxOptionsWindow()
        {
            InitializeComponent();
            
            // Load system font families
            LoadFontFamilies();
            
            // Load current settings from config
            LoadCurrentSettings();
            
            // Update UI with loaded settings
            UpdateUIFromSettings();
        }

        private void LoadFontFamilies()
        {
            try
            {
                // Get all font families
                var fontFamilies = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
                
                // Add to combo box
                fontFamilyComboBox.ItemsSource = fontFamilies;
                fontFamilyComboBox.DisplayMemberPath = "Source";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading font families: {ex.Message}");
            }
        }

        private void LoadCurrentSettings()
        {
            try
            {
                // Get values from config manager
                _originalBackgroundColor = ConfigManager.Instance.GetChatBoxBackgroundColor();
                _originalBackgroundOpacity = ConfigManager.Instance.GetChatBoxBackgroundOpacity();
                _originalWindowOpacity = ConfigManager.Instance.GetChatBoxWindowOpacity();
                _originalFontFamily = ConfigManager.Instance.GetChatBoxFontFamily();
                _originalFontSize = ConfigManager.Instance.GetChatBoxFontSize();
                _originalTextColor = ConfigManager.Instance.GetOriginalTextColor();
                _translatedTextColor = ConfigManager.Instance.GetTranslatedTextColor();
                
                // Set current values to match original values
                _currentBackgroundColor = _originalBackgroundColor;
                _currentBackgroundOpacity = _originalBackgroundOpacity;
                _currentWindowOpacity = _originalWindowOpacity;
                _currentFontFamily = _originalFontFamily;
                _currentFontSize = _originalFontSize;
                _currentOriginalTextColor = _originalTextColor;
                _currentTranslatedTextColor = _translatedTextColor;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading current settings: {ex.Message}");
            }
        }

        private void UpdateUIFromSettings()
        {
            try
            {
                // Update background color
                backgroundColorButton.Background = new SolidColorBrush(_currentBackgroundColor);
                backgroundColorText.Text = ColorToHexString(_currentBackgroundColor);
                
                // Update background opacity
                backgroundOpacitySlider.Value = _currentBackgroundOpacity;
                backgroundOpacityText.Text = $"{(int)(_currentBackgroundOpacity * 100)}%";
                
                // Update window opacity
                windowOpacitySlider.Value = _currentWindowOpacity;
                windowOpacityText.Text = $"{(int)(_currentWindowOpacity * 100)}%";
                
                // Update font family
                var fontFamily = fontFamilyComboBox.Items.Cast<FontFamily>()
                    .FirstOrDefault(f => f.Source == _currentFontFamily);
                if (fontFamily != null)
                {
                    fontFamilyComboBox.SelectedItem = fontFamily;
                }
                
                // Update font size
                fontSizeSlider.Value = _currentFontSize;
                fontSizeText.Text = _currentFontSize.ToString();
                
                // Update original text color
                originalTextColorButton.Background = new SolidColorBrush(_currentOriginalTextColor);
                originalTextColorText.Text = ColorToHexString(_currentOriginalTextColor);
                
                // Update translated text color
                translatedTextColorButton.Background = new SolidColorBrush(_currentTranslatedTextColor);
                translatedTextColorText.Text = ColorToHexString(_currentTranslatedTextColor);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating UI from settings: {ex.Message}");
            }
        }

        private string ColorToHexString(Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private void BackgroundColorButton_Click(object sender, RoutedEventArgs e)
        {
            // Create color dialog
            var colorDialog = new ColorDialog();
            
            // Set the initial color (ignore alpha, we handle that separately)
            colorDialog.Color = System.Drawing.Color.FromArgb(
                255, 
                _currentBackgroundColor.R, 
                _currentBackgroundColor.G, 
                _currentBackgroundColor.B);
            
            // Show dialog
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Get selected color (with current background opacity for preview)
                _currentBackgroundColor = Color.FromArgb(
                    (byte)(_currentBackgroundOpacity * 255), 
                    colorDialog.Color.R,
                    colorDialog.Color.G,
                    colorDialog.Color.B);
                
                // Update UI
                backgroundColorButton.Background = new SolidColorBrush(_currentBackgroundColor);
                backgroundColorText.Text = ColorToHexString(Color.FromArgb(255, _currentBackgroundColor.R, 
                                                                   _currentBackgroundColor.G, 
                                                                   _currentBackgroundColor.B));
            }
        }

        private void BackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (backgroundOpacityText != null)
            {
                _currentBackgroundOpacity = backgroundOpacitySlider.Value;
                backgroundOpacityText.Text = $"{(int)(_currentBackgroundOpacity * 100)}%";
                
                // Update the background color preview to show current opacity
                if (_currentBackgroundOpacity <= 0)
                {
                    // Show that background is fully transparent
                    backgroundColorButton.Background = new SolidColorBrush(Colors.Transparent);
                    
                    // Format color code + transparency indicator
                    string baseColor = $"#{_currentBackgroundColor.R:X2}{_currentBackgroundColor.G:X2}{_currentBackgroundColor.B:X2}";
                    backgroundColorText.Text = $"{baseColor} (Transparent)";
                }
                else
                {
                    // Update preview with current opacity
                    Color previewColor = Color.FromArgb(
                        (byte)(_currentBackgroundOpacity * 255),
                        _currentBackgroundColor.R,
                        _currentBackgroundColor.G,
                        _currentBackgroundColor.B);
                    backgroundColorButton.Background = new SolidColorBrush(previewColor);
                    
                    // Format color code with alpha
                    backgroundColorText.Text = ColorToHexString(Color.FromArgb(255, _currentBackgroundColor.R, 
                                                                  _currentBackgroundColor.G, 
                                                                  _currentBackgroundColor.B));
                }
            }
        }
        
        private void WindowOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (windowOpacityText != null)
            {
                _currentWindowOpacity = windowOpacitySlider.Value;
                windowOpacityText.Text = $"{(int)(_currentWindowOpacity * 100)}%";
            }
        }

        private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (fontFamilyComboBox.SelectedItem is FontFamily selectedFont)
            {
                _currentFontFamily = selectedFont.Source;
            }
        }

        private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (fontSizeText != null)
            {
                _currentFontSize = Math.Round(fontSizeSlider.Value);
                fontSizeText.Text = _currentFontSize.ToString();
            }
        }

        private void OriginalTextColorButton_Click(object sender, RoutedEventArgs e)
        {
            // Create color dialog
            var colorDialog = new ColorDialog();
            
            // Set the initial color
            colorDialog.Color = System.Drawing.Color.FromArgb(
                _currentOriginalTextColor.A, 
                _currentOriginalTextColor.R, 
                _currentOriginalTextColor.G, 
                _currentOriginalTextColor.B);
            
            // Show dialog
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Get selected color
                _currentOriginalTextColor = Color.FromArgb(
                    255, // Always full opacity for text
                    colorDialog.Color.R,
                    colorDialog.Color.G,
                    colorDialog.Color.B);
                
                // Update UI
                originalTextColorButton.Background = new SolidColorBrush(_currentOriginalTextColor);
                originalTextColorText.Text = ColorToHexString(_currentOriginalTextColor);
            }
        }

        private void TranslatedTextColorButton_Click(object sender, RoutedEventArgs e)
        {
            // Create color dialog
            var colorDialog = new ColorDialog();
            
            // Set the initial color
            colorDialog.Color = System.Drawing.Color.FromArgb(
                _currentTranslatedTextColor.A, 
                _currentTranslatedTextColor.R, 
                _currentTranslatedTextColor.G, 
                _currentTranslatedTextColor.B);
            
            // Show dialog
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Get selected color
                _currentTranslatedTextColor = Color.FromArgb(
                    255, // Always full opacity for text
                    colorDialog.Color.R,
                    colorDialog.Color.G,
                    colorDialog.Color.B);
                
                // Update UI
                translatedTextColorButton.Background = new SolidColorBrush(_currentTranslatedTextColor);
                translatedTextColorText.Text = ColorToHexString(_currentTranslatedTextColor);
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Save settings to config
                ConfigManager.Instance.SetValue(ConfigManager.CHATBOX_BACKGROUND_COLOR, ColorToHexString(
                    Color.FromArgb(255, _currentBackgroundColor.R, _currentBackgroundColor.G, _currentBackgroundColor.B)));
                ConfigManager.Instance.SetValue(ConfigManager.CHATBOX_BACKGROUND_OPACITY, _currentBackgroundOpacity.ToString());
                ConfigManager.Instance.SetValue(ConfigManager.CHATBOX_WINDOW_OPACITY, _currentWindowOpacity.ToString());
                ConfigManager.Instance.SetValue(ConfigManager.CHATBOX_FONT_FAMILY, _currentFontFamily);
                ConfigManager.Instance.SetValue(ConfigManager.CHATBOX_FONT_SIZE, _currentFontSize.ToString());
                ConfigManager.Instance.SetValue(ConfigManager.CHATBOX_ORIGINAL_TEXT_COLOR, ColorToHexString(_currentOriginalTextColor));
                ConfigManager.Instance.SetValue(ConfigManager.CHATBOX_TRANSLATED_TEXT_COLOR, ColorToHexString(_currentTranslatedTextColor));
                
                // Save config to file
                ConfigManager.Instance.SaveConfig();
                
                // Apply changes to ChatBoxWindow if it's open
                if (ChatBoxWindow.Instance != null)
                {
                    ChatBoxWindow.Instance.ApplyConfigurationStyling();
                }
                
                // Close the window
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Just close without saving
            this.DialogResult = false;
            this.Close();
        }
        
        private void DefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Reset to default values
                _currentBackgroundColor = DEFAULT_BACKGROUND_COLOR;
                _currentBackgroundOpacity = DEFAULT_BACKGROUND_OPACITY;
                _currentWindowOpacity = DEFAULT_WINDOW_OPACITY;
                _currentFontFamily = DEFAULT_FONT_FAMILY;
                _currentFontSize = DEFAULT_FONT_SIZE;
                _currentOriginalTextColor = DEFAULT_ORIGINAL_TEXT_COLOR;
                _currentTranslatedTextColor = DEFAULT_TRANSLATED_TEXT_COLOR;
                
                // Update UI with default values
                UpdateUIFromSettings();
                
                // Create flash animation for visual feedback
                CreateFlashAnimation(defaultsButton);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting defaults: {ex.Message}");
            }
        }
        
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
    }
}