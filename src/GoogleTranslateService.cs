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

        /// <summary>
        /// Open the HTTPS connections before the first translation (see TranslationWarmUp).
        /// The free client tries translate.googleapis.com first and falls back to the web
        /// endpoint, whose seed comes from translate.google.com.hk — warm both.
        /// </summary>
        internal static Task WarmUpConnectionAsync() =>
            ConfigManager.Instance.GetGoogleTranslateUseCloudApi()
                ? TranslationWarmUp.OpenConnectionsAsync(_httpClient, "Google Translate", "https://translation.googleapis.com/")
                : TranslationWarmUp.OpenConnectionsAsync(_httpClient, "Google Translate",
                    "https://translate.googleapis.com/", "https://translate.google.com.hk/");

        // Allow time for every part while still bounding an entire failed OCR batch.
        private const int MaxFreePartDurationMs = 6000;
        private const int MaxFreeTranslationDurationMs = 30000;
        private const int FreeRequestTimeoutMs = 8000;
        private static readonly GoogleTranslateFreeClient SharedFreeClient = new GoogleTranslateFreeClient(_httpClient);
        private readonly GoogleTranslateFreeClient _freeClient = SharedFreeClient;

        private readonly string _apiKey;
        private readonly bool _useCloudApi;
        public static Regex? GoogleTranslateResultRegex { get; set; }

        public GoogleTranslateService()
        {
            _apiKey = ConfigManager.Instance.GetGoogleTranslateApiKey();
            _useCloudApi = ConfigManager.Instance.GetGoogleTranslateUseCloudApi();
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

        // Free Google normally answers in well under a second. When it hasn't after this long (an
        // endpoint hanging until its timeout, requests queued behind the rate-limit spacing), the
        // fallback service is asked as well and the first good answer wins.
        private const int HedgeDelayMs = 1500;

        /// <summary>
        /// Translate text using Google Translate API
        /// </summary>
        public async Task<string?> TranslateAsync(string jsonData, string prompt)
        {
            bool canHedge = !_useCloudApi && IsFallbackServiceAvailable() && !_freeClient.IsCoolingDown;
            if (!canHedge)
                return await TranslateWithGoogleAsync(jsonData, prompt, allowFallback: true, CancellationToken.None);

            using var cancelGoogle = new CancellationTokenSource();
            Task<string?> googleTask = TranslateWithGoogleAsync(jsonData, prompt, allowFallback: false, cancelGoogle.Token);

            if (await Task.WhenAny(googleTask, Task.Delay(HedgeDelayMs)) == googleTask)
            {
                string? fast = await googleTask;
                // Google failed (rate limited, every endpoint down, ...): the fallback still gets the line on screen
                return fast ?? await TranslateWithFallbackAsync(jsonData, prompt, "Google Translate failed");
            }

            Task<string?> fallbackTask = TranslateWithFallbackAsync(jsonData, prompt, $"Google Translate has not answered after {HedgeDelayMs} ms");
            Task<string?> first = await Task.WhenAny(googleTask, fallbackTask);
            string? result = await first;
            if (result != null)
            {
                if (first == fallbackTask)
                {
                    // Stop the slow Google request so it doesn't hold a slot in the request spacing
                    cancelGoogle.Cancel();
                    Console.WriteLine("Google Translate: fallback answered first");
                }
                return result;
            }

            // The first to finish failed: wait for the other one
            return await (first == googleTask ? fallbackTask : googleTask);
        }

        private static bool IsFallbackServiceAvailable()
        {
            string fallback = ConfigManager.Instance.GetGoogleFreeFallbackService();
            return fallback == "Microsoft" || fallback == "Yandex";
        }

        // Translate with Google only. With allowFallback, a rate limit hands the request to the fallback
        // service (the original behaviour); without it, null is returned so the caller decides.
        private async Task<string?> TranslateWithGoogleAsync(string jsonData, string prompt, bool allowFallback, CancellationToken cancellationToken)
        {
            try
            {
                using var freeBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                // Analyze the input JSON data
                using JsonDocument doc = JsonDocument.Parse(jsonData);
                JsonElement root = doc.RootElement;
                if (!_useCloudApi)
                    freeBudget.CancelAfter(GetFreeTranslationBudgetMs(root));

                // Rate limited right now: every request would be refused, so go straight to the
                // fallback service instead of returning untranslated text.
                if (!_useCloudApi && _freeClient.IsCoolingDown)
                {
                    if (!allowFallback)
                        return null;

                    string? viaFallback = await TranslateWithFallbackAsync(jsonData, prompt, "Google Translate is rate limited (HTTP 429)");
                    if (viaFallback != null) return viaFallback;

                    // Google would refuse every part too: fail now instead of returning the source text
                    // and trying the (just failed) fallback a second time
                    Console.WriteLine("Google Translate is rate limited and the fallback failed; skipping this translation");
                    return null;
                }

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
                
                // The request carries language names (for AI services); map them to Google codes
                sourceLanguage = LanguageCatalog.ToGoogleCode(sourceLanguage);
                targetLanguage = LanguageCatalog.ToGoogleCode(targetLanguage);
                
                // Starting translation
                outputJson.WritePropertyName("translations");
                outputJson.WriteStartArray();
                
                int attemptedCount = 0;
                int successCount = 0;

                if (root.TryGetProperty("text_blocks", out JsonElement textBlocks))
                {
                    var blocks = new List<(string Id, string Text)>();
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

                        blocks.Add((blockId, originalText));
                    }

                    attemptedCount = blocks.Count;

                    // All blocks share the batched requests, instead of at least one request per block
                    var translations = await TranslatePreservingSeparatorsAsync(
                        blocks.Select(b => b.Text).ToList(), sourceLanguage, targetLanguage, freeBudget.Token);

                    for (int i = 0; i < blocks.Count; i++)
                    {
                        string translatedText = translations[i].Text;
                        // A valid translation can equal the source (names, numbers, same language).
                        if (translations[i].Success)
                            successCount++;

                        // Write the translated text to the output JSON
                        outputJson.WriteStartObject();
                        outputJson.WriteString("id", blocks[i].Id);
                        outputJson.WriteString("original_text", blocks[i].Text);
                        outputJson.WriteString("translated_text", translatedText);
                        outputJson.WriteEndObject();

                        // Log for debugging
                        Console.WriteLine($"Google translated: '{blocks[i].Text}' -> '{translatedText}'");
                    }
                }

                outputJson.WriteEndArray();
                outputJson.WriteEndObject();

                // Transfer the JSON to a string and return it
                outputJson.Flush();
                string result = Encoding.UTF8.GetString(memoryStream.ToArray());

                // Cancelled because the fallback already answered: the result is not needed
                if (cancellationToken.IsCancellationRequested)
                    return null;

                // Hit a 429 during this request: some blocks came back untranslated, so redo the
                // whole request with the fallback service.
                if (!_useCloudApi && _freeClient.IsCoolingDown)
                {
                    if (!allowFallback)
                        return null;

                    string? viaFallback = await TranslateWithFallbackAsync(jsonData, prompt, "Google Translate is rate limited (HTTP 429)");
                    if (viaFallback != null) return viaFallback;
                }

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

        /// <summary>
        /// Translate several blocks together. Each block may itself hold parts joined with the combined
        /// separator; all parts of all blocks go through one batched translation (few free requests, or
        /// one Cloud API call), then each block is rebuilt with its separators in place.
        /// </summary>
        private async Task<List<(string Text, bool Success)>> TranslatePreservingSeparatorsAsync(IReadOnlyList<string> texts, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
        {
            try
            {
                var originalParts = texts.Select(t => t.Split(new[] { CombinedBlockSeparator }, StringSplitOptions.None)).ToList();
                var translatableParts = new List<string>();
                var partMaps = new List<int[]>(texts.Count);

                foreach (string[] parts in originalParts)
                {
                    var partMap = new int[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                    {
                        string normalizedPart = NormalizeText(parts[i]);
                        if (string.IsNullOrWhiteSpace(normalizedPart))
                        {
                            partMap[i] = -1;
                            continue;
                        }

                        partMap[i] = translatableParts.Count;
                        translatableParts.Add(normalizedPart);
                    }
                    partMaps.Add(partMap);
                }

                if (translatableParts.Count == 0)
                {
                    return texts.Select(t => (t, false)).ToList();
                }

                List<string?> translatedBatch;
                if (_useCloudApi)
                {
                    translatedBatch = await TranslateBatchWithCloudApiAsync(translatableParts, sourceLanguage, targetLanguage);
                }
                else
                {
                    translatedBatch = await TranslateFreePartsBatchedAsync(translatableParts, sourceLanguage, targetLanguage, cancellationToken);
                }

                var results = new List<(string Text, bool Success)>(texts.Count);
                for (int t = 0; t < texts.Count; t++)
                {
                    string[] parts = originalParts[t];
                    int[] partMap = partMaps[t];
                    var translatedParts = new string[parts.Length];
                    bool anyTranslated = false;

                    for (int i = 0; i < parts.Length; i++)
                    {
                        int translatedIndex = partMap[i];
                        string? translatedPart = translatedIndex >= 0 && translatedIndex < translatedBatch.Count
                            ? translatedBatch[translatedIndex]
                            : null;

                        if (string.IsNullOrWhiteSpace(translatedPart))
                        {
                            translatedParts[i] = parts[i];
                        }
                        else
                        {
                            translatedParts[i] = translatedPart;
                            anyTranslated = true;
                        }
                    }

                    results.Add((string.Join(CombinedBlockSeparator, translatedParts), anyTranslated));
                }

                Console.WriteLine($"Google Translate: {texts.Count} block(s), {translatableParts.Count} part(s) translated together");
                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error preserving Google Translate separators: {ex.Message}");
                return texts.Select(t => (t, false)).ToList();
            }
        }

        // Upper bound on characters per batched free request (the text goes in the URL).
        private const int MaxFreeBatchChars = 1000;

        /// <summary>
        /// Translate OCR blocks with as few free requests as possible: parts are joined with
        /// newlines into one request per ~1000 characters and split back by line. Previously each
        /// block was its own request, which got the IP rate limited (HTTP 429) quickly.
        /// Parts never contain newlines (NormalizeText). If Google returns a different number of
        /// lines, that group falls back to one request per part.
        /// </summary>
        private async Task<List<string?>> TranslateFreePartsBatchedAsync(List<string> parts, string source, string target, CancellationToken cancellationToken)
        {
            var results = new List<string?>(new string?[parts.Count]);
            int start = 0;
            while (start < parts.Count && !cancellationToken.IsCancellationRequested)
            {
                int end = start, chars = 0;
                while (end < parts.Count && (end == start || chars + parts[end].Length + 1 <= MaxFreeBatchChars))
                {
                    chars += parts[end].Length + 1;
                    end++;
                }

                bool groupDone = false;
                if (end - start > 1)
                {
                    var group = parts.GetRange(start, end - start);
                    string? joined = await TranslateFreePartAsync(string.Join("\n", group), source, target, cancellationToken);
                    if (joined != null)
                    {
                        var lines = joined.Split('\n').Select(line => line.Trim()).ToList();
                        while (lines.Count > group.Count && lines[^1].Length == 0)
                            lines.RemoveAt(lines.Count - 1);
                        if (lines.Count == group.Count && lines.All(line => line.Length > 0))
                        {
                            for (int i = 0; i < lines.Count; i++)
                                results[start + i] = lines[i];
                            groupDone = true;
                        }
                        else
                        {
                            Console.WriteLine($"Google Translate: batch returned {lines.Count} lines for {group.Count} blocks, translating them one by one");
                        }
                    }
                    else if (_freeClient.IsCoolingDown)
                    {
                        // Rate limited: per-part requests would be refused too; caller falls back.
                        groupDone = true;
                    }
                }

                if (!groupDone)
                {
                    for (int i = start; i < end && !cancellationToken.IsCancellationRequested && !_freeClient.IsCoolingDown; i++)
                        results[i] = await TranslateFreePartAsync(parts[i], source, target, cancellationToken);
                }
                start = end;
            }
            return results;
        }

        /// <summary>
        /// Translate the whole request with the configured fallback service while free Google
        /// Translate is rate limited, so lines are not lost or shown untranslated. Only services
        /// returning the same {"translations": [...]} format are allowed (Logic parses the
        /// response as Google's). Returns null when disabled or when the fallback fails too.
        /// </summary>
        private static async Task<string?> TranslateWithFallbackAsync(string jsonData, string prompt, string reason)
        {
            string fallback = ConfigManager.Instance.GetGoogleFreeFallbackService();
            if (fallback != "Microsoft" && fallback != "Yandex")
                return null;
            try
            {
                Console.WriteLine($"{reason}, translating with {fallback} meanwhile");
                ITranslationService service = TranslationServiceFactory.CreateService(fallback);
                return await service.TranslateAsync(jsonData, prompt);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Google Translate fallback ({fallback}) failed: {ex.Message}");
                return null;
            }
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
                var requestBody = new Dictionary<string, object>
                {
                    ["q"] = texts,
                    ["target"] = targetLanguage,
                    ["format"] = "text"
                };
                // Without a source the Cloud API detects the language
                if (!LanguageCatalog.IsAuto(sourceLanguage))
                {
                    requestBody["source"] = sourceLanguage;
                }

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
        
    }
}
