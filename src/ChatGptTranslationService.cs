using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WPFScreenCapture
{
    public class ChatGptTranslationService : ITranslationService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _configFilePath;
        
        public ChatGptTranslationService()
        {
            // Set the base address for OpenAI API
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WPFScreenCapture");
            
            // Get the configuration file path
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _configFilePath = System.IO.Path.Combine(appDirectory, "chatgpt_config.txt");
            
            // Ensure the config file exists
            if (!System.IO.File.Exists(_configFilePath))
            {
                CreateDefaultConfigFile();
            }
        }
        
        private void CreateDefaultConfigFile()
        {
            try
            {
                string defaultPrompt = "You are a translator. Translate the text I'll provide into English. Keep it simple and conversational.";
                string content = $"<llm_prompt_multi_start>\n{defaultPrompt}\n<llm_prompt_multi_end>";
                System.IO.File.WriteAllText(_configFilePath, content);
                Console.WriteLine("Created default ChatGPT config file");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating default ChatGPT config file: {ex.Message}");
            }
        }

        public async Task<string?> TranslateAsync(string jsonData, string prompt)
        {
            try
            {
                // Get API key and model from config
                string apiKey = ConfigManager.Instance.GetChatGptApiKey();
                string model = ConfigManager.Instance.GetChatGptModel();
                
                // Validate we have an API key
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    Console.WriteLine("ChatGPT API key is missing. Please set it in the settings.");
                    return null;
                }
                
                // Log the original input
                //Console.WriteLine($"ChatGPT input JSON: {jsonData}");
                
                // Parse the input JSON
                JsonElement inputJson = JsonSerializer.Deserialize<JsonElement>(jsonData);
                
                // Get custom prompt from config
                string customPrompt = ConfigManager.Instance.GetServicePrompt("ChatGPT");
                
                // Build messages array for ChatGPT API
                var messages = new List<Dictionary<string, string>>();
                
                // Use the exact prompt format as specified
                StringBuilder systemPrompt = new StringBuilder();
     
                // Add any custom instructions from the config file
                if (!string.IsNullOrWhiteSpace(customPrompt) && !customPrompt.Contains("translator"))
                {
                     systemPrompt.AppendLine(customPrompt);
                }
                
                messages.Add(new Dictionary<string, string> 
                {
                    { "role", "system" },
                    { "content", systemPrompt.ToString() }
                });
                
                // Add the text to translate as the user message
                messages.Add(new Dictionary<string, string>
                {
                    { "role", "user" },
                    { "content", "Here is the input JSON:\n\n" + jsonData }
                });
                
                // Create request body
                var requestBody = new Dictionary<string, object>
                {
                    { "model", model },
                    { "messages", messages },
                    { "temperature", 0.3 },  // Lower temperature for more consistent translations
                    { "max_tokens", 2000 }   // Increase max tokens for longer texts
                };
                
                // Serialize the request body
                string requestJson = JsonSerializer.Serialize(requestBody);
                
                // Set up HTTP request
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                
                // Send request to OpenAI API
                var response = await _httpClient.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();
                
                // Check if request was successful
                if (response.IsSuccessStatusCode)
                {
                    // Log raw response before any processing
                    LogManager.Instance.LogLlmReply(responseContent);
                    
                    // Log to console for debugging (limited to first 500 chars)
                    if (responseContent.Length > 500)
                    {
                        Console.WriteLine($"ChatGPT API response: {responseContent.Substring(0, 500)}...");
                    }
                    else
                    {
                        Console.WriteLine($"ChatGPT API response: {responseContent}");
                    }
                    
                    try
                    {
                        // Parse response
                        var responseObj = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseContent);
                        if (responseObj != null && responseObj.TryGetValue("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
                        {
                            var firstChoice = choices[0];
                            string translatedText = firstChoice.GetProperty("message").GetProperty("content").GetString() ?? "";
                            
                            // Log the extracted translation
                            if (translatedText.Length > 100)
                            {
                                Console.WriteLine($"ChatGPT translation extracted: {translatedText.Substring(0, 100)}...");
                            }
                            else
                            {
                                Console.WriteLine($"ChatGPT translation extracted: {translatedText}");
                            }
                            
                            // Clean up the response - sometimes there might be markdown code block markers
                            translatedText = translatedText.Trim();
                            if (translatedText.StartsWith("```json"))
                            {
                                translatedText = translatedText.Substring(7);
                            }
                            else if (translatedText.StartsWith("```"))
                            {
                                translatedText = translatedText.Substring(3);
                            }
                            
                            if (translatedText.EndsWith("```"))
                            {
                                translatedText = translatedText.Substring(0, translatedText.Length - 3);
                            }
                            translatedText = translatedText.Trim();
                            
                            // Clean up escape sequences and newlines in the JSON
                            if (translatedText.StartsWith("{") && translatedText.EndsWith("}"))
                            {
                                // Properly escape newlines within JSON strings - don't convert to literal newlines
                                // as it will break JSON parsing
                                
                                // Make sure it doesn't have additional newlines that could cause issues
                                
                                if (translatedText.Contains("\r\n"))
                                {
                                    // We'll replace literal Windows newlines with spaces to avoid formatting issues
                                    translatedText = translatedText.Replace("\r\n", " ");
                                }
                               
                                
                                // Replace nicely formatted JSON (with newlines) with compact JSON for better parsing
                                try
                                {
                                    var tempJson = JsonSerializer.Deserialize<object>(translatedText);
                                    var options = new JsonSerializerOptions 
                                    { 
                                        WriteIndented = false // This ensures compact JSON without any newlines
                                    };
                                    translatedText = JsonSerializer.Serialize(tempJson, options);
                                    Console.WriteLine("Successfully normalized JSON format");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Failed to normalize JSON format: {ex.Message}");
                                }
                            }
                            
                            // Check if the response is in JSON format
                            if (translatedText.StartsWith("{") && translatedText.EndsWith("}"))
                            {
                                try
                                {
                                    // Validate it's proper JSON by parsing it
                                    var translatedJson = JsonSerializer.Deserialize<JsonElement>(translatedText);
                                    
                                    // Log that we got valid JSON
                                    Console.WriteLine("ChatGPT returned valid JSON");
                                    
                                    // Check if this is a game JSON translation with text_blocks
                                    if (translatedJson.TryGetProperty("text_blocks", out _))
                                    {
                                        // For game JSON format, we need to match the format that the other translation services use
                                        Console.WriteLine("This is a game JSON format - wrapping in the standard format");
                                        
                                        // Save the translated JSON to a debug file for inspection
                                        try 
                                        {
                                            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                                            string debugFilePath = System.IO.Path.Combine(appDirectory, "chatgpt_translation_debug.txt");
                                            System.IO.File.WriteAllText(debugFilePath, translatedText);
                                            Console.WriteLine($"Debug translation saved to {debugFilePath}");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Failed to write debug file: {ex.Message}");
                                        }
                                        
                                        // Based on the format of other translators, we need the text to be wrapped
                                        var outputJson = new Dictionary<string, object>
                                        {
                                            { "translated_text", translatedText },
                                            { "original_text", jsonData },
                                            { "detected_language", inputJson.GetProperty("source_language").GetString() ?? "ja" }
                                        };
                                        
                                        string finalOutput = JsonSerializer.Serialize(outputJson);
                                        Console.WriteLine($"Final wrapped output: {finalOutput.Substring(0, Math.Min(100, finalOutput.Length))}...");
                                        
                                        return finalOutput;
                                    }
                                    else
                                    {
                                        // For other formats, we'll wrap the result in the standard format
                                        var compatibilityOutput = new Dictionary<string, object>
                                        {
                                            { "translated_text", translatedText },
                                            { "original_text", jsonData },
                                            { "detected_language", inputJson.GetProperty("source_language").GetString() ?? "ja" }
                                        };
                                        
                                        string finalOutput = JsonSerializer.Serialize(compatibilityOutput);
                                        
                                        // Log the final output format
                                        Console.WriteLine($"Final output format: {finalOutput.Substring(0, Math.Min(100, finalOutput.Length))}...");
                                        
                                        return finalOutput;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error parsing JSON response: {ex.Message}");
                                    // Not valid JSON, will handle as plain text below
                                }
                            }
                            
                            // If we got plain text or invalid JSON, wrap it in our format
                            var formattedOutput = new Dictionary<string, object>
                            {
                                { "translated_text", translatedText },
                                { "original_text", jsonData },
                                { "detected_language", inputJson.GetProperty("source_language").GetString() ?? "ja" }
                            };
                            
                            string output = JsonSerializer.Serialize(formattedOutput);
                            Console.WriteLine($"Formatted as plain text, output: {output.Substring(0, Math.Min(100, output.Length))}...");
                            
                            return output;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing ChatGPT response: {ex.Message}");
                        return null;
                    }
                }
                else
                {
                    Console.WriteLine($"Error calling ChatGPT API: {response.StatusCode}");
                    Console.WriteLine($"Response: {responseContent}");
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ChatGptTranslationService.TranslateAsync: {ex.Message}");
                return null;
            }
        }
    }
}