using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace UGTLive
{
    public class GeminiTranslationService : ITranslationService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        
        /// <summary>
        /// Translate text using the Gemini API
        /// </summary>
        /// <param name="jsonData">The JSON data to translate</param>
        /// <param name="prompt">The prompt to guide the translation</param>
        /// <returns>The translation result as a JSON string or null if translation failed</returns>
        public async Task<string?> TranslateAsync(string jsonData, string prompt)
        {
            try
            {
                string apiKey = ConfigManager.Instance.GetGeminiApiKey();
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine("Gemini API key not configured");
                    return null;
                }

                var requestContent = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new
                                {
                                    text = $"{prompt}\n{jsonData}"
                                }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        response_mime_type = "application/json",
                    }
                };

                var jsonOptions = new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string requestJson = JsonSerializer.Serialize(requestContent, jsonOptions);
                var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                // Get model from config
                string model = ConfigManager.Instance.GetGeminiModel();
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                HttpResponseMessage response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    
                    // Log the raw Gemini response before returning it
                    LogManager.Instance.LogLlmReply(jsonResponse);
                    
                    return jsonResponse;
                }
                else
                {
                    string errorMessage = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Gemini API error: {response.StatusCode}, {errorMessage}");
                    
                    // Try to parse the error message from JSON if possible
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
                            
                            // Show error message to user
                            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                                System.Windows.MessageBox.Show(
                                    $"Gemini API error: {detailedError}\n\nPlease check your API key and settings.",
                                    "Gemini Translation Error",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Error);
                            });
                            
                            return null;
                        }
                    }
                    catch (JsonException)
                    {
                        // If we can't parse as JSON, just use the raw message
                    }
                    
                    // Show general error if JSON parsing failed
                    System.Windows.Application.Current.Dispatcher.Invoke(() => {
                        System.Windows.MessageBox.Show(
                            $"Gemini API error: {response.StatusCode}\n{errorMessage}\n\nPlease check your API key and settings.",
                            "Gemini Translation Error",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error);
                    });
                    
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Translation API error: {ex.Message}");
                
                // Show error message to user
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    System.Windows.MessageBox.Show(
                        $"Gemini API error: {ex.Message}\n\nPlease check your network connection and API key.",
                        "Gemini Translation Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                });
                
                return null;
            }
        }
    }
}