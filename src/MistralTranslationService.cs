using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RSTGameTranslation
{
    public class MistralTranslationService : ITranslationService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static int _consecutiveFailures = 0;
        private int delayMS = 100;
        
        /// <summary>
        /// Translate text using the Mistral API
        /// </summary>
        /// <param name="jsonData">The JSON data to translate</param>
        /// <param name="prompt">The prompt to guide the translation</param>
        /// <returns>The translation result as a JSON string or null if translation failed</returns>
        public async Task<string?> TranslateAsync(string jsonData, string prompt)
        {
            string apiKey = ConfigManager.Instance.GetMistralApiKey();
            string currenServices = ConfigManager.Instance.GetCurrentTranslationService();
            try
            {
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine("Mistral API key not configured");
                    return null;
                }
                // Get model from config
                string model = ConfigManager.Instance.GetMistralModel();

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

                string url = "https://api.mistral.ai/v1/chat/completions";
                HttpResponseMessage response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    // Reset consecutive failures counter on success
                    _consecutiveFailures = 0;
                    
                    // Log the raw Mistral response before returning it
                    LogManager.Instance.LogLlmReply(jsonResponse);
                    
                    return jsonResponse;
                }
                else
                {
                    string errorMessage = await response.Content.ReadAsStringAsync();
                    _consecutiveFailures++;
                    Console.WriteLine($"Mistral API error: {response.StatusCode}, {errorMessage}, error count: {_consecutiveFailures}");
                    // Increment consecutive failures counter
                    // Try to parse the error message from JSON if possible
                    string newApikey = ConfigManager.Instance.GetNextApiKey(currenServices, apiKey);
                    ConfigManager.Instance.SetMistralApiKey(newApikey);
                    Console.WriteLine("Change new api key successfully");
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
                                System.IO.File.WriteAllText("mistral_last_error.txt", $"Mistral API error: {detailedError}\n\nResponse code: {response.StatusCode}\nFull response: {errorMessage}");
                                
                                // Show error message to user
                                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                                    System.Windows.MessageBox.Show(
                                        string.Format(LocalizationManager.Instance.Strings["Msg_MistralApiError"], detailedError),
                                        LocalizationManager.Instance.Strings["Title_MistralError"],
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
                        System.IO.File.WriteAllText("mistral_last_error.txt", $"Mistral API error: {response.StatusCode}\n\nFull response: {errorMessage}");
                        
                        // Show general error if JSON parsing failed
                        System.Windows.Application.Current.Dispatcher.Invoke(() => {
                            System.Windows.MessageBox.Show(
                                string.Format(LocalizationManager.Instance.Strings["Msg_MistralApiErrorStatus"], response.StatusCode, errorMessage),
                                LocalizationManager.Instance.Strings["Title_MistralError"],
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
                System.IO.File.WriteAllText("mistral_last_error.txt", $"Mistral API error: {ex.Message}\n\nStack trace: {ex.StackTrace}");
                
                // Show error message to user
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    System.Windows.MessageBox.Show(
                        string.Format(LocalizationManager.Instance.Strings["Msg_MistralApiException"], ex.Message),
                        LocalizationManager.Instance.Strings["Title_MistralError"],
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                });
                
                return null;
            }
        }
    }
}