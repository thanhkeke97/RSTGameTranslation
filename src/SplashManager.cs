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
        
        public const double CurrentVersion = 4.3;
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
                    Width = 420,
                    Height = 280,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = Brushes.Transparent,
                    AllowsTransparency = true,
                    Topmost = true,
                    Opacity = 0 // Start invisible for fade-in animation
                };

                // Colors matching MainWindow light theme
                WPFColor surfaceColor = WPFColor.FromRgb(255, 255, 255);      // #FFFFFF
                WPFColor surface2Color = WPFColor.FromRgb(243, 248, 253);     // #F3F8FD
                WPFColor borderColor = WPFColor.FromRgb(224, 236, 248);       // #E0ECF8
                WPFColor textColor = WPFColor.FromRgb(11, 37, 69);            // #0B2545
                WPFColor mutedColor = WPFColor.FromRgb(91, 107, 122);         // #5B6B7A
                WPFColor accentColor = WPFColor.FromRgb(0, 102, 204);         // #0066CC

                // Main container - clean light design matching MainWindow
                Border mainBorder = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(borderColor),
                    Background = new SolidColorBrush(surfaceColor),
                    Padding = new Thickness(30),
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        Direction = 270,
                        ShadowDepth = 0,
                        BlurRadius = 25,
                        Opacity = 0.15
                    }
                };
                
                Grid grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Logo + Title
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Loading bar
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Author

                // Top section: Logo + Title (horizontal layout like MainWindow title bar)
                StackPanel headerPanel = new StackPanel
                {
                    Orientation = WPFOrientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                // Logo with gradient background (matching MainWindow title bar style)
                Border logoBorder = new Border
                {
                    Width = 48,
                    Height = 48,
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(0, 0, 15, 0),
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new WPFPoint(0, 0),
                        EndPoint = new WPFPoint(1, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(WPFColor.FromRgb(0, 170, 255), 0),   // #00aaff
                            new GradientStop(WPFColor.FromRgb(0, 204, 136), 1)    // #00cc88
                        }
                    }
                };

                System.Windows.Controls.Image appIcon = new System.Windows.Controls.Image
                {
                    Source = new BitmapImage(new Uri("pack://application:,,,/media/AppIcon.ico")),
                    Width = 32,
                    Height = 32,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                };
                logoBorder.Child = appIcon;
                headerPanel.Children.Add(logoBorder);

                // Title stack
                StackPanel titleStack = new StackPanel
                {
                    Orientation = WPFOrientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock appTitle = new TextBlock
                {
                    Text = "RSTGameTranslation",
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = new SolidColorBrush(textColor)
                };
                titleStack.Children.Add(appTitle);

                _versionTextBlock = new TextBlock
                {
                    Text = $"v{CurrentVersion.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}",
                    FontSize = 13,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = new SolidColorBrush(mutedColor),
                    Margin = new Thickness(0, 2, 0, 0)
                };
                titleStack.Children.Add(_versionTextBlock);

                headerPanel.Children.Add(titleStack);
                Grid.SetRow(headerPanel, 0);
                grid.Children.Add(headerPanel);

                // Status Text - clean and simple
                _statusTextBlock = new TextBlock
                {
                    Text = LocalizationManager.Instance.Strings["Splash_CheckingVersion"],
                    FontSize = 12,
                    FontFamily = new FontFamily("Segoe UI"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(mutedColor),
                    Margin = new Thickness(0, 0, 0, 12)
                };
                Grid.SetRow(_statusTextBlock, 2);
                grid.Children.Add(_statusTextBlock);

                // Loading bar - accent color matching MainWindow
                Border loadingBarContainer = new Border
                {
                    Height = 4,
                    Margin = new Thickness(20, 0, 20, 16),
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(surface2Color),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
                };

                Border loadingBar = new Border
                {
                    Height = 4,
                    CornerRadius = new CornerRadius(2),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Width = 0,
                    Background = new SolidColorBrush(accentColor)
                };

                loadingBarContainer.Child = loadingBar;
                Grid.SetRow(loadingBarContainer, 3);
                grid.Children.Add(loadingBarContainer);

                // Author text - subtle at bottom
                TextBlock authorText = new TextBlock
                {
                    Text = "By Thanh Pham",
                    FontSize = 11,
                    FontFamily = new FontFamily("Segoe UI"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(WPFColor.FromArgb(150, 91, 107, 122)),
                    Margin = new Thickness(0, 0, 0, 5)
                };
                Grid.SetRow(authorText, 4);
                grid.Children.Add(authorText);

                mainBorder.Child = grid;
                _splashWindow.Content = mainBorder;
                _splashWindow.Show();

                // === ANIMATIONS ===
                
                // 1. Fade-in animation for window
                DoubleAnimation fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(400),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                _splashWindow.BeginAnimation(Window.OpacityProperty, fadeIn);

                // 2. Loading bar animation
                DoubleAnimation loadingAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 320, // Window width - padding
                    Duration = TimeSpan.FromMilliseconds(1800),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };
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