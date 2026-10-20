using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RSTGameTranslation
{
    public class MistralTranslationService : ITranslationService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        /// <summary>Open the HTTPS connection before the first translation (see TranslationWarmUp).</summary>
        internal static Task WarmUpConnectionAsync() =>
            TranslationWarmUp.OpenConnectionsAsync(_httpClient, "Mistral", "https://api.mistral.ai/");
        private static int _consecutiveFailures = 0;
        private int delayMS = 100;
        
        /// <summary>
        /// Translate text using the Mistral API
        /// </summary>
        /// <param name="jsonData">The JSON data to translate</param>
        /// <param name="prompt">The prompt to guide the translation</param>
        /// <returns>The translation result as a JSON string or null if translation failed</returns>
        public Task<string?> TranslateAsync(string jsonData, string prompt) =>
            TranslateAsync(jsonData, prompt, ConfigManager.Instance.GetMistralApiKey(), new HashSet<string>());

        // triedKeys: keys this request already failed with, so a key switch never retries one
        private async Task<string?> TranslateAsync(string jsonData, string prompt, string apiKey, HashSet<string> triedKeys)
        {
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

                string url = "https://api.mistral.ai/v1/chat/completions";
                // The key goes on this request only: the HttpClient is shared by concurrent requests
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    // Reset consecutive failures counter on success
                    _consecutiveFailures = 0;
                    TranslationErrorNotifier.Reset("Mistral");

                    // Log the raw Mistral response before returning it
                    LogManager.Instance.LogLlmReply(jsonResponse);

                    return jsonResponse;
                }
                else
                {
                    string errorMessage = await response.Content.ReadAsStringAsync();
                    _consecutiveFailures++;
                    Console.WriteLine($"Mistral API error: {response.StatusCode}, {errorMessage}, error count: {_consecutiveFailures}");

                    // Rate limit, quota or invalid key: retry with another key
                    if (ApiKeyRotation.ShouldSwitchApiKey(response.StatusCode, errorMessage) &&
                        ApiKeyRotation.GetKeyToRetry("Mistral", apiKey, triedKeys) is string nextKey)
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
                                System.IO.File.WriteAllText("mistral_last_error.txt", $"Mistral API error: {detailedError}\n\nResponse code: {response.StatusCode}\nFull response: {errorMessage}");
                                
                                // Show error message to user
                                TranslationErrorNotifier.ShowOnce("Mistral",
                                    string.Format(LocalizationManager.Instance.Strings["Msg_MistralApiError"], detailedError),
                                    LocalizationManager.Instance.Strings["Title_MistralError"]);
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
                        TranslationErrorNotifier.ShowOnce("Mistral",
                            string.Format(LocalizationManager.Instance.Strings["Msg_MistralApiErrorStatus"], response.StatusCode, errorMessage),
                            LocalizationManager.Instance.Strings["Title_MistralError"]);
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
                TranslationErrorNotifier.ShowOnce("Mistral",
                    string.Format(LocalizationManager.Instance.Strings["Msg_MistralApiException"], ex.Message),
                    LocalizationManager.Instance.Strings["Title_MistralError"]);
                
                return null;
            }
        }
    }
}