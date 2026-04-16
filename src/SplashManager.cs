using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
        private Border? _loadingBar;
        private Border? _loadingBarContainer;
        private CancellationTokenSource? _updateCts;
        
        // Event to notify when splash screen is closed
        public event EventHandler? SplashClosed;
        
        public const double CurrentVersion = 4.9;
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

                // Use current theme colors (dark/light) from App resources
                SolidColorBrush surfaceBrush = GetThemeBrush("SurfaceBrush", WPFColor.FromRgb(255, 255, 255));
                SolidColorBrush surface2Brush = GetThemeBrush("Surface2Brush", WPFColor.FromRgb(243, 248, 253));
                SolidColorBrush borderBrush = GetThemeBrush("BorderBrush", WPFColor.FromRgb(224, 236, 248));
                SolidColorBrush textBrush = GetThemeBrush("TextBrush", WPFColor.FromRgb(11, 37, 69));
                SolidColorBrush mutedBrush = GetThemeBrush("MutedBrush", WPFColor.FromRgb(91, 107, 122));
                SolidColorBrush accentBrush = GetThemeBrush("AccentBrush", WPFColor.FromRgb(0, 102, 204));

                // Main container - clean light design matching MainWindow
                Border mainBorder = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(1),
                    BorderBrush = borderBrush,
                    Background = surfaceBrush,
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
                    Foreground = textBrush
                };
                titleStack.Children.Add(appTitle);

                _versionTextBlock = new TextBlock
                {
                    Text = $"v{CurrentVersion.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}",
                    FontSize = 13,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = mutedBrush,
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
                    Foreground = mutedBrush,
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
                    Background = surface2Brush,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
                };

                _loadingBar = new Border
                {
                    Height = 4,
                    CornerRadius = new CornerRadius(2),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Width = 0,
                    Background = accentBrush
                };

                _loadingBarContainer = loadingBarContainer;
                loadingBarContainer.Child = _loadingBar;
                Grid.SetRow(loadingBarContainer, 3);
                grid.Children.Add(loadingBarContainer);

                // Author text - subtle at bottom
                TextBlock authorText = new TextBlock
                {
                    Text = "By Thanh Pham",
                    FontSize = 11,
                    FontFamily = new FontFamily("Segoe UI"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(WPFColor.FromArgb(150, mutedBrush.Color.R, mutedBrush.Color.G, mutedBrush.Color.B)),
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
                _loadingBar.BeginAnimation(Border.WidthProperty, loadingAnimation);

                CheckForUpdates();
            });
        }

        private static SolidColorBrush GetThemeBrush(string key, WPFColor fallback)
        {
            try
            {
                if (System.Windows.Application.Current?.Resources[key] is SolidColorBrush brush)
                {
                    // Clone by color to avoid sharing mutable/frozen instances.
                    return new SolidColorBrush(brush.Color);
                }
            }
            catch
            {
                // Fallback below
            }

            return new SolidColorBrush(fallback);
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
                    ?? $"New version {versionInfo.LatestVersion.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} is available. Would you like to update automatically?";
                    
                    
                    // Update status text
                    UpdateStatusText(string.Format(
                        LocalizationManager.Instance.Strings["Splash_NewVersionAvailable"],
                        versionInfo.LatestVersion.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                    ));
                    
                    // Wait for 2 seconds before showing update dialog
                    await Task.Delay(2000);
                    
                    // Ask user to auto-update
                    bool shouldUpdate = false;
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
                        
                        shouldUpdate = (result == System.Windows.MessageBoxResult.Yes);
                    });
                    
                    if (shouldUpdate)
                    {
                        await DownloadAndInstallUpdate(versionInfo.LatestVersion);
                        return; // App will be restarted by update script
                    }
                    
                    CloseSplash();
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

        private void UpdateLoadingProgress(double percent)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (_loadingBar != null && _loadingBarContainer != null)
                {
                    // Stop any running animation
                    _loadingBar.BeginAnimation(Border.WidthProperty, null);
                    double maxWidth = _loadingBarContainer.ActualWidth > 0 ? _loadingBarContainer.ActualWidth : 320;
                    _loadingBar.Width = maxWidth * (percent / 100.0);
                }
            });
        }

        private async Task DownloadAndInstallUpdate(double version)
        {
            string versionStr = version.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            string downloadUrl = $"https://github.com/thanhkeke97/RSTGameTranslation/releases/download/V{versionStr}/RSTGameTranslation_v{versionStr}.zip";
            string tempDir = Path.Combine(Path.GetTempPath(), "RSTUpdate");
            string zipPath = Path.Combine(tempDir, $"RSTGameTranslation_v{versionStr}.zip");
            string extractDir = Path.Combine(tempDir, "extracted");

            // Create cancellation token for this update operation
            _updateCts?.Cancel();
            _updateCts = new CancellationTokenSource();
            var token = _updateCts.Token;

            try
            {
                // Clean up previous update attempt
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                // --- Step 1: Download the zip file with progress ---
                UpdateStatusText(string.Format(
                    LocalizationManager.Instance.Strings["Splash_Downloading"], 0));

                using var downloadClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(30) };
                downloadClient.DefaultRequestHeaders.Add("User-Agent", "RSTGameTranslation");

                using var response = await downloadClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                long downloadedBytes = 0;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[81920]; // 80KB buffer
                    int bytesRead;
                    int lastReportedPercent = -1;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                        downloadedBytes += bytesRead;

                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            int percent = (int)((downloadedBytes * 100) / totalBytes.Value);
                            if (percent != lastReportedPercent)
                            {
                                lastReportedPercent = percent;
                                UpdateStatusText(string.Format(
                                    LocalizationManager.Instance.Strings["Splash_Downloading"], percent));
                                UpdateLoadingProgress(percent);
                            }
                        }
                    }
                }

                Console.WriteLine($"Download complete: {downloadedBytes} bytes");

                // Validate zip file before extracting
                if (!File.Exists(zipPath) || new FileInfo(zipPath).Length == 0)
                {
                    throw new IOException("Downloaded file is empty or missing.");
                }

                // --- Step 2: Extract the zip ---
                token.ThrowIfCancellationRequested();
                UpdateStatusText(LocalizationManager.Instance.Strings["Splash_Extracting"]);
                UpdateLoadingProgress(100);

                try
                {
                    await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractDir), token);
                }
                catch (InvalidDataException)
                {
                    throw new InvalidDataException("Downloaded file is corrupted. Please try again.");
                }

                // Find the actual content directory (zip may have a root folder like "RSTGameTranslation")
                string sourceDir = extractDir;
                var subDirs = Directory.GetDirectories(extractDir);
                if (subDirs.Length == 1 && Directory.GetFiles(extractDir).Length == 0)
                {
                    // Zip has a single root folder, use that as source
                    sourceDir = subDirs[0];
                }

                Console.WriteLine($"Extracted to: {sourceDir}");

                // --- Step 3: Create update batch script and launch it ---
                token.ThrowIfCancellationRequested();
                UpdateStatusText(LocalizationManager.Instance.Strings["Splash_Installing"]);

                string appDir = NormalizeDirectoryPath(AppDomain.CurrentDomain.BaseDirectory);
                int currentPid = Environment.ProcessId;
                string currentExePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? Environment.ProcessPath
                    ?? Path.Combine(appDir, "rst.exe");
                string restartExePath = ResolveRestartExecutablePath(appDir, sourceDir, currentExePath);

                string scriptPath = CreateUpdateScript(tempDir, sourceDir, appDir, currentPid, restartExePath);

                Console.WriteLine($"Launching update script: {scriptPath}");
                Console.WriteLine($"Current app exe path: {currentExePath}");
                Console.WriteLine($"Resolved restart exe path: {restartExePath}");

                // Launch the batch script with UseShellExecute so 'start' command works properly
                Process.Start(new ProcessStartInfo
                {
                    FileName = scriptPath,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                // Notify user and shutdown; the batch script will restart the app
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_splashWindow != null)
                        _splashWindow.Topmost = false;

                    System.Windows.MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Splash_UpdateSuccess"], versionStr),
                        LocalizationManager.Instance.Strings["Title_UpdateAvailable"],
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information
                    );

                    System.Windows.Application.Current.Shutdown();
                });
            }
            catch (OperationCanceledException)
            {
                // User closed the app or cancelled the update
                Console.WriteLine("Update cancelled by user.");
                CleanupTempDir(tempDir);
                CloseSplash();
            }
            catch (HttpRequestException ex)
            {
                // Network error (disconnected, DNS failure, server error, etc.)
                Console.WriteLine($"Network error during auto-update: {ex.Message}");
                ShowUpdateError(ex.Message);
                CleanupTempDir(tempDir);
            }
            catch (InvalidDataException ex)
            {
                // Corrupted zip file
                Console.WriteLine($"Corrupted download: {ex.Message}");
                ShowUpdateError(ex.Message);
                CleanupTempDir(tempDir);
            }
            catch (IOException ex)
            {
                // File system errors (disk full, permission denied, etc.)
                Console.WriteLine($"IO error during auto-update: {ex.Message}");
                ShowUpdateError(ex.Message);
                CleanupTempDir(tempDir);
            }
            catch (Exception ex)
            {
                // Unexpected error
                Console.WriteLine($"Error during auto-update: {ex.Message}");
                ShowUpdateError(ex.Message);
                CleanupTempDir(tempDir);
            }
            finally
            {
                _updateCts?.Dispose();
                _updateCts = null;
            }
        }

        private void ShowUpdateError(string errorMessage)
        {
            UpdateStatusText(string.Format(
                LocalizationManager.Instance.Strings["Splash_UpdateFailed"], errorMessage));

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (_splashWindow != null)
                    _splashWindow.Topmost = false;

                System.Windows.MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_FailedDownloadUpdate"], errorMessage),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error
                );
            });

            CloseSplash();
        }

        private static void CleanupTempDir(string tempDir)
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { /* ignore cleanup errors */ }
        }

        private static string ResolveRestartExecutablePath(string appDir, string sourceDir, string currentExePath)
        {
            string currentExeName = Path.GetFileName(currentExePath);

            string? sourceExecutable = FindPreferredExecutable(sourceDir, currentExeName);
            if (!string.IsNullOrWhiteSpace(sourceExecutable))
                return Path.Combine(appDir, Path.GetFileName(sourceExecutable));

            string? appExecutable = FindPreferredExecutable(appDir, currentExeName);
            if (!string.IsNullOrWhiteSpace(appExecutable))
                return appExecutable;

            return Path.Combine(appDir, string.IsNullOrWhiteSpace(currentExeName) ? "rst.exe" : currentExeName);
        }

        private static string? FindPreferredExecutable(string directory, string? currentExeName)
        {
            if (!Directory.Exists(directory))
                return null;

            string[] preferredNames =
            {
                "rst.exe",
                currentExeName ?? string.Empty,
                "rst_debug.exe"
            };

            foreach (string preferredName in preferredNames)
            {
                if (string.IsNullOrWhiteSpace(preferredName))
                    continue;

                string candidatePath = Path.Combine(directory, preferredName);
                if (File.Exists(candidatePath))
                    return candidatePath;
            }

            try
            {
                foreach (string executablePath in Directory.GetFiles(directory, "*.exe"))
                {
                    string fileName = Path.GetFileName(executablePath);
                    if (fileName.Equals("createdump.exe", StringComparison.OrdinalIgnoreCase))
                        continue;

                    return executablePath;
                }
            }
            catch
            {
                // Ignore fallback scan errors and let caller use default path.
            }

            return null;
        }

        private static string NormalizeDirectoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string root = Path.GetPathRoot(path) ?? string.Empty;
            string trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.IsNullOrEmpty(trimmedPath))
                return root;

            return trimmedPath.Length < root.Length ? root : trimmedPath;
        }

        private static string EscapePowerShellSingleQuotedString(string value)
        {
            return value.Replace("'", "''");
        }

        private string CreateUpdateScript(string tempDir, string sourceDir, string appDir, int currentPid, string restartExePath)
        {
            string normalizedTempDir = NormalizeDirectoryPath(tempDir);
            string normalizedSourceDir = NormalizeDirectoryPath(sourceDir);
            string normalizedAppDir = NormalizeDirectoryPath(appDir);
            string scriptPath = Path.Combine(normalizedTempDir, "update.bat");
            string logPath = Path.Combine(normalizedAppDir, "update.log");
            string restartExePathForPowerShell = EscapePowerShellSingleQuotedString(restartExePath);
            string restartWorkingDirForPowerShell = EscapePowerShellSingleQuotedString(Path.GetDirectoryName(restartExePath) ?? normalizedAppDir);

            // Batch script: wait for process to exit → copy files → restart app → cleanup
            string script = $@"@echo off
setlocal EnableExtensions
chcp 65001 >nul
set ""LOGFILE={logPath}""
echo [%date% %time%] Waiting for application to close...>> ""%LOGFILE%""
:waitloop
tasklist /FI ""PID eq {currentPid}"" 2>NUL | find ""{currentPid}"" >NUL
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto waitloop
)
echo [%date% %time%] Application closed. Copying update files...>> ""%LOGFILE%""
robocopy ""{normalizedSourceDir}"" ""{normalizedAppDir}"" /E /R:10 /W:1 /NFL /NDL /NJH /NJS /NP >> ""%LOGFILE%"" 2>&1
set ""ROBOCOPY_EXIT=%errorlevel%""
if %ROBOCOPY_EXIT% GEQ 8 (
    echo [%date% %time%] ERROR: Failed to copy update files. Robocopy exit code: %ROBOCOPY_EXIT%>> ""%LOGFILE%""
    pause
    exit /b %ROBOCOPY_EXIT%
)
echo [%date% %time%] Update complete. Restarting application: {restartExePath}>> ""%LOGFILE%""
powershell -NoProfile -ExecutionPolicy Bypass -Command ""Start-Process -FilePath '{restartExePathForPowerShell}' -WorkingDirectory '{restartWorkingDirForPowerShell}'"" >> ""%LOGFILE%"" 2>&1
if errorlevel 1 (
    echo [%date% %time%] ERROR: Failed to restart application.>> ""%LOGFILE%""
    pause
    exit /b 1
)
echo [%date% %time%] Restart command sent successfully.>> ""%LOGFILE%""
timeout /t 2 /nobreak >nul
rmdir /S /Q ""{normalizedTempDir}"" >nul 2>&1
exit /b 0
";
            File.WriteAllText(scriptPath, script, System.Text.Encoding.UTF8);
            return scriptPath;
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
            // Cancel any ongoing update download
            _updateCts?.Cancel();

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