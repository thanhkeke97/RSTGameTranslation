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
        
        public const double CurrentVersion = 3.0;
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
                    Width = 550,
                    Height = 350,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new SolidColorBrush(Colors.White),
                    AllowsTransparency = true,
                    Topmost = true
                };

                // Create a border with drop shadow and rounded corners for visual appeal
                Border mainBorder = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    BorderBrush = new SolidColorBrush(WPFColor.FromRgb(100, 149, 237)), // Cornflower blue
                    BorderThickness = new Thickness(2),
                    Padding = new Thickness(20),
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new WPFPoint(0, 0),
                        EndPoint = new WPFPoint(1, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Colors.White, 0.0),
                            new GradientStop(WPFColor.FromRgb(240, 248, 255), 1.0) // AliceBlue
                        }
                    },
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        Direction = 315,
                        ShadowDepth = 10,
                        BlurRadius = 15,
                        Opacity = 0.6
                    }
                };
                
                Grid grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // App Icon with reflection effect
                StackPanel iconPanel = new StackPanel
                {
                    Orientation = WPFOrientation.Vertical,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                
                // Main icon
                System.Windows.Controls.Image appIcon = new System.Windows.Controls.Image
                {
                    Source = new BitmapImage(new Uri("pack://application:,,,/media/AppIcon.ico")),
                    Width = 180,
                    Height = 180,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        Direction = 320,
                        ShadowDepth = 3,
                        BlurRadius = 5,
                        Opacity = 0.4
                    }
                };
                iconPanel.Children.Add(appIcon);
                
                Grid.SetRow(iconPanel, 0);
                grid.Children.Add(iconPanel);

                // Version Text
                _versionTextBlock = new TextBlock
                {
                    Text = $"Realtime Screen Translator v{CurrentVersion.ToString("F1")} By Thanh Pham",
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 10, 0, 10),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(WPFColor.FromRgb(30, 30, 110))
                };
                Grid.SetRow(_versionTextBlock, 1);
                grid.Children.Add(_versionTextBlock);

                // Status Text
                _statusTextBlock = new TextBlock
                {
                    Text = "Checking latest version...",
                    FontSize = 12,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 0, 0, 10),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(WPFColor.FromRgb(90, 90, 90))
                };
                Grid.SetRow(_statusTextBlock, 2);
                grid.Children.Add(_statusTextBlock);

                // Set the grid as content of the border
                mainBorder.Child = grid;
                
                // Set the border as content of the window
                _splashWindow.Content = mainBorder;
                _splashWindow.Show();
                CheckForUpdates();
                // CloseSplashAfterDelay(2000);
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
                    string message = versionInfo.Message?.Replace("{VERSION_STRING}", versionInfo.LatestVersion.ToString()) 
                    ?? $"New version {versionInfo.LatestVersion.ToString("F1")} is available. Would you like to download it now?";
                    
                    
                    // Update status text
                    UpdateStatusText($"New version V{versionInfo.LatestVersion.ToString("F1")} available!");
                    
                    // Wait for 2 seconds before showing update dialog
                    await Task.Delay(2000);
                    
                    // Temporarily disable Topmost property before showing dialog
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_splashWindow != null)
                        {
                            _splashWindow.Topmost = false;
                        }
                        
                        System.Windows.MessageBoxResult result = System.Windows.MessageBox.Show(message, "Update Available", 
                            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);
                        
                        if (result == System.Windows.MessageBoxResult.Yes)
                        {
                            DownloadUpdate(versionInfo.LatestVersion);
                        }
                        
                        CloseSplash();
                    });
                }
                else
                {
                    UpdateStatusText("You have the latest version");
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
            string DownloadUrl = $"https://github.com/thanhkeke97/RSTGameTranslation/releases/download/V{version.ToString("F1")}/RSTGameTranslation_v{version.ToString("F1")}.zip";
            try
            {
                UpdateStatusText("Starting download...");
                
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
                
                System.Windows.MessageBox.Show($"Failed to download update: {ex.Message}", "Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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