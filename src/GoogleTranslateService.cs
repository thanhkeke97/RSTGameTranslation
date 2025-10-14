using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;

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
        public static Regex GoogleTranslateResultRegex { get; set; }
        private const string TEXT_SEPARATOR = "|||RST_SEPARATOR|||";

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
                    List<(string id, string text)> blocks = new List<(string id, string text)>();
                    StringBuilder combinedText = new StringBuilder();
                    
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
                        
                        blocks.Add((blockId, originalText));
                        
                        if (combinedText.Length > 0)
                        {
                            combinedText.Append(TEXT_SEPARATOR);
                        }
                        combinedText.Append(originalText);
                    }
                    
                    if (blocks.Count == 0)
                    {
                        outputJson.WriteEndArray();
                        outputJson.WriteEndObject();
                        outputJson.Flush();
                        return Encoding.UTF8.GetString(memoryStream.ToArray());
                    }
                    
                    string combinedTranslatedText;
                    if (_useCloudApi)
                    {
                        combinedTranslatedText = await TranslateWithCloudApiAsync(combinedText.ToString(), sourceLanguage, targetLanguage);
                    }
                    else
                    {
                        combinedTranslatedText = await TranslateWithFreeServiceAsync(combinedText.ToString(), sourceLanguage, targetLanguage);
                    }
                    
                    string[] translatedParts = combinedTranslatedText.Split(TEXT_SEPARATOR);
                    
                    if (translatedParts.Length != blocks.Count)
                    {
                        Console.WriteLine($"Warning: Number of translated parts ({translatedParts.Length}) does not match number of original blocks ({blocks.Count})");
                        
                        for (int i = 0; i < blocks.Count; i++)
                        {
                            var (blockId, originalText) = blocks[i];
                            string translatedText;
                            
                            if (_useCloudApi)
                            {
                                translatedText = await TranslateWithCloudApiAsync(originalText, sourceLanguage, targetLanguage);
                            }
                            else
                            {
                                translatedText = await TranslateWithFreeServiceAsync(originalText, sourceLanguage, targetLanguage);
                            }
                            
                            outputJson.WriteStartObject();
                            outputJson.WriteString("id", blockId);
                            outputJson.WriteString("original_text", originalText);
                            outputJson.WriteString("translated_text", translatedText);
                            outputJson.WriteEndObject();
                            
                            Console.WriteLine($"Google translated (individual): '{originalText}' -> '{translatedText}'");
                        }
                    }
                    else
                    {
                        for (int i = 0; i < blocks.Count; i++)
                        {
                            var (blockId, originalText) = blocks[i];
                            string translatedText = translatedParts[i].Trim();
                            
                            
                            outputJson.WriteStartObject();
                            outputJson.WriteString("id", blockId);
                            outputJson.WriteString("original_text", originalText);
                            outputJson.WriteString("translated_text", translatedText);
                            outputJson.WriteEndObject();
                            
                            
                            Console.WriteLine($"Google translated (batch): '{originalText}' -> '{translatedText}'");
                        }
                    }
                }
                
                outputJson.WriteEndArray();
                outputJson.WriteEndObject();
                
                outputJson.Flush();
                string result = Encoding.UTF8.GetString(memoryStream.ToArray());
                
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

                // First attempt with the 'googleapis' endpoint
                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sourceLanguage}&tl={targetLanguage}&dt=t&q={encodedText}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                        StringBuilder translatedText = new StringBuilder();
                        JsonElement outerArray = doc.RootElement;

                        if (outerArray.GetArrayLength() > 0)
                        {
                            JsonElement translationArray = outerArray[0];
                            foreach (JsonElement segment in translationArray.EnumerateArray())
                            {
                                if (segment.GetArrayLength() > 0 && segment[0].ValueKind == JsonValueKind.String)
                                {
                                    translatedText.Append(segment[0].GetString() ?? "");
                                }
                            }
                        }
                        
                        string result = translatedText.ToString();
                        if (!string.IsNullOrEmpty(result))
                        {
                            result = result.Replace(" " + TEXT_SEPARATOR + " ", TEXT_SEPARATOR)
                                          .Replace(" " + TEXT_SEPARATOR, TEXT_SEPARATOR)
                                          .Replace(TEXT_SEPARATOR + " ", TEXT_SEPARATOR);
                            return result;
                        }
                        else
                        {
                            Console.WriteLine("Translated text was empty after processing (googleapis)");
                            // Don't return here, fall through to the alternative
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        Console.WriteLine($"JSON parsing error (googleapis): {jsonEx.Message}");
                        // Fall through to the alternative method
                    }
                }

                // If the first attempt fails or results in empty text, try the alternative endpoint
                Console.WriteLine("First endpoint failed or returned empty. Trying alternative endpoint...");
                url = $"https://translate.google.com/m?hl={targetLanguage}&sl={sourceLanguage}&tl={targetLanguage}&ie=UTF-8&prev=_m&q={encodedText}";
                response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    string htmlResponse = await response.Content.ReadAsStringAsync();
                    var regex = new Regex("(?<=(<div(.*)class=\"result-container\"(.*)>))[\\s\\S]*?(?=(<\\/div>))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                    var matchResult = regex.Match(htmlResponse);
                    
                    if (matchResult.Success)
                    {
                        string result = matchResult.Value.ToString();
                        if (!string.IsNullOrEmpty(result))
                        {
                            result = result.Replace(" " + TEXT_SEPARATOR + " ", TEXT_SEPARATOR)
                                          .Replace(" " + TEXT_SEPARATOR, TEXT_SEPARATOR)
                                          .Replace(TEXT_SEPARATOR + " ", TEXT_SEPARATOR);
                            return result;
                        }
                    }
                }

                Console.WriteLine($"Google Translate free service error (both endpoints failed): {response.StatusCode}");
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
                "Romanian" or "ro" => "ro",
                "Polish" or "pl" => "pl",
                "Persian" or "Farsi" or "fa" => "fa",
                "Czech" or "cs" => "cs",
                "Indonesian" or "id" => "id",
                "Thai" or "th" or "Thailand" => "th",
                "Traditional Chinese" or "ch_tra" => "zh-TW",
                _ => language
            };
        }
    }
}