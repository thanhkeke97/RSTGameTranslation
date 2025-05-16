using System.IO;
using System.Text;
using System.Text.Json;

namespace RSTGameTranslation
{
    public class LogManager
    {
        private static LogManager? _instance;
        private readonly string _logDirectory;
        
        // Log file paths
        private readonly string _ocrResponsePath;
        private readonly string _llmRequestPath;
        private readonly string _llmReplyPath;
        
        // Singleton pattern
        public static LogManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LogManager();
                }
                return _instance;
            }
        }
        
        // Constructor
        private LogManager()
        {
            // Set log directory to be in the application's directory
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _logDirectory = appDirectory;
            
            // Set log file paths
            _ocrResponsePath = Path.Combine(_logDirectory, "last_ocr_response.json");
            _llmRequestPath = Path.Combine(_logDirectory, "last_llm_request_sent.txt");
            _llmReplyPath = Path.Combine(_logDirectory, "last_llm_reply_received.txt");
            
            Console.WriteLine($"Log files will be saved in: {_logDirectory}");
        }
        
        // Log OCR response
        public void LogOcrResponse(string jsonData)
        {
            try
            {
                // Attempt to format the JSON for better readability
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(jsonData);
                    var options = new JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                    };
                    jsonData = JsonSerializer.Serialize(doc.RootElement, options);
                }
                catch
                {
                    // If formatting fails, use the original JSON
                }
                
                // Write to file
                File.WriteAllText(_ocrResponsePath, jsonData);
                //Console.WriteLine($"OCR response logged to {_ocrResponsePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error logging OCR response: {ex.Message}");
            }
        }
        
        // Log LLM request
        public void LogLlmRequest(string prompt, string jsonData)
        {
            try
            {
                // Combine prompt and JSON data
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== LLM PROMPT ===");
                sb.AppendLine(prompt);
                sb.AppendLine();
                sb.AppendLine("=== INPUT JSON ===");
                
                // Attempt to format the JSON for better readability
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(jsonData);
                    var options = new JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                    };
                    sb.AppendLine(JsonSerializer.Serialize(doc.RootElement, options));
                }
                catch
                {
                    // If formatting fails, use the original JSON
                    sb.AppendLine(jsonData);
                }
                
                // Write to file
                File.WriteAllText(_llmRequestPath, sb.ToString());
                Console.WriteLine($"LLM request logged to {_llmRequestPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error logging LLM request: {ex.Message}");
            }
        }
        
        // Log LLM reply
        public void LogLlmReply(string jsonResponse)
        {
            try
            {
                // Attempt to format the JSON for better readability
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                    var options = new JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                    };
                    jsonResponse = JsonSerializer.Serialize(doc.RootElement, options);
                }
                catch
                {
                    // If formatting fails, use the original JSON
                }
                
                // Write to file
                File.WriteAllText(_llmReplyPath, jsonResponse);
                Console.WriteLine($"LLM reply logged to {_llmReplyPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error logging LLM reply: {ex.Message}");
            }
        }
    }
}