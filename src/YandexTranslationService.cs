using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.IO;

namespace RSTGameTranslation
{
    /// <summary>
    /// Translation service that uses Yandex browser endpoint.
    /// Optimized to batch segmented text into a single request.
    /// Returns the same output schema as GoogleTranslateService for compatibility.
    /// </summary>
    public class YandexTranslationService : ITranslationService
    {
        private readonly HttpClient _httpClient;

        public YandexTranslationService()
        {
            _httpClient = new HttpClient();

            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            }

            if (!_httpClient.DefaultRequestHeaders.Contains("Origin"))
            {
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://translate.yandex.com");
            }

            _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://translate.yandex.com/");
        }

        public async Task<string?> TranslateAsync(string jsonData, string prompt)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(jsonData);
                JsonElement root = doc.RootElement;

                var options = new JsonSerializerOptions { WriteIndented = true };
                using var memoryStream = new MemoryStream();
                using var outputJson = new Utf8JsonWriter(memoryStream);

                outputJson.WriteStartObject();

                if (root.TryGetProperty("metadata", out JsonElement metadata))
                {
                    outputJson.WritePropertyName("metadata");
                    JsonSerializer.Serialize(outputJson, metadata, options);
                }

                string sourceLanguage = root.TryGetProperty("source_language", out JsonElement srcLang)
                    ? srcLang.GetString() ?? "ja"
                    : ConfigManager.Instance.GetSourceLanguage();

                string targetLanguage = root.TryGetProperty("target_language", out JsonElement tgtLang)
                    ? tgtLang.GetString() ?? "en"
                    : ConfigManager.Instance.GetTargetLanguage();

                sourceLanguage = MapLanguageToYandexCode(sourceLanguage);
                targetLanguage = MapLanguageToYandexCode(targetLanguage);

                outputJson.WritePropertyName("translations");
                outputJson.WriteStartArray();

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

                        if (string.IsNullOrWhiteSpace(originalText))
                        {
                            continue;
                        }

                        string translatedText = await TranslateWithYandexAsync(originalText, sourceLanguage, targetLanguage);

                        outputJson.WriteStartObject();
                        outputJson.WriteString("id", blockId);
                        outputJson.WriteString("original_text", originalText);
                        outputJson.WriteString("translated_text", translatedText);
                        outputJson.WriteEndObject();

                        Console.WriteLine($"Yandex translated: '{originalText}' -> '{translatedText}'");
                    }
                }

                outputJson.WriteEndArray();
                outputJson.WriteEndObject();
                outputJson.Flush();

                string result = Encoding.UTF8.GetString(memoryStream.ToArray());
                Console.WriteLine($"Yandex final JSON result: {result.Substring(0, Math.Min(100, result.Length))}...");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in YandexTranslationService: {ex.Message}");
                return null;
            }
        }

        private async Task<string> TranslateWithYandexAsync(string text, string sourceLanguage, string targetLanguage)
        {
            try
            {
                const string separator = "##|||##";

                // Preserve RST block separator exactly by translating each segment,
                // but do it in ONE batched request for speed.
                if (text.Contains(separator, StringComparison.Ordinal))
                {
                    string[] originalParts = text.Split(new[] { separator }, StringSplitOptions.None);
                    var translatableParts = new List<string>();
                    var partMap = new List<int>(originalParts.Length);

                    foreach (string part in originalParts)
                    {
                        string normalizedPart = NormalizeText(part);
                        if (string.IsNullOrWhiteSpace(normalizedPart))
                        {
                            partMap.Add(-1);
                        }
                        else
                        {
                            partMap.Add(translatableParts.Count);
                            translatableParts.Add(normalizedPart);
                        }
                    }

                    if (translatableParts.Count == 0)
                    {
                        return text;
                    }

                    List<string> translatedBatch = await TranslateBatchWithYandexAsync(translatableParts, sourceLanguage, targetLanguage);
                    if (translatedBatch.Count == 0)
                    {
                        return text;
                    }

                    var rebuiltParts = new string[originalParts.Length];
                    for (int i = 0; i < originalParts.Length; i++)
                    {
                        int translatedIndex = partMap[i];
                        if (translatedIndex < 0 || translatedIndex >= translatedBatch.Count)
                        {
                            rebuiltParts[i] = originalParts[i];
                            continue;
                        }

                        string translatedPart = translatedBatch[translatedIndex];
                        rebuiltParts[i] = string.IsNullOrWhiteSpace(translatedPart) ? originalParts[i] : translatedPart;
                    }

                    return string.Join(separator, rebuiltParts);
                }

                string normalizedText = NormalizeText(text);
                if (string.IsNullOrWhiteSpace(normalizedText))
                {
                    return text;
                }

                List<string> translatedSingle = await TranslateBatchWithYandexAsync(new List<string> { normalizedText }, sourceLanguage, targetLanguage);
                if (translatedSingle.Count > 0 && !string.IsNullOrWhiteSpace(translatedSingle[0]))
                {
                    return translatedSingle[0];
                }

                return text;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error translating text with Yandex: {ex.Message}");
                return text;
            }
        }

        private async Task<List<string>> TranslateBatchWithYandexAsync(IReadOnlyList<string> texts, string sourceLanguage, string targetLanguage)
        {
            try
            {
                if (texts.Count == 0)
                {
                    return new List<string>();
                }

                string detectedSourceLanguage = sourceLanguage;
                if (string.IsNullOrWhiteSpace(detectedSourceLanguage) || detectedSourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    // Detect once for the whole batch to reduce latency.
                    string detectionSample = string.Join(" ", texts);
                    detectedSourceLanguage = await DetectLanguageAsync(detectionSample);
                    if (string.IsNullOrWhiteSpace(detectedSourceLanguage))
                    {
                        detectedSourceLanguage = "auto";
                    }
                }

                string langPair = $"{detectedSourceLanguage}-{targetLanguage}";
                var urlBuilder = new StringBuilder();
                urlBuilder.Append("https://browser.translate.yandex.net/api/v1/tr.json/translate");
                urlBuilder.Append("?lang=").Append(HttpUtility.UrlEncode(langPair));
                urlBuilder.Append("&srv=browser_video_translation");

                foreach (string text in texts)
                {
                    urlBuilder.Append("&text=").Append(HttpUtility.UrlEncode(text));
                }

                using var body = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("maxRetryCount", "2"),
                    new KeyValuePair<string, string>("fetchAbortTimeout", "500")
                });

                var response = await _httpClient.PostAsync(urlBuilder.ToString(), body);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Yandex translate endpoint error: {response.StatusCode}");
                    return new List<string>();
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                using JsonDocument responseDoc = JsonDocument.Parse(jsonResponse);
                var root = responseDoc.RootElement;

                if (!root.TryGetProperty("text", out JsonElement textArray) || textArray.ValueKind != JsonValueKind.Array)
                {
                    return new List<string>();
                }

                var results = new List<string>(textArray.GetArrayLength());
                foreach (JsonElement item in textArray.EnumerateArray())
                {
                    results.Add(item.GetString() ?? string.Empty);
                }

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Yandex batch translation: {ex.Message}");
                return new List<string>();
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

        private async Task<string> DetectLanguageAsync(string text)
        {
            try
            {
                string detectUrl = $"https://translate.yandex.net/api/v1/tr.json/detect?srv=browser_video_translation&text={HttpUtility.UrlEncode(text)}";
                var response = await _httpClient.GetAsync(detectUrl);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Yandex detect endpoint error: {response.StatusCode}");
                    return string.Empty;
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                if (doc.RootElement.TryGetProperty("lang", out JsonElement langElement))
                {
                    return langElement.GetString() ?? string.Empty;
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error detecting language with Yandex: {ex.Message}");
                return string.Empty;
            }
        }

        private string MapLanguageToYandexCode(string language)
        {
            return language.ToLower() switch
            {
                "japanese" or "japan" or "ja" => "ja",
                "english" or "en" => "en",
                "chinese" or "zh" or "ch_sim" => "zh",
                "korean" or "ko" => "ko",
                "vietnamese" or "vi" => "vi",
                "french" or "fr" => "fr",
                "german" or "de" => "de",
                "spanish" or "es" => "es",
                "italian" or "it" => "it",
                "portuguese" or "pt" => "pt",
                "russian" or "ru" => "ru",
                "hindi" or "hi" => "hi",
                "indonesian" or "id" => "id",
                "polish" or "pl" => "pl",
                "arabic" or "ar" => "ar",
                "dutch" or "nl" => "nl",
                "romanian" or "ro" => "ro",
                "persian" or "farsi" or "fa" => "fa",
                "czech" or "cs" => "cs",
                "thai" or "th" or "thailand" => "th",
                "traditional chinese" or "ch_tra" => "zh",
                "croatian" or "hr" => "hr",
                "turkish" or "tr" => "tr",
                "sinhala" or "si" => "si",
                "danish" or "da" => "da",
                "ukrainian" or "uk" => "uk",
                "finnish" or "fi" => "fi",
                _ => language
            };
        }
    }
}