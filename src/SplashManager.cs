using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace UGTLive
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
        
        public const double CurrentVersion = 0.1;
        private const string VersionCheckerUrl = "https://raw.githubusercontent.com/SethRobinson/UGTLive/refs/heads/main/media/latest_version_checker.json";
        private const string DownloadUrl = "https://www.rtsoft.com/files/UniversalGameTranslatorLive_Windows.zip";

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
                    Title = "Universal Game Translator Live",
                    Width = 550,
                    Height = 350,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new SolidColorBrush(Colors.White),
                    AllowsTransparency = true,
                    Topmost = true
                };

                Grid grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // App Icon
                System.Windows.Controls.Image appIcon = new System.Windows.Controls.Image
                {
                    Source = new BitmapImage(new Uri("pack://application:,,,/media/Icon1.ico")),
                    Width = 180,
                    Height = 180,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                };
                Grid.SetRow(appIcon, 0);
                grid.Children.Add(appIcon);

                // Version Text
                _versionTextBlock = new TextBlock
                {
                    Text = $"Universal Game Translator Live V{CurrentVersion} by Seth A. Robinson",
                    FontSize = 16,
                    Margin = new Thickness(0, 10, 0, 10),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                };
                Grid.SetRow(_versionTextBlock, 1);
                grid.Children.Add(_versionTextBlock);

                // Status Text
                _statusTextBlock = new TextBlock
                {
                    Text = "Checking latest version...",
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 10),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                };
                Grid.SetRow(_statusTextBlock, 2);
                grid.Children.Add(_statusTextBlock);

                _splashWindow.Content = grid;
                _splashWindow.Show();

                // Check for updates and close after 2 seconds
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
                    string message = versionInfo.Message?.Replace("{VERSION_STRING}", versionInfo.LatestVersion.ToString());
                    
                    // Update status text
                    UpdateStatusText($"New version V{versionInfo.LatestVersion} available!");
                    
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
                            DownloadUpdate();
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

        private async Task<VersionInfo?> FetchVersionInfo()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string json = await client.GetStringAsync(VersionCheckerUrl);
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

        private void DownloadUpdate()
        {
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