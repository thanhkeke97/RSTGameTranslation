using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.IO;

namespace UGTLive
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
                // Phân tích JSON đầu vào
                using JsonDocument doc = JsonDocument.Parse(jsonData);
                JsonElement root = doc.RootElement;

                // Tạo một tài liệu JSON mới cho văn bản đã dịch
                var options = new JsonSerializerOptions { WriteIndented = true };
                using var memoryStream = new MemoryStream();
                using var outputJson = new System.Text.Json.Utf8JsonWriter(memoryStream);
                
                outputJson.WriteStartObject();
                
                // Sao chép metadata từ đầu vào nếu có
                if (root.TryGetProperty("metadata", out JsonElement metadata))
                {
                    outputJson.WritePropertyName("metadata");
                    JsonSerializer.Serialize(outputJson, metadata, options);
                }
                
                // Lấy ngôn ngữ nguồn và đích
                string sourceLanguage = root.TryGetProperty("source_language", out JsonElement srcLang) 
                    ? srcLang.GetString() ?? "ja" 
                    : ConfigManager.Instance.GetSourceLanguage();
                    
                string targetLanguage = root.TryGetProperty("target_language", out JsonElement tgtLang) 
                    ? tgtLang.GetString() ?? "en" 
                    : ConfigManager.Instance.GetTargetLanguage();
                
                // Ánh xạ mã ngôn ngữ sang định dạng Google Translate nếu auto-mapping được bật
                if (_autoMapLanguages)
                {
                    sourceLanguage = MapLanguageToGoogleCode(sourceLanguage);
                    targetLanguage = MapLanguageToGoogleCode(targetLanguage);
                }
                
                // Bắt đầu dịch các khối văn bản
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
                        
                        // Bỏ qua văn bản trống
                        if (string.IsNullOrWhiteSpace(originalText))
                        {
                            continue;
                        }
                        
                        // Dịch văn bản bằng Google Translate
                        string translatedText;
                        if (_useCloudApi)
                        {
                            translatedText = await TranslateWithCloudApiAsync(originalText, sourceLanguage, targetLanguage);
                        }
                        else
                        {
                            translatedText = await TranslateWithFreeServiceAsync(originalText, sourceLanguage, targetLanguage);
                        }
                        
                        // Ghi khối đã dịch vào đầu ra
                        outputJson.WriteStartObject();
                        outputJson.WriteString("id", blockId);
                        outputJson.WriteString("original_text", originalText);
                        outputJson.WriteString("translated_text", translatedText);
                        outputJson.WriteEndObject();
                        
                        // Ghi log để gỡ lỗi
                        Console.WriteLine($"Google translated: '{originalText}' -> '{translatedText}'");
                    }
                }
                
                outputJson.WriteEndArray();
                outputJson.WriteEndObject();
                
                // Chuyển đổi thành chuỗi và trả về
                outputJson.Flush();
                string result = Encoding.UTF8.GetString(memoryStream.ToArray());
                
                // Ghi log kết quả cuối cùng
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
                // Store original text format information
                bool hasLineBreaks = text.Contains("\n");
                string[] originalLines = hasLineBreaks ? text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None) : null;
                
                // Normalize text for translation - replace line breaks with special marker
                // Using a marker that's unlikely to appear in normal text
                const string LINE_BREAK_MARKER = "§§LINEBREAK§§";
                string normalizedText = text;
                
                if (hasLineBreaks)
                {
                    // Replace line breaks with marker instead of removing them
                    normalizedText = normalizedText.Replace("\r\n", LINE_BREAK_MARKER).Replace("\n", LINE_BREAK_MARKER);
                }
                
                // Prepare the URL for the free translation service
                string encodedText = HttpUtility.UrlEncode(normalizedText);
                
                // Add User-Agent to make request look more like a browser
                if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                {
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                }
                
                // Use a more robust URL with additional parameters
                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sourceLanguage}&tl={targetLanguage}&dt=t&q={encodedText}";
                
                // Log translation attempt (truncate long texts)
                string logText = normalizedText.Length > 50 
                    ? normalizedText.Substring(0, 50) + "..." 
                    : normalizedText;
                Console.WriteLine($"Translating with free service: {logText}");
                
                // Send the request
                var response = await _httpClient.GetAsync(url);
                
                // Check if the request was successful
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
                            // If we used line break markers, restore them now
                            if (hasLineBreaks && result.Contains(LINE_BREAK_MARKER))
                            {
                                // Simply replace markers with actual line breaks
                                result = result.Replace(LINE_BREAK_MARKER, "\n");
                            }
                            // If the original had line breaks but they weren't preserved in translation
                            else if (hasLineBreaks && !result.Contains("\n") && originalLines.Length > 1)
                            {
                                // Try to restore line breaks based on original text structure
                                
                                // First, count characters per line in original text (excluding line breaks)
                                double[] originalLineCharRatios = new double[originalLines.Length];
                                int totalOriginalChars = text.Replace("\r", "").Replace("\n", "").Length;
                                
                                for (int i = 0; i < originalLines.Length; i++)
                                {
                                    originalLineCharRatios[i] = (double)originalLines[i].Length / totalOriginalChars;
                                }
                                
                                // Distribute translated text based on original character ratios
                                StringBuilder formattedResult = new StringBuilder();
                                int charPosition = 0;
                                
                                for (int i = 0; i < originalLines.Length - 1; i++) // Process all but last line
                                {
                                    int charsToTake = (int)Math.Round(result.Length * originalLineCharRatios[i]);
                                    
                                    // Ensure we take at least 1 character and don't exceed remaining length
                                    charsToTake = Math.Max(1, Math.Min(charsToTake, result.Length - charPosition));
                                    
                                    // Try to find a space near the calculated position to break naturally
                                    int breakPos = FindNaturalBreakPosition(result, charPosition + charsToTake);
                                    
                                    // Add the line with a line break
                                    formattedResult.AppendLine(result.Substring(charPosition, breakPos - charPosition));
                                    charPosition = breakPos;
                                }
                                
                                // Add the remaining text
                                if (charPosition < result.Length)
                                {
                                    formattedResult.Append(result.Substring(charPosition));
                                }
                                
                                result = formattedResult.ToString();
                            }
                            
                            return result;
                        }
                        else
                        {
                            Console.WriteLine("Translated text was empty after processing");
                            return $"[EMPTY RESULT] {text}";
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        Console.WriteLine($"JSON parsing error: {jsonEx.Message}");
                        Console.WriteLine($"Response content: {jsonResponse}");
                        return $"[JSON ERROR] {text}";
                    }
                }
                
                Console.WriteLine($"Google Translate free service error: {response.StatusCode}");
                
                // Try alternative endpoint if the first one fails
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || 
                    response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    Console.WriteLine("Trying alternative endpoint...");
                    url = $"https://translate.google.com/translate_a/single?client=at&dt=t&dt=ld&dt=qca&dt=rm&dt=bd&dj=1&sl={sourceLanguage}&tl={targetLanguage}&q={encodedText}";
                    
                    response = await _httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        try
                        {
                            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                            
                            if (doc.RootElement.TryGetProperty("sentences", out JsonElement sentences))
                            {
                                StringBuilder translatedText = new StringBuilder();
                                
                                foreach (JsonElement sentence in sentences.EnumerateArray())
                                {
                                    if (sentence.TryGetProperty("trans", out JsonElement trans))
                                    {
                                        translatedText.Append(trans.GetString());
                                    }
                                }
                                
                                string result = translatedText.ToString();
                                if (!string.IsNullOrEmpty(result))
                                {
                                    // Apply the same line break restoration logic as above
                                    if (hasLineBreaks && result.Contains(LINE_BREAK_MARKER))
                                    {
                                        result = result.Replace(LINE_BREAK_MARKER, "\n");
                                    }
                                    return result;
                                }
                            }
                        }
                        catch (JsonException)
                        {
                            // Fall through to error return
                        }
                    }
                }
                
                return $"[ERROR] {text}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error translating text with free service: {ex.Message}");
                return $"[ERROR] {text}";
            }
        }

        /// <summary>
        /// Find a good position to break text, preferably at a space or punctuation
        /// </summary>
        private int FindNaturalBreakPosition(string text, int targetPosition)
        {
            // If the target position is already at the end, return it
            if (targetPosition >= text.Length)
                return text.Length;
            
            // Look for a space within 10 characters of the target position
            int searchRange = 10;
            
            // Search backward first
            for (int i = 0; i < searchRange; i++)
            {
                int pos = targetPosition - i;
                if (pos <= 0)
                    break;
                    
                if (char.IsWhiteSpace(text[pos - 1]))
                    return pos;
            }
            
            // Then search forward
            for (int i = 1; i < searchRange; i++)
            {
                int pos = targetPosition + i;
                if (pos >= text.Length)
                    return text.Length;
                    
                if (char.IsWhiteSpace(text[pos - 1]))
                    return pos;
            }
            
            // If no good break point found, just use the target position
            return targetPosition;
        }
        
        /// <summary>
        /// Map UGTLive language codes to Google Translate language codes
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
                _ => language
            };
        }
    }
}