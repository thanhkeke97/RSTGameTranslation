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
        
        // Store current values for apply operation
        private Color _currentBackgroundColor;
        private Color _currentTextColor;
        private string _currentFont;
        private bool _currentFontOverrideEnabled;
        
        // Default values
        private readonly Color DEFAULT_BACKGROUND_COLOR = Color.FromArgb(128, 0, 0, 0); // Dark background
        private readonly Color DEFAULT_TEXT_COLOR = Colors.White;
        private readonly string DEFAULT_FONT = "Arial";
        private readonly bool DEFAULT_FONT_OVERRIDE = false;

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

                // Set current values to match original values
                _currentBackgroundColor = _originalBackgroundColor;
                _currentTextColor = _originalTextColor;
                _currentFont = _originalFont;
                _currentFontOverrideEnabled = _originalFontOverrideEnabled;
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
                // Save settings to config
                ConfigManager.Instance.SetValue(ConfigManager.OVERLAY_BACKGROUND_COLOR, ColorToHexString(
                    Color.FromArgb(255, _currentBackgroundColor.R, _currentBackgroundColor.G, _currentBackgroundColor.B)));
                ConfigManager.Instance.SetValue(ConfigManager.OVERLAY_TEXT_COLOR, ColorToHexString(_currentTextColor));
                ConfigManager.Instance.SetValue(ConfigManager.LANGUAGE_FONT_FAMILY,languageFontFamilyComboBox.Text);
                ConfigManager.Instance.SetValue(ConfigManager.LANGUAGE_FONT_OVERRIDE, 
                    languageFontOverrideCheckBox.IsChecked == true ? "true" : "false");
                // Save config to file
                ConfigManager.Instance.SaveConfig();
                Logic.Instance.ResetHash();
                Logic.Instance.ClearAllTextObjects();
                MainWindow.Instance.SetOCRCheckIsWanted(true);
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