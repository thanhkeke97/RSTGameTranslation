using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RSTGameTranslation
{
    public class CustomApiTranslationService : ITranslationService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static int _consecutiveFailures = 0;
        private static int _retryCount = 0;
        private static readonly object _keySwitchLock = new object();
        private const int MAX_RETRIES = 3;
        private int delayMS = 100;

        /// <summary>
        /// Check if error requires API key switch
        /// </summary>
        private bool ShouldSwitchApiKey(HttpStatusCode statusCode, string errorMessage)
        {
            // Switch key for quota/rate limit/invalid key errors
            if (statusCode == HttpStatusCode.Unauthorized ||  // 401 - Invalid API key
                (int)statusCode == 429 ||  // Too Many Requests - Rate limit
                statusCode == HttpStatusCode.Forbidden)  // 403 - Quota exceeded
            {
                return true;
            }

            // Check error message for quota/rate limit keywords
            string lowerMessage = errorMessage.ToLower();
            if (lowerMessage.Contains("quota") ||
                lowerMessage.Contains("rate limit") ||
                lowerMessage.Contains("rate-limit") ||
                lowerMessage.Contains("invalid api key") ||
                lowerMessage.Contains("api key not found") ||
                lowerMessage.Contains("api key invalid"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get masked API key for logging (show only first 4 and last 4 characters)
        /// </summary>
        private string MaskApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 8)
                return "***";
            return $"{apiKey.Substring(0, 4)}...{apiKey.Substring(apiKey.Length - 4)}";
        }

        /// <summary>
        /// Translate text using the Custom API
        /// </summary>
        /// <param name="jsonData">The JSON data to translate</param>
        /// <param name="prompt">The prompt to guide the translation</param>
        /// <returns>The translation result as a JSON string or null if translation failed</returns>
        public async Task<string?> TranslateAsync(string jsonData, string prompt)
        {
            string apiKey = ConfigManager.Instance.GetCustomApiKey().Trim();
            string currenServices = ConfigManager.Instance.GetCurrentTranslationService();

            // Check retry limit
            if (_retryCount >= MAX_RETRIES)
            {
                Console.WriteLine($"Custom API: Max retries ({MAX_RETRIES}) reached. Giving up.");
                _retryCount = 0;
                return null;
            }

            try
            {
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine("Custom API key not configured");
                    return null;
                }
                // Get model from config
                string model = ConfigManager.Instance.GetCustomApiModel();

                var requestContent = new
                {
                    model = model,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = $"{prompt}\n{jsonData}"
                        }
                    }
                };

                var jsonOptions = new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string requestJson = JsonSerializer.Serialize(requestContent, jsonOptions);
                var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                // Set the API key in the request header
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                string url = ConfigManager.Instance.GetCustomApiUrl().Trim();
                HttpResponseMessage response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    // Reset consecutive failures counter on success
                    _consecutiveFailures = 0;
                    _retryCount = 0;

                    // Log the raw Custom API response before returning it
                    LogManager.Instance.LogLlmReply(jsonResponse);

                    return jsonResponse;
                }
                else
                {
                    string errorMessage = await response.Content.ReadAsStringAsync();
                    _consecutiveFailures++;
                    Console.WriteLine($"Custom API error: {response.StatusCode}, {errorMessage}, error count: {_consecutiveFailures}");

                    // Check if we should switch API key
                    if (ShouldSwitchApiKey(response.StatusCode, errorMessage))
                    {
                        string newApikey = null;
                        lock (_keySwitchLock)
                        {
                            newApikey = ConfigManager.Instance.GetNextApiKey(currenServices, apiKey);
                            if (!string.IsNullOrEmpty(newApikey) && newApikey != apiKey)
                            {
                                ConfigManager.Instance.SetCustomApiKey(newApikey);
                                Console.WriteLine($"Switched API key from {MaskApiKey(apiKey)} to {MaskApiKey(newApikey)}");
                                _retryCount++;
                            }
                            else
                            {
                                Console.WriteLine("No more API keys available to switch to");
                            }
                        }

                        // Retry with new key (outside lock)
                        if (!string.IsNullOrEmpty(newApikey) && newApikey != apiKey)
                        {
                            return await TranslateAsync(jsonData, prompt);
                        }
                    }
                    try
                    {
                        using JsonDocument errorDoc = JsonDocument.Parse(errorMessage);
                        if (errorDoc.RootElement.TryGetProperty("error", out JsonElement errorElement))
                        {
                            string detailedError = "";

                            // Extract error message
                            if (errorElement.TryGetProperty("message", out JsonElement messageElement))
                            {
                                detailedError = messageElement.GetString() ?? "";
                            }
                            // ignore error if it's a rate limit error
                            if (_consecutiveFailures > 3)
                            {
                                // Write error to file
                                System.IO.File.WriteAllText("custom_api_last_error.txt", $"Custom API error: {detailedError}\n\nResponse code: {response.StatusCode}\nFull response: {errorMessage}");

                                // Show error message to user
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    System.Windows.MessageBox.Show(
                                        string.Format(LocalizationManager.Instance.Strings["Msg_CustomApiError"], detailedError),
                                        LocalizationManager.Instance.Strings["Title_CustomApiError"],
                                        System.Windows.MessageBoxButton.OK,
                                        System.Windows.MessageBoxImage.Error);
                                });
                            }
                            await Task.Delay(delayMS);
                            return null;
                        }
                    }
                    catch (JsonException)
                    {
                        // If we can't parse as JSON, just use the raw message
                    }
                    if (_consecutiveFailures > 3)
                    {
                        // Write error to file
                        System.IO.File.WriteAllText("custom_api_last_error.txt", $"Custom API error: {response.StatusCode}\n\nFull response: {errorMessage}");

                        // Show general error if JSON parsing failed
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            System.Windows.MessageBox.Show(
                                string.Format(LocalizationManager.Instance.Strings["Msg_CustomApiErrorStatus"], response.StatusCode, errorMessage),
                                LocalizationManager.Instance.Strings["Title_CustomApiError"],
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Error);
                        });
                    }
                    await Task.Delay(delayMS);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Translation API error: {ex.Message}");

                // Write error to file
                System.IO.File.WriteAllText("custom_api_last_error.txt", $"Custom API error: {ex.Message}\n\nStack trace: {ex.StackTrace}");

                // Show error message to user
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_CustomApiException"], ex.Message),
                        LocalizationManager.Instance.Strings["Title_CustomApiError"],
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                });

                return null;
            }
        }
    }
}