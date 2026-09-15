using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.IO;
using System.Text.RegularExpressions;

namespace RSTGameTranslation
{
    /// <summary>
    /// Translation service that uses Google Translate API
    /// </summary>
    public class GoogleTranslateService : ITranslationService
    {
        private const string CombinedBlockSeparator = "##|||##";

        // Shared HttpClient — HttpClient is thread-safe and designed to be reused.
        // Creating a new instance per service can lead to socket exhaustion under load.
        private static readonly HttpClient _httpClient = CreateSharedHttpClient();

        // Lightweight throttle so we don't burst the free endpoint.
        private static readonly object _throttleLock = new object();
        private static DateTime _lastFreeRequestUtc = DateTime.MinValue;
        private const int MinFreeRequestIntervalMs = 200;
        private const int MaxFreeRetriesPerEndpoint = 2;

        // Hard budget per text block. If we exceed this we abort retries and
        // return null so the user sees the source text instead of a long wait.
        private const int MaxPerBlockDurationMs = 6000;

        // Per-request HTTP timeout. Keep this short — long timeouts compound
        // across retries and blocks and produce the "pending forever" UX bug.
        private const int FreeRequestTimeoutMs = 8000;

        // Endpoint variants tried in order. The `gtx` client is the canonical one
        // but is aggressively rate-limited; the other variants ride the same
        // backend with different identifiers and tend to have independent quotas.
        private static readonly string[] FreeEndpointTemplates = new[]
        {
            "https://translate.googleapis.com/translate_a/single?client=gtx&sl={0}&tl={1}&dt=t&q={2}",
            "https://translate.googleapis.com/translate_a/single?client=webapp&sl={0}&tl={1}&dt=t&q={2}",
            "https://translate.googleapis.com/translate_a/single?sl={0}&tl={1}&dt=t&q={2}"
        };

        private readonly string _apiKey;
        private readonly bool _useCloudApi;
        private readonly bool _autoMapLanguages;
        public static Regex? GoogleTranslateResultRegex { get; set; }

        public GoogleTranslateService()
        {
            _apiKey = ConfigManager.Instance.GetGoogleTranslateApiKey();
            _useCloudApi = ConfigManager.Instance.GetGoogleTranslateUseCloudApi();
            _autoMapLanguages = ConfigManager.Instance.GetGoogleTranslateAutoMapLanguages();
        }

        private static HttpClient CreateSharedHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(FreeRequestTimeoutMs)
            };
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                client.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            }
            return client;
        }

        /// <summary>
        /// Translate text using Google Translate API
        /// </summary>
        public async Task<string?> TranslateAsync(string jsonData, string prompt)
        {
            try
            {
                // Analyze the input JSON data
                using JsonDocument doc = JsonDocument.Parse(jsonData);
                JsonElement root = doc.RootElement;

                // Create a new JSON object for the output
                // We will copy the metadata if exists, and then add the translations
                var options = new JsonSerializerOptions { WriteIndented = true };
                using var memoryStream = new MemoryStream();
                using var outputJson = new System.Text.Json.Utf8JsonWriter(memoryStream);
                
                outputJson.WriteStartObject();
                
                // Copy metadata if exists
                if (root.TryGetProperty("metadata", out JsonElement metadata))
                {
                    outputJson.WritePropertyName("metadata");
                    JsonSerializer.Serialize(outputJson, metadata, options);
                }
                
                // Get source and target languages
                // Default to Japanese and English if not specified
                string sourceLanguage = root.TryGetProperty("source_language", out JsonElement srcLang) 
                    ? srcLang.GetString() ?? "ja" 
                    : ConfigManager.Instance.GetSourceLanguage();
                    
                string targetLanguage = root.TryGetProperty("target_language", out JsonElement tgtLang) 
                    ? tgtLang.GetString() ?? "en" 
                    : ConfigManager.Instance.GetTargetLanguage();
                
                // Mapping languages to Google codes
                if (_autoMapLanguages)
                {
                    sourceLanguage = MapLanguageToGoogleCode(sourceLanguage);
                    targetLanguage = MapLanguageToGoogleCode(targetLanguage);
                }
                
                // Starting translation
                outputJson.WritePropertyName("translations");
                outputJson.WriteStartArray();
                
                int attemptedCount = 0;
                int successCount = 0;

                if (root.TryGetProperty("text_blocks", out JsonElement textBlocks))
                {
                    foreach (JsonElement block in textBlocks.EnumerateArray())
                    {
                        string originalText = "";
                        string blockId = "";

                        if (block.TryGetProperty("text", out JsonElement text))
                        {
                            originalText = text.GetString() ?? "";
                        }

                        if (block.TryGetProperty("id", out JsonElement id))
                        {
                            blockId = id.GetString() ?? "";
                        }

                        // Ignore empty blocks
                        if (string.IsNullOrWhiteSpace(originalText))
                        {
                            continue;
                        }

                        attemptedCount++;

                        // Perform translation. TranslatePreservingSeparatorsAsync
                        // falls back to the source text when a block fails, so we
                        // compare against the source to detect real success.
                        string translatedText = await TranslatePreservingSeparatorsAsync(originalText, sourceLanguage, targetLanguage);
                        if (string.IsNullOrEmpty(translatedText))
                        {
                            translatedText = originalText;
                        }
                        else if (!string.Equals(translatedText, originalText, StringComparison.Ordinal))
                        {
                            successCount++;
                        }

                        // Write the translated text to the output JSON
                        outputJson.WriteStartObject();
                        outputJson.WriteString("id", blockId);
                        outputJson.WriteString("original_text", originalText);
                        outputJson.WriteString("translated_text", translatedText);
                        outputJson.WriteEndObject();

                        // Log for debugging
                        Console.WriteLine($"Google translated: '{originalText}' -> '{translatedText}'");
                    }
                }

                outputJson.WriteEndArray();
                outputJson.WriteEndObject();

                // Transfer the JSON to a string and return it
                outputJson.Flush();
                string result = Encoding.UTF8.GetString(memoryStream.ToArray());

                // If every block failed to translate, signal a complete failure
                // so the caller (Logic) can surface the error instead of silently
                // displaying the source language as if it were the translation.
                if (attemptedCount > 0 && successCount == 0)
                {
                    Console.WriteLine($"Google Translate: all {attemptedCount} block(s) failed; returning null to caller");
                    return null;
                }

                // Log
                Console.WriteLine($"Google Translate final JSON result: {result.Substring(0, Math.Min(100, result.Length))}...");

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GoogleTranslateService: {ex.Message}");
                return null;
            }
        }

        private async Task<string> TranslatePreservingSeparatorsAsync(string text, string sourceLanguage, string targetLanguage)
        {
            try
            {
                if (!text.Contains(CombinedBlockSeparator, StringComparison.Ordinal))
                {
                    string? single = await TranslateSingleTextAsync(text, sourceLanguage, targetLanguage);
                    return string.IsNullOrEmpty(single) ? text : single;
                }

                string[] originalParts = text.Split(new[] { CombinedBlockSeparator }, StringSplitOptions.None);
                string[] translatedParts = new string[originalParts.Length];
                var translatableParts = new List<string>();
                var partMap = new List<int>(originalParts.Length);

                for (int i = 0; i < originalParts.Length; i++)
                {
                    string normalizedPart = NormalizeText(originalParts[i]);

                    if (string.IsNullOrWhiteSpace(normalizedPart))
                    {
                        partMap.Add(-1);
                        continue;
                    }

                    partMap.Add(translatableParts.Count);
                    translatableParts.Add(normalizedPart);
                }

                if (translatableParts.Count == 0)
                {
                    return text;
                }

                List<string?> translatedBatch;
                if (_useCloudApi)
                {
                    translatedBatch = await TranslateBatchWithCloudApiAsync(translatableParts, sourceLanguage, targetLanguage);
                }
                else
                {
                    translatedBatch = new List<string?>(translatableParts.Count);
                    foreach (string part in translatableParts)
                    {
                        translatedBatch.Add(await TranslateWithFreeServiceAsync(part, sourceLanguage, targetLanguage));
                    }
                }

                for (int i = 0; i < originalParts.Length; i++)
                {
                    int translatedIndex = partMap[i];
                    if (translatedIndex < 0 || translatedIndex >= translatedBatch.Count)
                    {
                        translatedParts[i] = originalParts[i];
                        continue;
                    }

                    string? translatedPart = translatedBatch[translatedIndex];
                    translatedParts[i] = string.IsNullOrWhiteSpace(translatedPart) ? originalParts[i] : translatedPart;
                }

                string rebuiltText = string.Join(CombinedBlockSeparator, translatedParts);
                Console.WriteLine($"Google Translate preserved separator across {translatedParts.Length} block(s)");
                return rebuiltText;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error preserving Google Translate separators: {ex.Message}");
                return text;
            }
        }

        private async Task<string?> TranslateSingleTextAsync(string text, string sourceLanguage, string targetLanguage)
        {
            string normalizedText = NormalizeText(text);
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                return text;
            }

            if (_useCloudApi)
            {
                return await TranslateWithCloudApiAsync(normalizedText, sourceLanguage, targetLanguage);
            }

            return await TranslateWithFreeServiceAsync(normalizedText, sourceLanguage, targetLanguage);
        }

        private async Task<List<string?>> TranslateBatchWithCloudApiAsync(IReadOnlyList<string> texts, string sourceLanguage, string targetLanguage)
        {
            try
            {
                if (texts.Count == 0)
                {
                    return new List<string?>();
                }

                if (string.IsNullOrEmpty(_apiKey))
                {
                    Console.WriteLine("Google Translate API key is not configured");
                    return texts.Select(_ => (string?)null).ToList();
                }

                string url = $"https://translation.googleapis.com/language/translate/v2?key={_apiKey}";
                var requestBody = new
                {
                    q = texts,
                    source = sourceLanguage,
                    target = targetLanguage,
                    format = "text"
                };

                string jsonRequest = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Google Translate batch API error: {response.StatusCode}");
                    return texts.Select(_ => (string?)null).ToList();
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(jsonResponse);

                if (doc.RootElement.TryGetProperty("data", out JsonElement data) &&
                    data.TryGetProperty("translations", out JsonElement translations) &&
                    translations.ValueKind == JsonValueKind.Array)
                {
                    var results = new List<string?>(translations.GetArrayLength());
                    foreach (JsonElement translation in translations.EnumerateArray())
                    {
                        string translated = translation.TryGetProperty("translatedText", out JsonElement translatedText)
                            ? HttpUtility.HtmlDecode(translatedText.GetString() ?? string.Empty)
                            : string.Empty;
                        results.Add(string.IsNullOrEmpty(translated) ? null : translated);
                    }

                    return results;
                }

                return texts.Select(_ => (string?)null).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error batching Google Cloud translations: {ex.Message}");
                return texts.Select(_ => (string?)null).ToList();
            }
        }

        private string NormalizeText(string text)
        {
            string normalizedText = text.Replace("\r\n", " ").Replace("\n", " ");

            while (normalizedText.Contains("  "))
            {
                normalizedText = normalizedText.Replace("  ", " ");
            }

            return normalizedText.Trim();
        }
        
        /// <summary>
        /// Translate a single text using Google Cloud Translation API (paid)
        /// </summary>
        private async Task<string?> TranslateWithCloudApiAsync(string text, string sourceLanguage, string targetLanguage)
        {
            try
            {
                List<string?> results = await TranslateBatchWithCloudApiAsync(new[] { text }, sourceLanguage, targetLanguage);
                if (results.Count == 0)
                {
                    return null;
                }
                return string.IsNullOrEmpty(results[0]) ? null : results[0];
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error translating text with Cloud API: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Translate a single text using Google Translate free web service.
        /// Tries multiple endpoint variants with retry + exponential backoff to
        /// work around Google's aggressive rate-limiting on the `gtx` client.
        /// Returns null when all attempts fail so the caller can surface the error.
        /// Hard-capped at <see cref="MaxPerBlockDurationMs"/> so the UI never
        /// hangs on a single block.
        /// </summary>
        private async Task<string?> TranslateWithFreeServiceAsync(string text, string sourceLanguage, string targetLanguage)
        {
            string normalizedText = NormalizeText(text);
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                return text;
            }

            // Log translation attempt (truncate long texts)
            string logText = normalizedText.Length > 50
                ? normalizedText.Substring(0, 50) + "..."
                : normalizedText;
            Console.WriteLine($"Translating with free service: {logText}");

            // Per-block budget. If we exceed this we abort immediately and
            // return null so the rest of the pipeline can keep moving.
            CancellationTokenSource? budgetCts = null;
            try
            {
                budgetCts = new CancellationTokenSource();
                budgetCts.CancelAfter(MaxPerBlockDurationMs);
                CancellationToken budgetToken = budgetCts.Token;

                string? lastError = null;
                Stopwatch budgetWatch = Stopwatch.StartNew();

                // Try each endpoint variant in order. The `gtx` client is the canonical
                // free Google Translate client but is aggressively rate-limited; the
                // other variants ride the same backend with different identifiers and
                // tend to have independent quotas.
                for (int endpointIndex = 0; endpointIndex < FreeEndpointTemplates.Length; endpointIndex++)
                {
                    for (int attempt = 1; attempt <= MaxFreeRetriesPerEndpoint; attempt++)
                    {
                        if (budgetToken.IsCancellationRequested)
                        {
                            lastError = $"budget exceeded ({budgetWatch.ElapsedMilliseconds}ms)";
                            break;
                        }

                        await ThrottleFreeRequestAsync(budgetToken);

                        string url = string.Format(FreeEndpointTemplates[endpointIndex],
                            sourceLanguage,
                            targetLanguage,
                            HttpUtility.UrlEncode(normalizedText));

                        try
                        {
                            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, budgetToken);

                            // 429 = rate limited, 5xx = transient server error -> retry with backoff.
                            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                            {
                                int retryDelayMs = ComputeRetryDelayMs(response, attempt);

                                // If Google tells us to wait a long time, skip to
                                // the next endpoint variant instead of blocking
                                // the UI on a retry-after countdown.
                                if (retryDelayMs > MaxPerBlockDurationMs / 2)
                                {
                                    lastError = $"HTTP {response.StatusCode} (long retry-after)";
                                    Console.WriteLine($"Google Translate endpoint #{endpointIndex + 1} returned {response.StatusCode} with long retry-after; skipping to next endpoint");
                                    break;
                                }

                                lastError = $"HTTP {response.StatusCode}";
                                Console.WriteLine($"Google Translate endpoint #{endpointIndex + 1} returned {response.StatusCode} (attempt {attempt}/{MaxFreeRetriesPerEndpoint}); retrying in {retryDelayMs}ms");
                                if (attempt < MaxFreeRetriesPerEndpoint)
                                {
                                    await Task.Delay(retryDelayMs, budgetToken);
                                    continue;
                                }
                                break; // exhausted retries for this endpoint
                            }

                            if (!response.IsSuccessStatusCode)
                            {
                                lastError = $"HTTP {response.StatusCode}";
                                Console.WriteLine($"Google Translate endpoint #{endpointIndex + 1} returned {response.StatusCode}; trying next endpoint");
                                break;
                            }

                            string jsonResponse = await response.Content.ReadAsStringAsync();
                            string? result = ParseFreeServiceJsonResponse(jsonResponse);
                            if (!string.IsNullOrEmpty(result))
                            {
                                string logResult = result.Length > 50
                                    ? result.Substring(0, 50) + "..."
                                    : result;
                                Console.WriteLine($"Translation result: {logResult} (took {budgetWatch.ElapsedMilliseconds}ms)");
                                return result;
                            }

                            lastError = "empty result";
                            Console.WriteLine($"Google Translate endpoint #{endpointIndex + 1} returned empty result (attempt {attempt}/{MaxFreeRetriesPerEndpoint})");
                        }
                        catch (TaskCanceledException ex) when (budgetToken.IsCancellationRequested)
                        {
                            lastError = "budget exceeded";
                            Console.WriteLine($"Google Translate per-block budget exceeded after {budgetWatch.ElapsedMilliseconds}ms");
                            break;
                        }
                        catch (TaskCanceledException ex)
                        {
                            lastError = "timeout";
                            Console.WriteLine($"Google Translate timeout (endpoint #{endpointIndex + 1}, attempt {attempt}): {ex.Message}");
                        }
                        catch (HttpRequestException ex)
                        {
                            lastError = ex.Message;
                            Console.WriteLine($"Google Translate network error (endpoint #{endpointIndex + 1}, attempt {attempt}): {ex.Message}");
                        }
                        catch (JsonException ex)
                        {
                            lastError = ex.Message;
                            Console.WriteLine($"Google Translate JSON parse error (endpoint #{endpointIndex + 1}, attempt {attempt}): {ex.Message}");
                        }
                    }

                    if (budgetToken.IsCancellationRequested)
                    {
                        break;
                    }
                }

                Console.WriteLine($"Google Translate free service failed for all endpoints: {lastError ?? "unknown"} ({budgetWatch.ElapsedMilliseconds}ms)");
                return null;
            }
            finally
            {
                budgetCts?.Dispose();
            }
        }

        /// <summary>
        /// Parses a translate_a/single JSON response and concatenates all
        /// translation segments. Returns null if the payload is malformed or empty.
        /// </summary>
        private static string? ParseFreeServiceJsonResponse(string jsonResponse)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                JsonElement outerArray = doc.RootElement;
                if (outerArray.GetArrayLength() == 0)
                {
                    return null;
                }

                StringBuilder translatedText = new StringBuilder();
                JsonElement translationArray = outerArray[0];
                foreach (JsonElement segment in translationArray.EnumerateArray())
                {
                    if (segment.GetArrayLength() > 0 && segment[0].ValueKind == JsonValueKind.String)
                    {
                        translatedText.Append(segment[0].GetString() ?? "");
                    }
                }

                string result = translatedText.ToString();
                return string.IsNullOrEmpty(result) ? null : result;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Computes how long to wait before the next retry. Honors the server's
        /// Retry-After header when present, otherwise uses exponential backoff
        /// with jitter. The returned delay is capped to avoid blocking the UI.
        /// </summary>
        private static int ComputeRetryDelayMs(HttpResponseMessage response, int attempt)
        {
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue)
                {
                    int delta = (int)response.Headers.RetryAfter.Delta.Value.TotalMilliseconds;
                    if (delta > 0)
                    {
                        return Math.Min(Math.Max(delta, 200), MaxPerBlockDurationMs);
                    }
                }
                if (response.Headers.RetryAfter.Date.HasValue)
                {
                    int delta = (int)(response.Headers.RetryAfter.Date.Value - DateTime.UtcNow).TotalMilliseconds;
                    if (delta > 0)
                    {
                        return Math.Min(Math.Max(delta, 200), MaxPerBlockDurationMs);
                    }
                }
            }

            // Exponential backoff with jitter: ~250ms, 500ms + 0-150ms.
            int baseDelay = (int)(Math.Pow(2, attempt - 1) * 250);
            int jitter = Random.Shared.Next(0, 150);
            return baseDelay + jitter;
        }

        /// <summary>
        /// Enforces a minimum interval between free-endpoint requests to avoid
        /// bursting Google's rate limiter. Honors the per-block budget so we
        /// don't sleep into the timeout.
        /// </summary>
        private static async Task ThrottleFreeRequestAsync(CancellationToken cancellationToken)
        {
            int delayMs = 0;
            lock (_throttleLock)
            {
                DateTime now = DateTime.UtcNow;
                double elapsedMs = (now - _lastFreeRequestUtc).TotalMilliseconds;
                if (elapsedMs < MinFreeRequestIntervalMs)
                {
                    delayMs = (int)(MinFreeRequestIntervalMs - elapsedMs);
                }
                _lastFreeRequestUtc = now.AddMilliseconds(delayMs);
            }

            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }
        }
        
        /// <summary>
        /// Map RST language codes to Google Translate language codes
        /// </summary>
        private string MapLanguageToGoogleCode(string language)
        {
            return language.ToLower() switch
            {
                "japanese" or "japan" or "ja" => "ja",
                "english" or "en" => "en",
                "chinese" or "zh" or "ch_sim" => "zh-CN",
                "korean" or "ko" => "ko",
                "vietnamese" or "vi" => "vi",
                "french" or "fr" => "fr",
                "german" or "de" => "de",
                "spanish" or "es" => "es",
                "italian" or "it" => "it",
                "portuguese" or "pt" => "pt",
                "russian" or "ru" => "ru",
                "hindi" or "hi" => "hi",
                "bulgarian" or "bg" or "bg-bg" => "bg",
                "indonesian" or "id" => "id",
                "polish" or "pl" => "pl",
                "arabic" or "ar" => "ar",
                "dutch" or "nl" => "nl",
                "romanian" or "ro" => "ro",
                "persian" or "farsi" or "fa" => "fa",
                "czech" or "cs" => "cs",
                "thai" or "th" or "thailand" => "th",
                "traditional chinese" or "ch_tra" => "zh-TW",
                "croatian" or "hr" => "hr",
                "hungarian" or "hu" => "hu",
                "turkish" or "tr" => "tr",
                "sinhala" or "si" => "si",
                "danish" or "da" => "da",
                "ukrainian" or "uk" => "uk",
                "finnish" or "fi" => "fi",
                "central kurdish" or "ckb" => "ckb",
                "bengali" or "bn" => "bn",
                "greek" or "el" => "el",
                _ => language
            };
        }
    }
}