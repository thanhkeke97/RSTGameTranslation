using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace RSTGameTranslation
{
    public class LMStudioTranslationService : ITranslationService
    {
        /// <summary>
        /// Translate text using the LM Studio API
        /// </summary>
        /// <param name="jsonData">The JSON data to translate</param>
        /// <param name="prompt">The prompt to guide the translation</param>
        /// <returns>The translation result formatted to match Gemini's response structure</returns>
        public async Task<string?> TranslateAsync(string jsonData, string prompt)
        {
            try
            {
                // Get the LM Studio API endpoint and model from config
                string lmStudioEndpoint = ConfigManager.Instance.GetLMStudioApiEndpoint();
                string lmStudioModel = ConfigManager.Instance.GetLMStudioModel();
                
                // Create LM Studio API request using the chat completion format
                var requestContent = new
                {
                    model = lmStudioModel, // Use model from config
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
                
                Console.WriteLine($"Sending request to LM Studio API at: {lmStudioEndpoint}");
                
                // Create a new HttpClient with a longer timeout
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(2); // 2 minute timeout
                    
                    HttpResponseMessage response = await client.PostAsync(lmStudioEndpoint, content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        
                        // Parse the LM Studio response which has a different format from Gemini
                        try
                        {
                            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                            
                            // LM Studio uses the exact format shown in the example
                            string responseText = "";
                            
                            // Extract content from choices[0].message.content as shown in the example output
                            if (doc.RootElement.TryGetProperty("choices", out JsonElement choicesElement) && 
                                choicesElement.ValueKind == JsonValueKind.Array && 
                                choicesElement.GetArrayLength() > 0)
                            {
                                var firstChoice = choicesElement[0];
                                if (firstChoice.TryGetProperty("message", out JsonElement messageElement) &&
                                    messageElement.TryGetProperty("content", out JsonElement contentElement))
                                {
                                    responseText = contentElement.GetString() ?? "";
                                }
                            }
                            
                            if (string.IsNullOrEmpty(responseText))
                            {
                                Console.WriteLine("Could not find response text in LM Studio API response");
                                Console.WriteLine($"Raw response: {jsonResponse}");
                                return null;
                            }
                            
                            LogManager.Instance.LogLlmReply(responseText);

                            // Check if the response contains nested JSON
                            try
                            {
                                // Trim possible markdown code blocks or extra text
                                responseText = responseText.Trim();
                                if (responseText.StartsWith("```json"))
                                {
                                    int endIndex = responseText.IndexOf("```", 7);
                                    if (endIndex > 0)
                                    {
                                        responseText = responseText.Substring(7, endIndex - 7).Trim();
                                    }
                                }
                                
                                // If the response doesn't start with '{', look for JSON within the text
                                if (!responseText.StartsWith("{"))
                                {
                                    int jsonStart = responseText.IndexOf('{');
                                    int jsonEnd = responseText.LastIndexOf('}');
                                    
                                    if (jsonStart >= 0 && jsonEnd > jsonStart)
                                    {
                                        responseText = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                                    }
                                }
                                
                                Console.WriteLine($"Attempting to parse nested JSON: {responseText}");
                                
                                using JsonDocument nestedDoc = JsonDocument.Parse(responseText);
                                
                                // Try to find text_blocks array which contains translations
                                if (nestedDoc.RootElement.TryGetProperty("text_blocks", out JsonElement textBlocks) && 
                                    textBlocks.ValueKind == JsonValueKind.Array)
                                {
                                    // Don't extract just the text - we want to keep the entire JSON structure
                                    // This ensures Logic.cs gets the complete JSON with text_blocks
                                    Console.WriteLine("Found structured JSON with text_blocks - keeping entire JSON structure");
                                    
                                    // Ensure the responseText is the entire JSON object
                                    // responseText is already set to the entire JSON
                                }
                                else
                                {
                                    // If we don't have the structured JSON we expect, try to extract text
                                    // Extract text from the first text block or combine all blocks
                                    StringBuilder combinedText = new StringBuilder();
                                    
                                    // Nested blocks may vary, so try to handle different structures
                                    
                                    // Try to find any text property at the root level
                                    if (nestedDoc.RootElement.TryGetProperty("text", out JsonElement rootTextProp))
                                    {
                                        combinedText.AppendLine(rootTextProp.GetString());
                                    }
                                    
                                    if (combinedText.Length > 0)
                                    {
                                        responseText = combinedText.ToString().Trim();
                                    }
                                    else
                                    {
                                        // Just use the entire cleaned JSON object as is - Logic.cs can handle it
                                        // Keep the responseText as the cleaned JSON
                                        Console.WriteLine("Using the entire JSON object as responseText");
                                    }
                                }
                            }
                            catch (JsonException ex)
                            {
                                // If nested parsing fails, use the original response text
                                Console.WriteLine($"Could not parse nested JSON, using raw response: {ex.Message}");
                            }
                            
                            // Process the response to match what Logic.cs expects from Gemini
                            var formattedResponse = new
                            {
                                candidates = new[]
                                {
                                    new
                                    {
                                        content = new
                                        {
                                            parts = new[]
                                            {
                                                new
                                                {
                                                    text = responseText
                                                }
                                            }
                                        }
                                    }
                                }
                            };
                            
                            return JsonSerializer.Serialize(formattedResponse, jsonOptions);
                        }
                        catch (JsonException jex)
                        {
                            Console.WriteLine($"Error parsing LM Studio response: {jex.Message}");
                            Console.WriteLine($"Raw response: {jsonResponse}");
                            return null;
                        }
                    }
                    else
                    {
                        string errorMessage = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"LM Studio API error: {response.StatusCode}, {errorMessage}");
                        
                        // Try to parse the error message from JSON if possible
                        try
                        {
                            using JsonDocument errorDoc = JsonDocument.Parse(errorMessage);
                            if (errorDoc.RootElement.TryGetProperty("error", out JsonElement errorElement))
                            {
                                string detailedError = errorElement.GetString() ?? errorMessage;
                                
                                // Show error message to user
                                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                                    System.Windows.MessageBox.Show(
                                        $"LM Studio error: {detailedError}\n\nPlease check your model name and LM Studio settings.",
                                        "LM Studio Translation Error",
                                        System.Windows.MessageBoxButton.OK,
                                        System.Windows.MessageBoxImage.Error);
                                });
                                
                                return null;
                            }
                        }
                        catch (JsonException)
                        {
                            // If we can't parse as JSON, just use the raw message
                        }
                        
                        // Show general error if JSON parsing failed
                        System.Windows.Application.Current.Dispatcher.Invoke(() => {
                            System.Windows.MessageBox.Show(
                                $"LM Studio API error: {response.StatusCode}\n{errorMessage}\n\nPlease check your settings.",
                                "LM Studio Translation Error",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Error);
                        });
                        
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LM Studio API error: {ex.Message}");
                
                // Show error message to user for other exceptions
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    System.Windows.MessageBox.Show(
                        $"LM Studio API error: {ex.Message}\n\nPlease check your network connection and LM Studio settings.",
                        "LM Studio Translation Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                });
                
                return null;
            }
        }
    }
}