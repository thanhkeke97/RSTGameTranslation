// SupertonicModelDownloader - downloads the on-device Supertonic TTS model
// (ONNX + voice styles) from Hugging Face. Model is OpenRAIL-M licensed.
//
// Pattern: mirrors OllamaModelDownloader (test/check + popup progress window).
// For Supertonic we always need to download (no pre-existing endpoint to
// query), so this class is download-only and exposes a single
// DownloadAsync() entry point.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MessageBox = System.Windows.MessageBox;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace RSTGameTranslation
{
    public class SupertonicModelDownloader
    {
        private const string BaseUrl = "https://huggingface.co/Supertone/supertonic-3/resolve/main/";

        // Files to fetch: 4 ONNX + 2 JSON + 9 voice styles. Voice styles are
        // tiny (~1-2 MB each) and bundle nicely with the first download so the
        // user can switch voices without re-fetching.
        private static readonly string[] RequiredFiles =
        {
            "onnx/duration_predictor.onnx",
            "onnx/text_encoder.onnx",
            "onnx/vector_estimator.onnx",
            "onnx/vocoder.onnx",
            "onnx/tts.json",
            "onnx/unicode_indexer.json",
            "voice_styles/M1.json",
            "voice_styles/M2.json",
            "voice_styles/M3.json",
            "voice_styles/M4.json",
            "voice_styles/M5.json",
            "voice_styles/F1.json",
            "voice_styles/F2.json",
            "voice_styles/F3.json",
            "voice_styles/F4.json",
            "voice_styles/F5.json",
        };

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15) // per-file timeout (large ONNX ~150 MB)
        };

        private ProgressBar? _progressBar;
        private TextBlock? _statusText;
        private Window? _statusWindow;
        private bool _isDownloading = false;
        private CancellationTokenSource? _cts;

        /// <summary>
        /// Downloads all Supertonic model files into the model root directory
        /// (typically &lt;app&gt;/Supertonic/). Skips files that already exist
        /// and match the remote size. Returns true if every required file is
        /// present at the end.
        /// </summary>
        public async Task<bool> DownloadAsync()
        {
            if (_isDownloading)
            {
                MessageBox.Show("A Supertonic model download is already in progress.",
                    "Download in progress", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            string modelRoot = SupertonicTTSService.GetModelRoot();
            try
            {
                _isDownloading = true;
                _cts = new CancellationTokenSource();
                ShowStatusWindow();

                // First, resolve the size of each remote file (HEAD request).
                // We use this both for progress reporting and to skip files
                // that have already been fully downloaded.
                var fileInfos = new List<(string Rel, long Size)>();
                UpdateStatus("Checking model files...", 0, indeterminate: true);

                foreach (var rel in RequiredFiles)
                {
                    long size = await GetRemoteSizeAsync(rel);
                    if (size <= 0)
                        throw new InvalidOperationException($"Could not determine size of {rel}");
                    fileInfos.Add((rel, size));
                }

                long totalBytes = fileInfos.Sum(f => f.Size);
                long completedBytes = 0;
                int fileIndex = 0;
                int fileCount = fileInfos.Count;

                foreach (var (rel, size) in fileInfos)
                {
                    fileIndex++;
                    string localPath = Path.Combine(modelRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                    if (File.Exists(localPath) && new FileInfo(localPath).Length == size)
                    {
                        Console.WriteLine($"Supertonic: {rel} already present ({size} bytes), skipping");
                        completedBytes += size;
                        UpdateStatus($"[{fileIndex}/{fileCount}] {rel} (cached)",
                            (int)(completedBytes * 100 / totalBytes));
                        continue;
                    }

                    UpdateStatus($"[{fileIndex}/{fileCount}] Downloading {rel} ({FormatSize(size)})...",
                        (int)(completedBytes * 100 / totalBytes));

                    long written = await DownloadFileAsync(rel, localPath, size, fileIndex, fileCount,
                        completedBytes, totalBytes, _cts.Token);
                    completedBytes += written;
                }

                UpdateStatus("Download complete!", 100);
                await Task.Delay(1500);
                CloseStatusWindow();
                return true;
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Download cancelled.", 0);
                await Task.Delay(1500);
                CloseStatusWindow();
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Supertonic download error: {ex.Message}");
                CloseStatusWindow();
                MessageBox.Show(
                    $"Error downloading Supertonic model: {ex.Message}",
                    "Download error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            finally
            {
                _isDownloading = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        public void Cancel()
        {
            try { _cts?.Cancel(); } catch { }
        }

        // ============================================================
        // Internal
        // ============================================================

        private async Task<long> GetRemoteSizeAsync(string relPath)
        {
            string url = BaseUrl + relPath;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Head, url);
                    using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    if (resp.IsSuccessStatusCode)
                        return resp.Content.Headers.ContentLength ?? 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"HEAD {relPath} attempt {attempt} failed: {ex.Message}");
                }
                await Task.Delay(1000 * attempt);
            }
            return 0;
        }

        private async Task<long> DownloadFileAsync(string relPath, string localPath, long expectedSize,
            int fileIndex, int fileCount, long baseBytes, long totalBytes, CancellationToken token)
        {
            string url = BaseUrl + relPath;
            const int maxRetries = 3;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    long existing = File.Exists(localPath) ? new FileInfo(localPath).Length : 0;
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    if (existing > 0 && expectedSize > 0)
                        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);

                    using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
                    resp.EnsureSuccessStatusCode();

                    bool isResume = existing > 0 && existing < expectedSize;
                    using var src = await resp.Content.ReadAsStreamAsync(token);

                    FileStream fs;
                    if (isResume)
                    {
                        fs = new FileStream(localPath, FileMode.Append, FileAccess.Write, FileShare.None);
                    }
                    else
                    {
                        fs = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        existing = 0;
                    }

                    try
                    {
                        byte[] buffer = new byte[64 * 1024];
                        long fileWritten = existing;
                        int read;
                        long lastReport = 0;
                        while ((read = await src.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                        {
                            await fs.WriteAsync(buffer, 0, read, token);
                            fileWritten += read;
                            long overall = baseBytes + fileWritten;
                            int overallPct = totalBytes > 0 ? (int)(overall * 100 / totalBytes) : 0;
                            if (fileWritten - lastReport > 256 * 1024 || fileWritten == expectedSize)
                            {
                                lastReport = fileWritten;
                                UpdateStatus(
                                    $"[{fileIndex}/{fileCount}] {relPath} - {FormatSize(fileWritten)}/{FormatSize(expectedSize)}",
                                    overallPct);
                            }
                        }
                        return fileWritten;
                    }
                    finally
                    {
                        await fs.FlushAsync(token);
                        fs.Close();
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Download {relPath} attempt {attempt} failed: {ex.Message}");
                    if (attempt == maxRetries) throw;
                    await Task.Delay(1500 * attempt, token);
                }
            }
            return 0;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private void ShowStatusWindow()
        {
            if (_statusWindow != null)
            {
                _statusWindow.Show();
                return;
            }

            _statusWindow = new Window
            {
                Title = "Supertonic Model Download",
                Width = 460,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Topmost = true
            };
            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = "Preparing download...",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = System.Windows.Media.Brushes.Black
            };
            Grid.SetRow(_statusText, 0);

            _progressBar = new ProgressBar
            {
                Height = 22,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            Grid.SetRow(_progressBar, 1);

            grid.Children.Add(_statusText);
            grid.Children.Add(_progressBar);
            _statusWindow.Content = grid;
            _statusWindow.Show();
        }

        private void UpdateStatus(string message, int percent, bool indeterminate = false)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_statusText != null) _statusText.Text = message;
                if (_progressBar != null)
                {
                    _progressBar.IsIndeterminate = indeterminate;
                    if (!indeterminate) _progressBar.Value = percent;
                }
            });
        }

        private void CloseStatusWindow()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _statusWindow?.Close();
                _statusWindow = null;
                _progressBar = null;
                _statusText = null;
            });
        }
    }
}
