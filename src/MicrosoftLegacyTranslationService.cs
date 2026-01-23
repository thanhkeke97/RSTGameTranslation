using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;
using System.Globalization;

namespace RSTGameTranslation
{
    /// <summary>
    /// Microsoft legacy translator using HMAC-based signature
    /// </summary>
    public class MicrosoftLegacyTranslationService : ITranslationService
    {
        private readonly HttpClient _httpClient;
        private readonly string _singleKey;

        // Default private key 
        private static readonly byte[] _defaultPrivateKey = new byte[] {
            0xA2,0x29,0x3A,0x3D,0xD0,0xDD,0x32,0x73,0x97,0x7A,0x64,0xDB,0xC2,0xF3,0x27,0xF5,
            0xD7,0xBF,0x87,0xD9,0x45,0x9D,0xF0,0x5A,0x09,0x66,0xC6,0x30,0xC6,0x6A,0xAA,0x84,
            0x9A,0x41,0xAA,0x94,0x3A,0xA8,0xD5,0x1A,0x6E,0x4D,0xAA,0xC9,0xA3,0x70,0x12,0x35,
            0xC7,0xEB,0x12,0xF6,0xE8,0x23,0x07,0x9E,0x47,0x10,0x95,0x91,0x88,0x55,0xD8,0x17
        };

        public MicrosoftLegacyTranslationService()
        {
            _httpClient = new HttpClient();
            _singleKey = ConfigManager.Instance.GetMicrosoftApiKey();
        }

        // Concurrency and retry/backoff tuning for rate limit handling
        private static readonly int MaxConcurrentRequests = 2; // adjust if needed
        private static readonly SemaphoreSlim _concurrencySemaphore = new SemaphoreSlim(MaxConcurrentRequests);
        private static readonly Random _jitterRng = new Random();
        private const int DefaultMaxRetries = 5; // total attempts
        private const int BaseDelayMs = 500; // base for exponential backoff (ms)
        private const int MaxDelayMs = 60000; // cap backoff at 60s

        /// <summary>
        /// Compute signature 
        /// </summary>
        internal static string ComputeSignature(string urlWithoutScheme, byte[] privateKey, DateTime utcNow, string? guid = null)
        {
            if (guid == null)
            {
                guid = Guid.NewGuid().ToString("N"); // no dashes
            }

            // Escape the url (encode reserved chars)
            string escapedUrl = Uri.EscapeDataString(urlWithoutScheme);

            // RFC1123-like format used in python code
            string dateTime = utcNow.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture);

            string canonical = ("MSTranslatorAndroidApp" + escapedUrl + dateTime + guid).ToLowerInvariant();
            byte[] payload = Encoding.UTF8.GetBytes(canonical);

            using (var hmac = new HMACSHA256(privateKey))
            {
                byte[] hash = hmac.ComputeHash(payload);
                string signatureBase64 = Convert.ToBase64String(hash);
                string signature = $"MSTranslatorAndroidApp::{signatureBase64}::{dateTime}::{guid}";
                return signature;
            }
        }

        // Map generic language names/codes to Microsoft Translator codes
        internal static string MapToMicrosoftCode(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
                return language ?? "";

            string l = language.Trim().ToLowerInvariant();
            return l switch
            {
                "japanese" or "japan" or "ja" => "ja",
                "english" or "en" => "en",
                "chinese" or "zh" or "ch_sim" or "simplified chinese" => "zh-CN",
                "traditional chinese" or "ch_tra" or "zh-tw" or "tw" => "zh-TW",
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
                "arabic" or "ar" => "ar",
                "thai" or "th" => "th",
                "dutch" or "nl" => "nl",
                "polish" or "pl" => "pl",
                "romanian" or "ro" => "ro",
                "persian" or "fa" or "farsi" => "fa",
                "czech" or "cs" => "cs",
                "sinhala" or "si" => "si",
                "ukrainian" or "uk" => "uk",
                "auto" or "auto-detect" or "auto_detect" => "auto",
                _ => language
            };
        }

        /// <summary>
        /// Translate: accepts the same JSON schema as other services and returns a result JSON or null on failure
        /// </summary>
        public async Task<string?> TranslateAsync(string jsonData, string prompt)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(jsonData);
                JsonElement root = doc.RootElement;

                string sourceLanguage = root.TryGetProperty("source_language", out JsonElement s) ? (s.GetString() ?? ConfigManager.Instance.GetSourceLanguage()) : ConfigManager.Instance.GetSourceLanguage();
                string targetLanguage = root.TryGetProperty("target_language", out JsonElement t) ? (t.GetString() ?? ConfigManager.Instance.GetTargetLanguage()) : ConfigManager.Instance.GetTargetLanguage();

                // Map languages to Microsoft expected codes (e.g., 'japanese' -> 'ja', 'chinese' -> 'zh-CN')
                string mappedSource = MapToMicrosoftCode(sourceLanguage);
                string mappedTarget = MapToMicrosoftCode(targetLanguage);

                string host = "api.cognitive.microsofttranslator.com";
                string apiVersion = "3.0";
                string path = $"translate?api-version={apiVersion}&to={mappedTarget}";
                if (!string.IsNullOrEmpty(mappedSource) && !string.Equals(mappedSource, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    path += $"&from={mappedSource}";
                }

                // urlWithoutScheme used for signature must be host + path (no scheme)
                string urlForSignature = $"{host}/{path}";
                byte[] privateKey = _defaultPrivateKey;

                // allow override via config key
                string overrideKey = ConfigManager.Instance.GetMicrosoftLegacySignatureKey();
                if (!string.IsNullOrEmpty(overrideKey))
                {
                    try
                    {
                        // interpret as hex string or base64 - try hex first
                        if (overrideKey.StartsWith("0x") || overrideKey.Contains(" ") == false && overrideKey.Length % 2 == 0)
                        {
                            // Try parse hex
                            int len = overrideKey.Length;
                            byte[] parsed = new byte[len / 2];
                            for (int i = 0; i < len; i += 2)
                                parsed[i / 2] = Convert.ToByte(overrideKey.Substring(i, 2), 16);
                            privateKey = parsed;
                        }
                        else
                        {
                            // fallback to base64
                            privateKey = Convert.FromBase64String(overrideKey);
                        }
                    }
                    catch
                    {
                        Console.WriteLine("Failed to parse override Microsoft legacy signature key; using embedded default.");
                    }
                }

                // Prepare response JSON writer
                var options = new JsonSerializerOptions { WriteIndented = true };
                using var memoryStream = new MemoryStream();
                using var outputJson = new Utf8JsonWriter(memoryStream);

                outputJson.WriteStartObject();
                outputJson.WritePropertyName("translations");
                outputJson.WriteStartArray();

                if (root.TryGetProperty("text_blocks", out JsonElement textBlocks))
                {
                    foreach (JsonElement block in textBlocks.EnumerateArray())
                    {
                        string originalText = block.TryGetProperty("text", out JsonElement textEl) ? (textEl.GetString() ?? "") : "";
                        string blockId = block.TryGetProperty("id", out JsonElement idEl) ? (idEl.GetString() ?? "") : "";

                        if (string.IsNullOrWhiteSpace(originalText))
                        {
                            continue;
                        }

                        // Prepare signature inputs
                        string guid = Guid.NewGuid().ToString("N");
                        DateTime utcNow = DateTime.UtcNow;
                        string signature = ComputeSignature(urlForSignature, privateKey, utcNow, guid);

                        // Reconstruct canonical string for debugging (no private key included)
                        string escapedForDebug = Uri.EscapeDataString(urlForSignature);
                        string dateForDebug = utcNow.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture);
                        string canonicalForDebug = ("MSTranslatorAndroidApp" + escapedForDebug + dateForDebug + guid).ToLowerInvariant();

                        var request = new HttpRequestMessage(HttpMethod.Post, $"https://{urlForSignature}");
                        request.Headers.Add("X-MT-Signature", signature);
                        var bodyArray = new object[] { new { Text = originalText } };
                        string jsonRequest = JsonSerializer.Serialize(bodyArray);
                        request.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                        HttpResponseMessage? response = null;

                        try
                        {
                            // Use retry/backoff + concurrency control to send the request
                            response = await SendWithRetriesAsync(urlForSignature, privateKey, jsonRequest, DefaultMaxRetries);
                            if (response == null)
                            {
                                Console.WriteLine("Microsoft translator failed after retries");
                                return null;
                            }
                        }
                        catch (Exception exSend)
                        {
                            // Log send exception
                            string debug = $"Exception sending request after retries: {exSend}\nURL: https://{urlForSignature}\nRequestBody: {jsonRequest}";
                            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "microsoft_last_request.txt"), debug); } catch { }
                            Console.WriteLine($"Microsoft translator request failed: {exSend.Message}");
                            return null;
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            string err = await response.Content.ReadAsStringAsync();
                            string debug = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\nURL: https://{urlForSignature}\nRequestBody: {jsonRequest}\nResponse: {err}";

                            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "microsoft_last_error.txt"), err); } catch { }
                            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "microsoft_last_request.txt"), debug); } catch { }

                            Console.WriteLine($"Microsoft translator HTTP error: {response.StatusCode}");

                            // On failure, return null (non-throwing behavior like other services)
                            return null;
                        }

                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        try
                        {
                            using JsonDocument respDoc = JsonDocument.Parse(jsonResponse);
                            string translated = "";
                            if (respDoc.RootElement.GetArrayLength() > 0 &&
                                respDoc.RootElement[0].TryGetProperty("translations", out JsonElement transArr) &&
                                transArr.GetArrayLength() > 0 &&
                                transArr[0].TryGetProperty("text", out JsonElement ttext))
                            {
                                translated = ttext.GetString() ?? "";
                            }

                            // Write output element
                            outputJson.WriteStartObject();
                            outputJson.WriteString("id", blockId);
                            outputJson.WriteString("original_text", originalText);
                            outputJson.WriteString("translated_text", translated);
                            outputJson.WriteEndObject();

                            Console.WriteLine($"Microsoft translated: '{originalText}' -> '{translated}'");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error parsing Microsoft response: {ex.Message}");
                            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "microsoft_last_error.txt"), jsonResponse);
                            return null;
                        }
                    }
                }

                outputJson.WriteEndArray();
                outputJson.WriteEndObject();
                outputJson.Flush();
                string result = Encoding.UTF8.GetString(memoryStream.ToArray());
                Console.WriteLine($"Microsoft Translate final JSON result: { (result.Length>100? result.Substring(0,100) + "..." : result)}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in MicrosoftLegacyTranslationService: {ex.Message}");
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "microsoft_last_error.txt"), ex.ToString());
                }
                catch { }
                return null;
            }
        }

        // Send request with retries, exponential backoff, jitter and respecting Retry-After header
        private async Task<HttpResponseMessage?> SendWithRetriesAsync(string urlForSignature, byte[] privateKey, string jsonRequest, int maxRetries)
        {
            await _concurrencySemaphore.WaitAsync();
            try
            {
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    string guid = Guid.NewGuid().ToString("N");
                    DateTime utcNow = DateTime.UtcNow;
                    string signature = ComputeSignature(urlForSignature, privateKey, utcNow, guid);

                    var request = new HttpRequestMessage(HttpMethod.Post, $"https://{urlForSignature}");
                    request.Headers.Remove("X-MT-Signature");
                    request.Headers.Add("X-MT-Signature", signature);
                    request.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = null!;
                    try
                    {
                        response = await _httpClient.SendAsync(request);
                    }
                    catch (Exception ex)
                    {
                        // Network error - consider retrying
                        Console.WriteLine($"Send attempt {attempt} failed: {ex.Message}");
                        if (attempt == maxRetries)
                        {
                            throw; // bubble up to caller to handle logging
                        }
                        else
                        {
                            int delayMs = CalculateBackoffMs(attempt);
                            await Task.Delay(delayMs);
                            continue;
                        }
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        return response;
                    }

                    int status = (int)response.StatusCode;
                    if (status == 429)
                    {
                        // Respect Retry-After if provided
                        int delayMs = CalculateBackoffMs(attempt);
                        if (response.Headers.TryGetValues("Retry-After", out var values))
                        {
                            var first = values?.FirstOrDefault();
                            if (int.TryParse(first, out int seconds))
                            {
                                delayMs = Math.Max(delayMs, seconds * 1000);
                            }
                            else if (DateTime.TryParse(first, out DateTime retryDate))
                            {
                                int ms = (int)Math.Max(0, (retryDate - DateTime.UtcNow).TotalMilliseconds);
                                delayMs = Math.Max(delayMs, Math.Min(ms, MaxDelayMs));
                            }
                        }

                        // Log and wait
                        Console.WriteLine($"Received 429, attempt {attempt}, waiting {delayMs}ms before retry");
                        if (attempt == maxRetries)
                        {
                            // final attempt failed
                            return response;
                        }
                        await Task.Delay(delayMs);
                        continue;
                    }
                    else if (status >= 500 && status < 600)
                    {
                        // Server errors - retry with backoff
                        if (attempt == maxRetries)
                        {
                            return response;
                        }
                        int delayMs = CalculateBackoffMs(attempt);
                        Console.WriteLine($"Server error {status}, attempt {attempt}, waiting {delayMs}ms before retry");
                        await Task.Delay(delayMs);
                        continue;
                    }
                    else
                    {
                        // Client error (4xx other than 429) - do not retry
                        return response;
                    }
                }

                return null;
            }
            finally
            {
                _concurrencySemaphore.Release();
            }
        }

        private int CalculateBackoffMs(int attempt)
        {
            // exponential backoff with jitter: base * 2^(attempt-1) capped, jitter factor 0.5-1.5
            double exponential = BaseDelayMs * Math.Pow(2, attempt - 1);
            double jitterFactor = 0.5 + _jitterRng.NextDouble();
            int ms = (int)Math.Min(MaxDelayMs, exponential * jitterFactor);
            return ms;
        }
    }
}
