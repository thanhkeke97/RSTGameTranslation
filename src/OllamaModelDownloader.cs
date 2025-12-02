using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MessageBox = System.Windows.MessageBox;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace RSTGameTranslation
{
    public class OllamaModelDownloader
    {
        private ProgressBar? _modelProgressBar;
        private TextBlock? _modelStatusText;
        private Window? _modelStatusWindow;
        private bool _isModelDownloading = false;
        
        public async Task<bool> TestAndDownloadModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_PleaseEnterModelName"],
                    LocalizationManager.Instance.Strings["Title_ModelError"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
            
            // Check if already downloading
            if (_isModelDownloading)
            {
                MessageBox.Show(
                    LocalizationManager.Instance.Strings["Msg_ModelDownloadInProgress"],
                    LocalizationManager.Instance.Strings["Title_DownloadInProgress"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }
            
            try
            {
                // Create or show the status window
                ShowModelStatusWindow("Testing model...");
                
                // Check if model exists
                bool modelExists = await CheckIfModelExists(model);
                
                if (modelExists)
                {
                    // Show success and close window after delay
                    UpdateModelStatus("Model is available", 100);
                    await Task.Delay(2000);
                    CloseModelStatusWindow();
                    return true;
                }
                else
                {
                    // Ask if user wants to download the model
                    _modelStatusWindow?.Hide();
                    
                    var result = MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_ModelNotFoundDownload"], model),
                        LocalizationManager.Instance.Strings["Title_DownloadModel"],
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                        
                    if (result == MessageBoxResult.Yes)
                    {
                        // Show download status window
                        _isModelDownloading = true;
                        ShowModelStatusWindow($"Downloading model {model}...");
                        
                        // Start download and monitor progress
                        await DownloadModel(model);
                        return true;
                    }
                    else
                    {
                        CloseModelStatusWindow();
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                CloseModelStatusWindow();
                MessageBox.Show(
                    string.Format(LocalizationManager.Instance.Strings["Msg_ErrorCheckingModel"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_Error"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Console.WriteLine($"Error checking/downloading model: {ex.Message}");
                return false;
            }
        }
        
        private void ShowModelStatusWindow(string initialMessage)
        {
            // Create a new window if it doesn't exist
            if (_modelStatusWindow == null)
            {
                _modelStatusWindow = new Window
                {
                    Title = LocalizationManager.Instance.Strings["Title_OllamaModelStatus"],
                    Width = 400,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.ToolWindow
                };
                
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
                
                // Status text
                _modelStatusText = new TextBlock
                {
                    Text = initialMessage,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                    Margin = new Thickness(10)
                };
                
                // Progress bar
                _modelProgressBar = new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Height = 20,
                    Margin = new Thickness(10),
                    IsIndeterminate = true
                };
                
                grid.Children.Add(_modelStatusText);
                Grid.SetRow(_modelStatusText, 0);
                
                grid.Children.Add(_modelProgressBar);
                Grid.SetRow(_modelProgressBar, 1);
                
                _modelStatusWindow.Content = grid;
                
                // Handle closing
                _modelStatusWindow.Closing += (s, e) => 
                {
                    if (_isModelDownloading)
                    {
                        e.Cancel = true;
                        MessageBox.Show(
                            LocalizationManager.Instance.Strings["Msg_PleaseWaitDownload"],
                            LocalizationManager.Instance.Strings["Title_DownloadInProgress"],
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                };
            }
            
            // Update the message if window already exists
            if (_modelStatusText != null)
            {
                _modelStatusText.Text = initialMessage;
            }
            
            // Reset progress bar
            if (_modelProgressBar != null)
            {
                _modelProgressBar.IsIndeterminate = true;
                _modelProgressBar.Value = 0;
            }
            
            // Show the window
            _modelStatusWindow.Show();
            _modelStatusWindow.Activate();
        }
        
        private void UpdateModelStatus(string message, int progressValue = -1)
        {
            if (_modelStatusText != null)
            {
                _modelStatusText.Text = message;
            }
            
            if (_modelProgressBar != null)
            {
                if (progressValue >= 0)
                {
                    _modelProgressBar.IsIndeterminate = false;
                    _modelProgressBar.Value = progressValue;
                }
                else
                {
                    _modelProgressBar.IsIndeterminate = true;
                }
            }
        }
        
        private void CloseModelStatusWindow()
        {
            _isModelDownloading = false;
            
            if (_modelStatusWindow != null && _modelStatusWindow.IsVisible)
            {
                _modelStatusWindow.Close();
                _modelStatusWindow = null;
                _modelStatusText = null;
                _modelProgressBar = null;
            }
        }
        
        private async Task<bool> CheckIfModelExists(string model)
        {
            try
            {
                string ollamaUrl = ConfigManager.Instance.GetOllamaUrl();
                string ollamaPort = ConfigManager.Instance.GetOllamaPort();
                // Correctly format the URL
                if (!ollamaUrl.StartsWith("http://") && !ollamaUrl.StartsWith("https://"))
                {
                    ollamaUrl = "http://" + ollamaUrl;
                }
                
                string apiUrl = $"{ollamaUrl}:{ollamaPort}/api/tags";
                Console.WriteLine($"Checking models from URL: {apiUrl}");
                
                using (var client = new HttpClient())
                {
                    // Set a reasonable timeout
                    client.Timeout = TimeSpan.FromSeconds(30);
                    HttpResponseMessage response = await client.GetAsync(apiUrl);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"Response from Ollama tags API: {jsonResponse}");
                        
                        using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                        
                        // Check if the model exists in the Ollama tags list
                        if (doc.RootElement.TryGetProperty("models", out JsonElement modelsElement))
                        {
                            foreach (JsonElement modelElement in modelsElement.EnumerateArray())
                            {
                                if (modelElement.TryGetProperty("name", out JsonElement nameElement))
                                {
                                    string modelName = nameElement.GetString() ?? "";
                                    Console.WriteLine($"Found installed model: {modelName}");
                                    if (modelName.Equals(model, StringComparison.OrdinalIgnoreCase))
                                    {
                                        Console.WriteLine($"Model {model} exists in Ollama");
                                        return true;
                                    }
                                }
                            }
                        }
                        
                        return false;
                    }
                    else
                    {
                        string errorMessage = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"Ollama API error: {response.StatusCode}, {errorMessage}");
                        throw new Exception($"Ollama API error: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking model: {ex.Message}");
                throw;
            }
        }
        
        private async Task DownloadModel(string model)
        {
            try
            {
                string ollamaUrl = ConfigManager.Instance.GetOllamaUrl();
                string ollamaPort = ConfigManager.Instance.GetOllamaPort();
                // Correctly format the URL
                if (!ollamaUrl.StartsWith("http://") && !ollamaUrl.StartsWith("https://"))
                {
                    ollamaUrl = "http://" + ollamaUrl;
                }
                
                string apiUrl = $"{ollamaUrl}:{ollamaPort}/api/pull";
                Console.WriteLine($"Downloading model from URL: {apiUrl}");
                
                // Prepare the pull request
                var requestContent = new
                {
                    name = model,
                    stream = true
                };
                
                // Log the request for debugging
                Console.WriteLine($"Sending request to download model: {model}");
                Console.WriteLine($"Request content: {JsonSerializer.Serialize(requestContent)}");
                
                string requestJson = JsonSerializer.Serialize(requestContent);
                var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
                
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromHours(2); // Long timeout for large models
                    
                    HttpResponseMessage response = await client.PostAsync(apiUrl, content);
                    
                    if (response.IsSuccessStatusCode)
                    {                        
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var reader = new System.IO.StreamReader(stream))
                        {
                            string? line;
                            int lastProgress = 0;
                            
                            Console.WriteLine("Starting to read response stream...");
                            while ((line = await reader.ReadLineAsync()) != null)
                            {
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                
                                Console.WriteLine($"Received line: {line.Substring(0, Math.Min(100, line.Length))}...");
                                
                                try {
                                    using JsonDocument doc = JsonDocument.Parse(line);
                                    
                                    // Extract status information
                                    if (doc.RootElement.TryGetProperty("status", out JsonElement statusElement))
                                    {
                                        string status = statusElement.GetString() ?? "";
                                        
                                        // Check for download progress
                                        if (doc.RootElement.TryGetProperty("completed", out JsonElement completedElement) &&
                                            doc.RootElement.TryGetProperty("total", out JsonElement totalElement))
                                        {
                                            long completed = completedElement.GetInt64();
                                            long total = totalElement.GetInt64();
                                            
                                            if (total > 0)
                                            {
                                                int progress = (int)((completed * 100) / total);
                                                
                                                // Only update status if progress has changed significantly
                                                if (progress >= lastProgress + 5 || progress == 100)
                                                {
                                                    lastProgress = progress;
                                                    Console.WriteLine($"Download progress: {progress}%");
                                                    UpdateModelStatus($"Downloading {model}: {progress}%", progress);
                                                }
                                            }
                                        }
                                        else 
                                        {
                                            // Just display the status message
                                            UpdateModelStatus($"Status: {status}");
                                        }
                                    }
                                    
                                    // Check for completion
                                    if (doc.RootElement.TryGetProperty("digest", out JsonElement digestElement))
                                    {
                                        // Digest property means download is complete
                                        UpdateModelStatus($"Model {model} downloaded successfully!", 100);
                                        break;
                                    }
                                }
                                catch (JsonException ex) 
                                {
                                    // Log and skip invalid JSON lines
                                    Console.WriteLine($"Invalid JSON: {ex.Message}, Line: {line}");
                                    continue;
                                }
                            }
                        }
                        
                        // Verify the model was really downloaded by checking again
                        Console.WriteLine("Verifying model was downloaded...");
                        bool verifyDownloaded = await CheckIfModelExists(model);
                        
                        if (verifyDownloaded)
                        {
                            Console.WriteLine($"Model {model} verified as downloaded");
                            // Show success message and close window after delay
                            await Task.Delay(3000);
                            CloseModelStatusWindow();
                            MessageBox.Show(
                                string.Format(LocalizationManager.Instance.Strings["Msg_ModelDownloadSuccess"], model),
                                LocalizationManager.Instance.Strings["Title_DownloadComplete"],
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                        else
                        {
                            // Something went wrong - model still doesn't exist
                            Console.WriteLine($"WARNING: Model {model} was not found after download completed");
                            CloseModelStatusWindow();
                            MessageBox.Show(
                                string.Format(LocalizationManager.Instance.Strings["Msg_ModelDownloadIssue"], model),
                                LocalizationManager.Instance.Strings["Title_DownloadIssue"],
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        string errorMessage = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"Ollama API error: {response.StatusCode}, {errorMessage}");
                        throw new Exception($"Ollama API error: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading model: {ex.Message}");
                _isModelDownloading = false;
                throw;
            }
            finally
            {
                _isModelDownloading = false;
            }
        }
    }
}