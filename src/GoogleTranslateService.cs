using System;
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

        // Allow time for every part while still bounding an entire failed OCR batch.
        private const int MaxFreePartDurationMs = 6000;
        private const int MaxFreeTranslationDurationMs = 30000;
        private const int FreeRequestTimeoutMs = 8000;
        private static readonly GoogleTranslateFreeClient SharedFreeClient = new GoogleTranslateFreeClient(_httpClient);
        private readonly GoogleTranslateFreeClient _freeClient = SharedFreeClient;

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

        internal GoogleTranslateService(GoogleTranslateFreeClient freeClient) : this()
        {
            _freeClient = freeClient;
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
                using var freeBudget = new CancellationTokenSource();
                // Analyze the input JSON data
                using JsonDocument doc = JsonDocument.Parse(jsonData);
                JsonElement root = doc.RootElement;
                if (!_useCloudApi)
                    freeBudget.CancelAfter(GetFreeTranslationBudgetMs(root));

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

                        var translation = await TranslatePreservingSeparatorsAsync(
                            originalText, sourceLanguage, targetLanguage, freeBudget.Token);
                        string translatedText = translation.Text;
                        // A valid translation can equal the source (names, numbers, same language).
                        if (translation.Success)
                            successCount++;

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

        private async Task<(string Text, bool Success)> TranslatePreservingSeparatorsAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
        {
            try
            {
                if (!text.Contains(CombinedBlockSeparator, StringComparison.Ordinal))
                {
                    string? single = await TranslateSingleTextAsync(text, sourceLanguage, targetLanguage, cancellationToken);
                    return string.IsNullOrEmpty(single) ? (text, false) : (single, true);
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
                    return (text, false);
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
                        if (cancellationToken.IsCancellationRequested)
                            break;
                        translatedBatch.Add(await TranslateFreePartAsync(part, sourceLanguage, targetLanguage, cancellationToken));
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
                return (rebuiltText, translatedBatch.Any(value => !string.IsNullOrWhiteSpace(value)));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error preserving Google Translate separators: {ex.Message}");
                return (text, false);
            }
        }

        private async Task<string?> TranslateSingleTextAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
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

            return await TranslateFreePartAsync(normalizedText, sourceLanguage, targetLanguage, cancellationToken);
        }

        internal static int GetFreeTranslationBudgetMs(JsonElement root)
        {
            int parts = 0;
            if (root.TryGetProperty("text_blocks", out JsonElement blocks) && blocks.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement block in blocks.EnumerateArray())
                {
                    if (!block.TryGetProperty("text", out JsonElement text) || text.ValueKind != JsonValueKind.String)
                        continue;
                    foreach (string part in (text.GetString() ?? "").Split(CombinedBlockSeparator))
                    {
                        if (!string.IsNullOrWhiteSpace(part) && ++parts >= MaxFreeTranslationDurationMs / MaxFreePartDurationMs)
                            return MaxFreeTranslationDurationMs;
                    }
                }
            }
            return Math.Max(1, parts) * MaxFreePartDurationMs;
        }

        private async Task<string?> TranslateFreePartAsync(string text, string source, string target, CancellationToken batchToken)
        {
            using var partBudget = CancellationTokenSource.CreateLinkedTokenSource(batchToken);
            partBudget.CancelAfter(MaxFreePartDurationMs);
            return await _freeClient.TranslateAsync(text, source, target, partBudget.Token);
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
