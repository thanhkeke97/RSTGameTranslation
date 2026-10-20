using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RSTGameTranslation
{
    public class GroqTranslationService : ITranslationService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        /// <summary>Open the HTTPS connection before the first translation (see TranslationWarmUp).</summary>
        internal static Task WarmUpConnectionAsync() =>
            TranslationWarmUp.OpenConnectionsAsync(_httpClient, "Groq", "https://api.groq.com/");
        private static int _consecutiveFailures = 0;
        private int delayMS = 100;
        
        /// <summary>
        /// Translate text using the Groq API
        /// </summary>
        /// <param name="jsonData">The JSON data to translate</param>
        /// <param name="prompt">The prompt to guide the translation</param>
        /// <returns>The translation result as a JSON string or null if translation failed</returns>
        public Task<string?> TranslateAsync(string jsonData, string prompt) =>
            TranslateAsync(jsonData, prompt, ConfigManager.Instance.GetGroqApiKey(), new HashSet<string>());

        // triedKeys: keys this request already failed with, so a key switch never retries one
        private async Task<string?> TranslateAsync(string jsonData, string prompt, string apiKey, HashSet<string> triedKeys)
        {
            try
            {
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine("Groq API key not configured");
                    return null;
                }
                // Get model from config
                string model = ConfigManager.Instance.GetGroqModel();

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

                string url = "https://api.groq.com/openai/v1/chat/completions";
                // The key goes on this request only: the HttpClient is shared by concurrent requests
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    // Reset consecutive failures counter on success
                    _consecutiveFailures = 0;
                    TranslationErrorNotifier.Reset("Groq");

                    // Log the raw Groq response before returning it
                    LogManager.Instance.LogLlmReply(jsonResponse);

                    return jsonResponse;
                }
                else
                {
                    string errorMessage = await response.Content.ReadAsStringAsync();
                    _consecutiveFailures++;
                    Console.WriteLine($"Groq API error: {response.StatusCode}, {errorMessage}, error count: {_consecutiveFailures}");

                    // Rate limit, quota or invalid key: retry with another key
                    if (ApiKeyRotation.ShouldSwitchApiKey(response.StatusCode, errorMessage) &&
                        ApiKeyRotation.GetKeyToRetry("Groq", apiKey, triedKeys) is string nextKey)
                    {
                        return await TranslateAsync(jsonData, prompt, nextKey, triedKeys);
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
                                System.IO.File.WriteAllText("groq_last_error.txt", $"Groq API error: {detailedError}\n\nResponse code: {response.StatusCode}\nFull response: {errorMessage}");
                                
                                // Show error message to user
                                TranslationErrorNotifier.ShowOnce("Groq",
                                    string.Format(LocalizationManager.Instance.Strings["Msg_GroqApiError"], detailedError),
                                    LocalizationManager.Instance.Strings["Title_GroqError"]);
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
                        System.IO.File.WriteAllText("groq_last_error.txt", $"Groq API error: {response.StatusCode}\n\nFull response: {errorMessage}");
                        
                        // Show general error if JSON parsing failed
                        TranslationErrorNotifier.ShowOnce("Groq",
                            string.Format(LocalizationManager.Instance.Strings["Msg_GroqApiErrorStatus"], response.StatusCode, errorMessage),
                            LocalizationManager.Instance.Strings["Title_GroqError"]);
                    }
                    await Task.Delay(delayMS);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Translation API error: {ex.Message}");
                
                // Write error to file
                System.IO.File.WriteAllText("groq_last_error.txt", $"Groq API error: {ex.Message}\n\nStack trace: {ex.StackTrace}");
                
                // Show error message to user
                TranslationErrorNotifier.ShowOnce("Groq",
                    string.Format(LocalizationManager.Instance.Strings["Msg_GroqApiException"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_GroqError"]);
                
                return null;
            }
        }
    }
}