using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly bool _useCloudApi;
        private readonly bool _autoMapLanguages;
        public static Regex? GoogleTranslateResultRegex { get; set; }

        public GoogleTranslateService()
        {
            _httpClient = new HttpClient();
            _apiKey = ConfigManager.Instance.GetGoogleTranslateApiKey();
            _useCloudApi = ConfigManager.Instance.GetGoogleTranslateUseCloudApi();
            _autoMapLanguages = ConfigManager.Instance.GetGoogleTranslateAutoMapLanguages();
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
                        
                        // Perform translation
                        string translatedText;
                        if (_useCloudApi)
                        {
                            translatedText = await TranslateWithCloudApiAsync(originalText, sourceLanguage, targetLanguage);
                        }
                        else
                        {
                            translatedText = await TranslateWithFreeServiceAsync(originalText, sourceLanguage, targetLanguage);
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
        /// Translate a single text using Google Cloud Translation API (paid)
        /// </summary>
        private async Task<string> TranslateWithCloudApiAsync(string text, string sourceLanguage, string targetLanguage)
        {
            try
            {
                // Check if API key is available
                if (string.IsNullOrEmpty(_apiKey))
                {
                    Console.WriteLine("Google Translate API key is not configured");
                    return $"[API KEY MISSING] {text}";
                }
                
                // Prepare the API URL
                string url = $"https://translation.googleapis.com/language/translate/v2?key={_apiKey}";
                
                // Prepare the request body
                var requestBody = new
                {
                    q = text,
                    source = sourceLanguage,
                    target = targetLanguage,
                    format = "text"
                };
                
                string jsonRequest = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                
                // Send the request
                var response = await _httpClient.PostAsync(url, content);
                
                // Check if the request was successful
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                    
                    // Extract the translated text from the response
                    if (doc.RootElement.TryGetProperty("data", out JsonElement data) &&
                        data.TryGetProperty("translations", out JsonElement translations) &&
                        translations.GetArrayLength() > 0 &&
                        translations[0].TryGetProperty("translatedText", out JsonElement translatedText))
                    {
                        return HttpUtility.HtmlDecode(translatedText.GetString() ?? text);
                    }
                }
                
                Console.WriteLine($"Google Translate API error: {response.StatusCode}");
                return $"[ERROR] {text}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error translating text with Cloud API: {ex.Message}");
                return $"[ERROR] {text}";
            }
        }
        
        /// <summary>
        /// Translate a single text using Google Translate free web service
        /// </summary>
        private async Task<string> TranslateWithFreeServiceAsync(string text, string sourceLanguage, string targetLanguage)
        {
            try
            {
                // Normalize the text by replacing line breaks with spaces and removing extra spaces
                string normalizedText = text.Replace("\r\n", " ").Replace("\n", " ");
                
                // Ensure there are no consecutive spaces
                while (normalizedText.Contains("  "))
                {
                    normalizedText = normalizedText.Replace("  ", " ");
                }
                
                // Prepare the URL for the free translation service
                string encodedText = HttpUtility.UrlEncode(normalizedText);
                
                // Add User-Agent to make request look more like a browser
                if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                {
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                }
                
                // Log translation attempt (truncate long texts)
                string logText = normalizedText.Length > 50 
                    ? normalizedText.Substring(0, 50) + "..." 
                    : normalizedText;
                Console.WriteLine($"Translating with free service: {logText}");
                
                // First try with the translate.googleapis.com endpoint (now as primary)
                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sourceLanguage}&tl={targetLanguage}&dt=t&q={encodedText}";
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    
                    try
                    {
                        // The response is a nested JSON array, not a proper JSON object
                        // Format: [[[translated_text, original_text, ...], ...], ...]
                        using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                        
                        // Build the full translated text from all segments
                        StringBuilder translatedText = new StringBuilder();
                        
                        // Navigate through the nested arrays
                        JsonElement outerArray = doc.RootElement;
                        if (outerArray.GetArrayLength() > 0)
                        {
                            JsonElement translationArray = outerArray[0];
                            
                            // Iterate through each translation segment
                            foreach (JsonElement segment in translationArray.EnumerateArray())
                            {
                                if (segment.GetArrayLength() > 0 && segment[0].ValueKind == JsonValueKind.String)
                                {
                                    string segmentText = segment[0].GetString() ?? "";
                                    translatedText.Append(segmentText);
                                }
                            }
                        }
                        
                        string result = translatedText.ToString();
                        
                        // Log the result for debugging (truncate long results)
                        string logResult = result.Length > 50 
                            ? result.Substring(0, 50) + "..." 
                            : result;
                        Console.WriteLine($"Translation result: {logResult}");
                        
                        if (!string.IsNullOrEmpty(result))
                        {
                            return result;
                        }
                        else
                        {
                            Console.WriteLine("Translated text was empty after processing");
                            // Fall through to alternative endpoint
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        Console.WriteLine($"JSON parsing error: {jsonEx.Message}");
                        Console.WriteLine($"Response content: {jsonResponse}");
                        // Fall through to alternative endpoint
                    }
                }
                
                // Try alternative endpoint if the first one fails
                Console.WriteLine("Trying alternative endpoint...");
                url = $"https://translate.google.com/m?hl={targetLanguage}&sl={sourceLanguage}&tl={targetLanguage}&ie=UTF-8&prev=_m&q={encodedText}";
                response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    string htmlResponse = await response.Content.ReadAsStringAsync();
                    GoogleTranslateResultRegex = new Regex("(?<=(<div(.*)class=\"result-container\"(.*)>))[\\s\\S]*?(?=(<\\/div>))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                    var matchResult = GoogleTranslateResultRegex.Match(htmlResponse);
                    
                    if (matchResult.Success)
                    {
                        string result = matchResult.Value.ToString();
                        if (!string.IsNullOrEmpty(result))
                        {
                            return result;
                        }
                        else
                        {
                            Console.WriteLine("Translated text was empty after processing");
                            return $"[EMPTY RESULT] {text}";
                        }
                    }
                }
                
                Console.WriteLine($"Google Translate free service error: {response.StatusCode}");
                return $"[ERROR] {text}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error translating text with free service: {ex.Message}");
                return $"[ERROR] {text}";
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
                "turkish" or "tr" => "tr",
                _ => language
            };
        }
    }
}