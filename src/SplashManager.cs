using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

// Use explicit namespaces to avoid ambiguity
using WPFPoint = System.Windows.Point;
using WPFColor = System.Windows.Media.Color;
using WPFOrientation = System.Windows.Controls.Orientation;

namespace RSTGameTranslation
{
    public class SplashManager
    {
        private static SplashManager? _instance;
        public static SplashManager Instance => _instance ??= new SplashManager();

        private Window? _splashWindow;
        private TextBlock? _versionTextBlock;
        private TextBlock? _statusTextBlock;
        
        // Event to notify when splash screen is closed
        public event EventHandler? SplashClosed;
        
        public const double CurrentVersion = 4.1;
        private const string VersionCheckerUrl = "https://raw.githubusercontent.com/thanhkeke97/RSTGameTranslation/refs/heads/main/media/latest_version_checker.json";

        private class VersionInfo
        {
            [System.Text.Json.Serialization.JsonPropertyName("name")]
            public string? Name { get; set; }
            
            [System.Text.Json.Serialization.JsonPropertyName("latest_version")]
            public double LatestVersion { get; set; }
            
            [System.Text.Json.Serialization.JsonPropertyName("message")]
            public string? Message { get; set; }
        }

        public void ShowSplash()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _splashWindow = new Window
                {
                    Title = "Realtime Screen Translator",
                    Width = 600,
                    Height = 400,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = Brushes.Transparent,
                    AllowsTransparency = true,
                    Topmost = true,
                    Opacity = 0 // Start invisible for fade-in animation
                };

                // Main container with dark glassmorphism effect (Option 1 Style)
                Border mainBorder = new Border
                {
                    CornerRadius = new CornerRadius(20),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(30),
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new WPFPoint(0, 0),
                        EndPoint = new WPFPoint(1, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(WPFColor.FromArgb(255, 15, 15, 35), 0.0),      // Dark blue-black
                            new GradientStop(WPFColor.FromArgb(255, 25, 20, 45), 0.5),      // Deep purple
                            new GradientStop(WPFColor.FromArgb(255, 20, 25, 50), 1.0)       // Dark blue
                        }
                    },
                    Effect = new DropShadowEffect
                    {
                        Color = WPFColor.FromRgb(100, 80, 200),
                        Direction = 315,
                        ShadowDepth = 0,
                        BlurRadius = 40,
                        Opacity = 0.8
                    }
                };

                // Gradient border effect
                Border gradientBorder = new Border
                {
                    CornerRadius = new CornerRadius(20),
                    BorderThickness = new Thickness(2),
                    BorderBrush = new LinearGradientBrush
                    {
                        StartPoint = new WPFPoint(0, 0),
                        EndPoint = new WPFPoint(1, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(WPFColor.FromRgb(138, 43, 226), 0.0),   // Blue-violet
                            new GradientStop(WPFColor.FromRgb(75, 0, 130), 0.5),     // Indigo
                            new GradientStop(WPFColor.FromRgb(147, 112, 219), 1.0)   // Medium purple
                        }
                    }
                };
                
                Grid grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Icon
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Title
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Author (Đã thêm lại)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) }); // Loading bar

                // App Icon with glow effect
                StackPanel iconPanel = new StackPanel
                {
                    Orientation = WPFOrientation.Vertical,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                
                // Main icon with enhanced glow
                System.Windows.Controls.Image appIcon = new System.Windows.Controls.Image
                {
                    Source = new BitmapImage(new Uri("pack://application:,,,/media/AppIcon.ico")),
                    Width = 200,
                    Height = 200,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Effect = new DropShadowEffect
                    {
                        Color = WPFColor.FromRgb(138, 43, 226),
                        Direction = 0,
                        ShadowDepth = 0,
                        BlurRadius = 30,
                        Opacity = 0.9
                    }
                };
                iconPanel.Children.Add(appIcon);
                
                Grid.SetRow(iconPanel, 0);
                grid.Children.Add(iconPanel);

                // Version Text with glow
                _versionTextBlock = new TextBlock
                {
                    Text = $"Realtime Screen Translator v{CurrentVersion.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}",
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("Segoe UI"),
                    Margin = new Thickness(0, 20, 0, 5),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(Colors.White),
                    Effect = new DropShadowEffect
                    {
                        Color = WPFColor.FromRgb(138, 43, 226),
                        Direction = 0,
                        ShadowDepth = 0,
                        BlurRadius = 20,
                        Opacity = 0.8
                    }
                };
                Grid.SetRow(_versionTextBlock, 1);
                grid.Children.Add(_versionTextBlock);

                // Author text 
                TextBlock authorText = new TextBlock
                {
                    Text = "By Thanh Pham",
                    FontSize = 13,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 0, 0, 15),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(WPFColor.FromArgb(180, 200, 200, 255)),
                    Opacity = 0.7
                };
                Grid.SetRow(authorText, 2); // Row riêng
                grid.Children.Add(authorText);

                // Status Text with modern styling
                _statusTextBlock = new TextBlock
                {
                    Text = LocalizationManager.Instance.Strings["Splash_CheckingVersion"],
                    FontSize = 13,
                    FontFamily = new FontFamily("Segoe UI"),
                    Margin = new Thickness(0, 10, 0, 15),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(WPFColor.FromArgb(200, 180, 180, 255))
                };
                Grid.SetRow(_statusTextBlock, 3);
                grid.Children.Add(_statusTextBlock);

                // Modern loading bar
                Border loadingBarContainer = new Border
                {
                    Height = 4,
                    Margin = new Thickness(40, 0, 40, 20),
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(WPFColor.FromArgb(40, 255, 255, 255)),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
                };

                Border loadingBar = new Border
                {
                    Height = 4,
                    CornerRadius = new CornerRadius(2),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Width = 0,
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new WPFPoint(0, 0.5),
                        EndPoint = new WPFPoint(1, 0.5),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(WPFColor.FromRgb(138, 43, 226), 0.0),
                            new GradientStop(WPFColor.FromRgb(147, 112, 219), 1.0)
                        }
                    }
                };

                loadingBarContainer.Child = loadingBar;
                Grid.SetRow(loadingBarContainer, 4);
                grid.Children.Add(loadingBarContainer);

                // Set the grid as content of borders
                mainBorder.Child = grid;
                gradientBorder.Child = mainBorder;
                
                // Set the border as content of the window
                _splashWindow.Content = gradientBorder;
                _splashWindow.Show();

                // === ANIMATIONS ===
                
                // 1. Fade-in animation for window
                DoubleAnimation fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(600),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                _splashWindow.BeginAnimation(Window.OpacityProperty, fadeIn);

                // 2. Icon pulse animation (subtle glow effect)
                DoubleAnimation iconPulse = new DoubleAnimation
                {
                    From = 0.7,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(1500),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                appIcon.BeginAnimation(UIElement.OpacityProperty, iconPulse);

                // 3. Loading bar animation (FIXED: Chạy trực tiếp)
                DoubleAnimation loadingAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 520, // Ước lượng width (600 - 40*2 padding)
                    Duration = TimeSpan.FromMilliseconds(2000),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };
                
                // Fix: Chạy animation ngay lập tức, không chờ Loaded event
                loadingBar.BeginAnimation(Border.WidthProperty, loadingAnimation);

                CheckForUpdates();
            });
        }

        private async void CheckForUpdates()
        {
            try
            {
                VersionInfo? versionInfo = await FetchVersionInfo();
                if (versionInfo == null)
                {
                    CloseSplashAfterDelay(2000);
                    return;
                }

                if (versionInfo.LatestVersion > CurrentVersion)
                {
                    string message = versionInfo.Message?.Replace("{VERSION_STRING}", versionInfo.LatestVersion.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)) 
                    ?? $"New version {versionInfo.LatestVersion.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} is available. Would you like to download it now?";
                    
                    
                    // Update status text
                    UpdateStatusText(string.Format(
                        LocalizationManager.Instance.Strings["Splash_NewVersionAvailable"],
                        versionInfo.LatestVersion.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                    ));
                    
                    // Wait for 2 seconds before showing update dialog
                    await Task.Delay(2000);
                    
                    // Temporarily disable Topmost property before showing dialog
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_splashWindow != null)
                        {
                            _splashWindow.Topmost = false;
                        }
                        
                        System.Windows.MessageBoxResult result = System.Windows.MessageBox.Show(
                            message,
                            LocalizationManager.Instance.Strings["Title_UpdateAvailable"],
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Information
                        );
                        
                        if (result == System.Windows.MessageBoxResult.Yes)
                        {
                            DownloadUpdate(versionInfo.LatestVersion);
                        }
                        
                        CloseSplash();
                    });
                }
                else
                {
                    UpdateStatusText(LocalizationManager.Instance.Strings["Splash_LatestVersion"]);
                    CloseSplashAfterDelay(2000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking for updates: {ex.Message}");
                CloseSplashAfterDelay(2000);
            }
        }

        private static readonly HttpClient _httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };

        private async Task<VersionInfo?> FetchVersionInfo()
        {
            try
            {
                
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    
                    if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                    {
                        _httpClient.DefaultRequestHeaders.Add("User-Agent", "RSTGameTranslation");
                    }

                    
                    if (!_httpClient.DefaultRequestHeaders.Contains("Cache-Control"))
                    {
                        _httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache, no-store");
                    }

                    Console.WriteLine($"Fetching version info from: {VersionCheckerUrl}");
                    
                    
                    using (var response = await _httpClient.GetAsync(VersionCheckerUrl, cts.Token))
                    {
                        response.EnsureSuccessStatusCode();
                        string json = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"Received JSON: {json}");
                        
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };
                        
                        var result = JsonSerializer.Deserialize<VersionInfo>(json, options);
                        Console.WriteLine($"Deserialized version: {result?.LatestVersion}, name: {result?.Name}, message: {result?.Message}");
                        return result;
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Fetching version info timed out after 5 seconds");
                return null;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"HTTP request error: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching version info: {ex.Message}");
                return null;
            }
        }

        private void UpdateStatusText(string text)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (_statusTextBlock != null)
                {
                    _statusTextBlock.Text = text;
                }
            });
        }

        private void DownloadUpdate(double version)
        {
            string DownloadUrl = $"https://github.com/thanhkeke97/RSTGameTranslation/releases/download/V{version.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}/RSTGameTranslation_v{version.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}.zip";
            try
            {
                UpdateStatusText(LocalizationManager.Instance.Strings["Splash_StartingDownload"]);
                
                // Open the download URL in the default browser
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = DownloadUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading update: {ex.Message}");
                // Ensure error dialog is visible
                if (_splashWindow != null)
                {
                    _splashWindow.Topmost = false;
                }
                
                System.Windows.MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_FailedDownloadUpdate"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error
                );
            }
        }

        private void CloseSplashAfterDelay(int delay)
        {
            Task.Delay(delay).ContinueWith(_ =>
            {
                CloseSplash();
            });
        }

        private void CloseSplash()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _splashWindow?.Close();
                _splashWindow = null;
                
                // Raise the SplashClosed event
                SplashClosed?.Invoke(this, EventArgs.Empty);
            });
        }
    }
}