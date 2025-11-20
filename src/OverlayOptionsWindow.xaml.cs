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

namespace RSTGameTranslation
{
    public partial class OverlayOptionsWindow : Window
    {
        // Store original values for cancel operation
        private Color _originalBackgroundColor;
        private Color _originalTextColor;
        private string _originalFont;
        private bool _originalFontOverrideEnabled;
        private double _originalFontSizeMin;
        private double _originalFontSizeMax;
        
        // Store current values for apply operation
        private Color _currentBackgroundColor;
        private Color _currentTextColor;
        private string _currentFont;
        private bool _currentFontOverrideEnabled;
        private double _currentFontSizeMin;
        private double _currentFontSizeMax;
        
        // Default values
        private readonly Color DEFAULT_BACKGROUND_COLOR = Color.FromArgb(128, 0, 0, 0); // Dark background
        private readonly Color DEFAULT_TEXT_COLOR = Colors.White;
        private readonly string DEFAULT_FONT = "Arial";
        private readonly bool DEFAULT_FONT_OVERRIDE = false;
        private readonly double DEFAULT_FONT_SIZE_MIN = 10;
        private readonly double DEFAULT_FONT_SIZE_MAX = 68;

        public OverlayOptionsWindow()
        {
            InitializeComponent();
            
            // Load font settings
            LoadFontSettings();

            // Load current settings from config
            LoadCurrentSettings();

            
            // Update UI with loaded settings
            UpdateUIFromSettings();
        }


        private void LoadCurrentSettings()
        {
            try
            {
                // Get values from config manager
                _originalBackgroundColor = ConfigManager.Instance.GetOverlayBackgroundColor();
                _originalTextColor = ConfigManager.Instance.GetOverlayTextColor();
                _originalFont = ConfigManager.Instance.GetLanguageFontFamily();
                _originalFontOverrideEnabled = ConfigManager.Instance.IsLanguageFontOverrideEnabled();
                _originalFontSizeMin = ConfigManager.Instance.GetLanguageFontSizeMin();
                _originalFontSizeMax = ConfigManager.Instance.GetLanguageFontSizeMax();

                // Set current values to match original values
                _currentBackgroundColor = _originalBackgroundColor;
                _currentTextColor = _originalTextColor;
                _currentFont = _originalFont;
                _currentFontOverrideEnabled = _originalFontOverrideEnabled;
                _currentFontSizeMin = _originalFontSizeMin;
                _currentFontSizeMax = _originalFontSizeMax;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading current settings: {ex.Message}");

                // Use defaults if settings can't be loaded
                SetDefaultValues();
            }
        }

        private void SetDefaultValues()
        {
            // Set current values to defaults
            _currentBackgroundColor = DEFAULT_BACKGROUND_COLOR;
            _currentTextColor = DEFAULT_TEXT_COLOR;
            _currentFont = DEFAULT_FONT;
            _currentFontOverrideEnabled = DEFAULT_FONT_OVERRIDE;
            _currentFontSizeMin = DEFAULT_FONT_SIZE_MIN;
            _currentFontSizeMax = DEFAULT_FONT_SIZE_MAX;
        }

        private void UpdateUIFromSettings()
        {
            try
            {
                // Update background color
                backgroundColorButton.Background = new SolidColorBrush(_currentBackgroundColor);
                backgroundColorText.Text = ColorToHexString(_currentBackgroundColor);
                
                // Update text color
                textColorButton.Background = new SolidColorBrush(_currentTextColor);
                textColorText.Text = ColorToHexString(_currentTextColor);

                // Update font family selection
                languageFontFamilyComboBox.Text = _currentFont;

                // Update font override checkbox
                languageFontOverrideCheckBox.IsChecked = _currentFontOverrideEnabled;

                // Update font size min
                languageFontSizeMinTextBox.Text = _currentFontSizeMin.ToString();

                // Update font size max
                languageFontSizeMaxTextBox.Text = _currentFontSizeMax.ToString();
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
                // Get selected color
                _currentBackgroundColor = Color.FromArgb(
                    255, 
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
        

        private void TextColorButton_Click(object sender, RoutedEventArgs e)
        {
            // Create color dialog
            var colorDialog = new ColorDialog();
            
            // Set the initial color
            colorDialog.Color = System.Drawing.Color.FromArgb(
                _currentTextColor.A, 
                _currentTextColor.R, 
                _currentTextColor.G, 
                _currentTextColor.B);
            
            // Show dialog
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Get selected color
                _currentTextColor = Color.FromArgb(
                    255, // Always full opacity for text
                    colorDialog.Color.R,
                    colorDialog.Color.G,
                    colorDialog.Color.B);
                
                // Update UI
                textColorButton.Background = new SolidColorBrush(_currentTextColor);
                textColorText.Text = ColorToHexString(_currentTextColor);
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate font sizes
                if (!double.TryParse(languageFontSizeMinTextBox.Text, out double minSize) ||
                    !double.TryParse(languageFontSizeMaxTextBox.Text, out double maxSize))
                {
                    MessageBox.Show("Font sizes must be valid numbers.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (minSize < 0 || maxSize < 0)
                {
                    MessageBox.Show("Font sizes cannot be negative.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (minSize > maxSize)
                {
                    MessageBox.Show("Minimum font size cannot be greater than maximum font size.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Save settings to config
                ConfigManager.Instance.SetValue(ConfigManager.OVERLAY_BACKGROUND_COLOR, ColorToHexString(
                    Color.FromArgb(255, _currentBackgroundColor.R, _currentBackgroundColor.G, _currentBackgroundColor.B)));
                ConfigManager.Instance.SetValue(ConfigManager.OVERLAY_TEXT_COLOR, ColorToHexString(_currentTextColor));
                ConfigManager.Instance.SetValue(ConfigManager.LANGUAGE_FONT_FAMILY, languageFontFamilyComboBox.Text);
                ConfigManager.Instance.SetValue(ConfigManager.LANGUAGE_FONT_OVERRIDE,
                    languageFontOverrideCheckBox.IsChecked == true ? "true" : "false");
                
                // Save validated font sizes
                ConfigManager.Instance.SetValue(ConfigManager.LANGUAGE_FONT_SIZE_MIN, minSize.ToString());
                ConfigManager.Instance.SetValue(ConfigManager.LANGUAGE_FONT_SIZE_MAX, maxSize.ToString());

                // Save config to file
                ConfigManager.Instance.SaveConfig();
                TextObject.ClearCache();
                var currentTexts = Logic.Instance.GetTextObjects();
                foreach (var textObj in currentTexts)
                {
                    textObj.UpdateUIElement(); 
                }
                MonitorWindow.Instance.RefreshOverlays();

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
                SetDefaultValues();
                
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

        private void LoadFontSettings()
        {
            try
            {
                // Get all font families
                var fontFamilies = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
                
                // Populate language font combo box
                if (languageFontFamilyComboBox != null)
                {
                    languageFontFamilyComboBox.ItemsSource = fontFamilies;
                    languageFontFamilyComboBox.DisplayMemberPath = "Source";
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error populating font family combo boxes: {ex.Message}");
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