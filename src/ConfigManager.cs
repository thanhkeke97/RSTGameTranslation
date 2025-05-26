using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace RSTGameTranslation
{
    public class ConfigManager
    {
        private static ConfigManager? _instance;
        private readonly string _configFilePath;
        private readonly string _geminiConfigFilePath;
        private readonly string _ollamaConfigFilePath;
        private readonly string _chatgptConfigFilePath;
        private readonly string _googleTranslateConfigFilePath;
        private readonly Dictionary<string, string> _configValues;
        private string _currentTranslationService = "Gemini"; // Default to Gemini

        // Config keys
        public const string GEMINI_API_KEY = "gemini_api_key";
        public const string GEMINI_MODEL = "gemini_model";
        public const string TRANSLATION_SERVICE = "translation_service";
        public const string OCR_METHOD = "ocr_method";
        public const string OLLAMA_URL = "ollama_url";
        public const string OLLAMA_PORT = "ollama_port";
        public const string OLLAMA_MODEL = "ollama_model";
        public const string CHATGPT_API_KEY = "chatgpt_api_key";
        public const string CHATGPT_MODEL = "chatgpt_model";
        public const string FORCE_CURSOR_VISIBLE = "force_cursor_visible";
        public const string AUTO_SIZE_TEXT_BLOCKS = "auto_size_text_blocks";
        public const string GOOGLE_TRANSLATE_API_KEY = "google_translate_api_key";
        // Google Translate settings
        public const string GOOGLE_TRANSLATE_USE_CLOUD_API = "google_translate_use_cloud_api";
        public const string GOOGLE_TRANSLATE_AUTO_MAP_LANGUAGES = "google_translate_auto_map_languages";
        
        // Translation context keys
        public const string MAX_CONTEXT_PIECES = "max_context_pieces";
        public const string MIN_CONTEXT_SIZE = "min_context_size";
        public const string GAME_INFO = "game_info";
        
        // OCR configuration keys
        public const string MIN_TEXT_FRAGMENT_SIZE = "min_text_fragment_size";
        public const string BLOCK_DETECTION_SCALE = "block_detection_scale";
        public const string BLOCK_DETECTION_SETTLE_TIME = "block_detection_settle_time";
        public const string KEEP_TRANSLATED_TEXT_UNTIL_REPLACED = "keep_translated_text_until_replaced";
        public const string LEAVE_TRANSLATION_ONSCREEN = "leave_translation_onscreen";
        public const string MIN_LETTER_CONFIDENCE = "min_letter_confidence";
        public const string MIN_LINE_CONFIDENCE = "min_line_confidence";
        public const string AUTO_TRANSLATE_ENABLED = "auto_translate_enabled";
        public const string IGNORE_PHRASES = "ignore_phrases";
        
        // Text-to-Speech configuration keys
        public const string TTS_ENABLED = "tts_enabled";
        public const string TTS_SERVICE = "tts_service";
        public const string ELEVENLABS_API_KEY = "elevenlabs_api_key";
        public const string ELEVENLABS_VOICE = "elevenlabs_voice";
        public const string GOOGLE_TTS_API_KEY = "google_tts_api_key";
        public const string GOOGLE_TTS_VOICE = "google_tts_voice";
        
        // ChatBox configuration keys
        public const string CHATBOX_FONT_FAMILY = "chatbox_font_family";
        public const string CHATBOX_FONT_SIZE = "chatbox_font_size";
        public const string CHATBOX_FONT_COLOR = "chatbox_font_color";
        public const string CHATBOX_ORIGINAL_TEXT_COLOR = "chatbox_original_text_color";
        public const string CHATBOX_TRANSLATED_TEXT_COLOR = "chatbox_translated_text_color";
        public const string CHATBOX_BACKGROUND_COLOR = "chatbox_background_color";
        public const string CHATBOX_BACKGROUND_OPACITY = "chatbox_background_opacity";
        public const string CHATBOX_WINDOW_OPACITY = "chatbox_window_opacity";
        public const string CHATBOX_LINES_OF_HISTORY = "chatbox_lines_of_history";
        public const string CHATBOX_OPACITY = "chatbox_opacity";
        public const string CHATBOX_MIN_TEXT_SIZE = "chatbox_min_text_size";
        public const string SOURCE_LANGUAGE = "source_language";
        public const string TARGET_LANGUAGE = "target_language";
        public const string AUDIO_PROCESSING_PROVIDER = "audio_processing_provider";
        public const string OPENAI_REALTIME_API_KEY = "openai_realtime_api_key";
        public const string AUDIO_SERVICE_AUTO_TRANSLATE = "audio_service_auto_translate";

        // Singleton instance
        public static ConfigManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ConfigManager();
                }
                return _instance;
            }
        }

        // Constructor
        private ConfigManager()
        {
            _configValues = new Dictionary<string, string>();
            
            // Set config file paths to be in the application's directory
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _configFilePath = Path.Combine(appDirectory, "config.txt");
            _geminiConfigFilePath = Path.Combine(appDirectory, "gemini_config.txt");
            _ollamaConfigFilePath = Path.Combine(appDirectory, "ollama_config.txt");
            _chatgptConfigFilePath = Path.Combine(appDirectory, "chatgpt_config.txt");
            _googleTranslateConfigFilePath = Path.Combine(appDirectory, "google_translate_config.txt");
            
            Console.WriteLine($"Config file path: {_configFilePath}");
            Console.WriteLine($"Gemini config file path: {_geminiConfigFilePath}");
            Console.WriteLine($"Ollama config file path: {_ollamaConfigFilePath}");
            Console.WriteLine($"ChatGPT config file path: {_chatgptConfigFilePath}");
            Console.WriteLine($"Google Translate config file path: {_googleTranslateConfigFilePath}");
            
            // Load main config values
            LoadConfig();
            
            // Load translation service from config
            if (_configValues.TryGetValue(TRANSLATION_SERVICE, out string? service))
            {
                _currentTranslationService = service;
            }
            else
            {
                // Set default and save it
                _currentTranslationService = "Gemini";
                _configValues[TRANSLATION_SERVICE] = _currentTranslationService;
                SaveConfig();
            }
            
            // Remove the old "llm_prompt_multi" entry if it exists, as it's now stored in separate files
            if (_configValues.ContainsKey("llm_prompt_multi"))
            {
                Console.WriteLine("Removing unused 'llm_prompt_multi' entry from config");
                _configValues.Remove("llm_prompt_multi");
                SaveConfig();
            }
            
            // Create service-specific config files if they don't exist
            EnsureServiceConfigFilesExist();
        }

        // Get a boolean configuration value
        public bool GetBoolValue(string key, bool defaultValue = false)
        {
            string value = GetValue(key, defaultValue.ToString().ToLower());
            return value.ToLower() == "true";
        }

        // Load configuration from file
        private void LoadConfig()
        {
            try
            {
                // Create default config if it doesn't exist
                if (!File.Exists(_configFilePath))
                {
                    Console.WriteLine("Configuration file not found. Creating default configuration.");
                    CreateDefaultConfig();
                }
                else
                {
                    // Read all content from the config file
                    string content = File.ReadAllText(_configFilePath);
                    
                    // First, process multiline values with tags
                    ProcessMultilineValues(content);
                    
                    // Then process regular single-line values
                    ProcessSingleLineValues(content);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading config: {ex.Message}");
                MessageBox.Show($"Error loading configuration: {ex.Message}", "Configuration Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            
            // Debug output: dump all loaded config values
            Console.WriteLine("=== All Loaded Config Values ===");
            foreach (var entry in _configValues)
            {
                Console.WriteLine($"  {entry.Key} = {(entry.Key.Contains("api_key") ? "***" : entry.Value)}");
            }
            Console.WriteLine("===============================");
        }
        
        public bool GetGoogleTranslateUseCloudApi()
        {
            return GetBoolValue(GOOGLE_TRANSLATE_USE_CLOUD_API, false);
        }

        public void SetGoogleTranslateUseCloudApi(bool useCloudApi)
        {
            _configValues[GOOGLE_TRANSLATE_USE_CLOUD_API] = useCloudApi.ToString();
            SaveConfig();
            Console.WriteLine($"Google Translate Cloud API usage set to: {useCloudApi}");
        }

        // Set/Get Google Translate auto language mapping
        public bool GetGoogleTranslateAutoMapLanguages()
        {
            return GetBoolValue(GOOGLE_TRANSLATE_AUTO_MAP_LANGUAGES, true);
        }

        public void SetGoogleTranslateAutoMapLanguages(bool autoMap)
        {
            _configValues[GOOGLE_TRANSLATE_AUTO_MAP_LANGUAGES] = autoMap.ToString();
            SaveConfig();
            Console.WriteLine($"Google Translate auto language mapping set to: {autoMap}");
        }

        // Create default configuration
        private void CreateDefaultConfig()
        {
            // Set default values based on current config.txt
            _configValues[GEMINI_API_KEY] = "<your API key here>";
            _configValues[AUTO_SIZE_TEXT_BLOCKS] = "true";
            _configValues[CHATBOX_FONT_FAMILY] = "Segoe UI";
            _configValues[CHATBOX_FONT_SIZE] = "15";
            _configValues[CHATBOX_FONT_COLOR] = "#FFFFFFFF";
            _configValues[CHATBOX_BACKGROUND_COLOR] = "#FF000000";
            _configValues[CHATBOX_LINES_OF_HISTORY] = "20";
            _configValues[CHATBOX_OPACITY] = "0";
            _configValues[CHATBOX_ORIGINAL_TEXT_COLOR] = "#FFFAFAD2";
            _configValues[CHATBOX_TRANSLATED_TEXT_COLOR] = "#FFFFFFFF";
            _configValues[CHATBOX_BACKGROUND_OPACITY] = "0.35";
            _configValues[CHATBOX_WINDOW_OPACITY] = "1";
            _configValues[CHATBOX_MIN_TEXT_SIZE] = "2";
            _configValues[TRANSLATION_SERVICE] = "Gemini";
            _configValues[OLLAMA_URL] = "http://localhost";
            _configValues[OLLAMA_PORT] = "11434";
            _configValues[OCR_METHOD] = "EasyOCR";
            _configValues[OLLAMA_MODEL] = "gemma3:12b";
            _configValues[SOURCE_LANGUAGE] = "ja";
            _configValues[TARGET_LANGUAGE] = "en";
            _configValues[ELEVENLABS_API_KEY] = "<your API key here>";
            _configValues[ELEVENLABS_VOICE] = "21m00Tcm4TlvDq8ikWAM";
            _configValues[TTS_SERVICE] = "Google Cloud TTS";
            _configValues[GOOGLE_TTS_API_KEY] = "<your API key here>";
            _configValues[GOOGLE_TTS_VOICE] = "ja-JP-Neural2-B";
            _configValues[TTS_ENABLED] = "true";
            _configValues[MAX_CONTEXT_PIECES] = "20";
            _configValues[MIN_CONTEXT_SIZE] = "8";
            _configValues[GAME_INFO] = "We're playing an unspecified game.";
            _configValues[MIN_TEXT_FRAGMENT_SIZE] = "1";
            _configValues[CHATGPT_MODEL] = "gpt-4.1-nano";
            _configValues[CHATGPT_API_KEY] = "<your API key here>";
            _configValues[GEMINI_MODEL] = "gemini-2.0-flash-lite";
            _configValues[BLOCK_DETECTION_SCALE] = "3.00";
            _configValues[BLOCK_DETECTION_SETTLE_TIME] = "0.15";
            _configValues[KEEP_TRANSLATED_TEXT_UNTIL_REPLACED] = "true";
            _configValues[LEAVE_TRANSLATION_ONSCREEN] = "true";
            _configValues[MIN_LETTER_CONFIDENCE] = "0.1";
            _configValues[MIN_LINE_CONFIDENCE] = "0.1";
            _configValues[AUTO_TRANSLATE_ENABLED] = "true";
            _configValues[IGNORE_PHRASES] = "";
            
            // Save the default configuration
            SaveConfig();
            Console.WriteLine("Default configuration created and saved.");
        }
        
        // Process multiline values enclosed in tags
        private void ProcessMultilineValues(string content)
        {
            try
            {
                // Use regex to find content between tags like <key_start>content<key_end>
                string pattern = @"<(\w+)_start>(.*?)<\1_end>";
                
                // Use RegexOptions.Singleline to make '.' match newlines as well
                var matches = Regex.Matches(content, pattern, RegexOptions.Singleline);
                
                foreach (Match match in matches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        string key = match.Groups[1].Value;
                        string value = match.Groups[2].Value;
                        
                        // Store the value
                        _configValues[key] = value;
                        
                        Console.WriteLine($"Loaded multiline config: {key} ({value.Length} chars)");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing multiline values: {ex.Message}");
            }
        }
        
        // Process single-line key-value pairs
        private void ProcessSingleLineValues(string content)
        {
            try
            {
                // Remove sections with multiline tags to avoid parsing them as single-line entries
                content = Regex.Replace(content, @"<\w+_start>.*?<\w+_end>", "", RegexOptions.Singleline);
                
                // Split into lines and process each line
                string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (string line in lines)
                {
                    // Skip comments and empty lines
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                        continue;
                    
                    // Skip lines that are part of multiline tags
                    if (line.Contains("_start>") || line.Contains("_end>"))
                        continue;
                    
                    // Parse config entries in format "key|value|"
                    string[] parts = line.Split('|');
                    if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[0]))
                    {
                        string key = parts[0].Trim();
                        
                        // Special handling for IGNORE_PHRASES
                        if (key == IGNORE_PHRASES)
                        {
                            // For IGNORE_PHRASES, we need to capture the full line after the key
                            string phraseValue = line.Substring(key.Length + 1);
                            // Remove trailing delimiter if present
                            if (phraseValue.EndsWith("|"))
                                phraseValue = phraseValue.Substring(0, phraseValue.Length - 1);
                                
                            _configValues[key] = phraseValue;
                            Console.WriteLine($"Loaded ignore phrases config: {key}");
                            continue;
                        }
                        
                        // Normal key-value pairs
                        string value = parts[1].Trim();
                        
                        // Only add if not already added by multiline processing
                        if (!_configValues.ContainsKey(key))
                        {
                            _configValues[key] = value;
                            Console.WriteLine($"Loaded config: {key}={value}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing single-line values: {ex.Message}");
            }
        }

        // Save configuration to file
        public void SaveConfig()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Configuration file for WPFScreenCapture");
                sb.AppendLine("# Format for single-line values: key|value|");
                sb.AppendLine("# Format for multiline values: <key_start>multiple lines of content<key_end>");
                sb.AppendLine();
                
                // First add single-line entries
                foreach (var entry in _configValues.Where(e => !ShouldBeMultiline(e.Key)))
                {
                    sb.AppendLine($"{entry.Key}|{entry.Value}|");
                }
                
                // Then add multiline entries
                foreach (var entry in _configValues.Where(e => ShouldBeMultiline(e.Key)))
                {
                    sb.AppendLine();
                    sb.AppendLine($"<{entry.Key}_start>");
                    sb.AppendLine(entry.Value);
                    sb.AppendLine($"<{entry.Key}_end>");
                }
                
                // Write to file
                File.WriteAllText(_configFilePath, sb.ToString());
                
                Console.WriteLine($"Saved config to {_configFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving config: {ex.Message}");
                MessageBox.Show($"Error saving configuration: {ex.Message}", "Configuration Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        // Determine if a key should be stored as multiline
        private bool ShouldBeMultiline(string key)
        {
            // List of keys that should use multiline format
            return key.EndsWith("_multi") || key.EndsWith("_template");
        }

        // Get a configuration value
        public string GetValue(string key, string defaultValue = "")
        {
            if (_configValues.TryGetValue(key, out var value))
            {
                //Console.WriteLine($"ConfigManager.GetValue: Found value for key '{key}': '{value}'");
                return value;
            }
            
            Console.WriteLine($"ConfigManager.GetValue: Key '{key}' not found, returning default value: '{defaultValue}'");
            return defaultValue;
        }

        // Set a configuration value
        public void SetValue(string key, string value)
        {
            _configValues[key] = value;
        }

        // Get Gemini API key
        public string GetGeminiApiKey()
        {
            return GetValue(GEMINI_API_KEY);
        }
        
        // Set Gemini API key
        public void SetGeminiApiKey(string apiKey)
        {
            _configValues[GEMINI_API_KEY] = apiKey;
            SaveConfig();
        }
        
        // Get/Set Ollama URL
        public string GetOllamaUrl()
        {
            return GetValue(OLLAMA_URL, "http://localhost");
        }
        
        public void SetOllamaUrl(string url)
        {
            _configValues[OLLAMA_URL] = url;
            SaveConfig();
        }
        
        // Get/Set Ollama Port
        public string GetOllamaPort()
        {
            return GetValue(OLLAMA_PORT, "11434");
        }
        
        public void SetOllamaPort(string port)
        {
            _configValues[OLLAMA_PORT] = port;
            SaveConfig();
        }
        
        // Get/Set Ollama Model
        public string GetOllamaModel()
        {
            return GetValue(OLLAMA_MODEL, "llama3"); // Default to llama3
        }
        
        public void SetOllamaModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                _configValues[OLLAMA_MODEL] = model;
                SaveConfig();
                Console.WriteLine($"Ollama model set to: {model}");
            }
        }
        
        // Get the full Ollama API endpoint
        public string GetOllamaApiEndpoint()
        {
            string url = GetOllamaUrl();
            string port = GetOllamaPort();
            
            // Ensure URL doesn't end with a slash
            if (url.EndsWith("/"))
            {
                url = url.Substring(0, url.Length - 1);
            }
            
            return $"{url}:{port}/api/generate";
        }
        
        // Get current translation service
        public string GetCurrentTranslationService()
        {
            return _currentTranslationService;
        }
        
        // Set current translation service
        public void SetTranslationService(string service)
        {
            if (service == "Gemini" || service == "Ollama" || service == "ChatGPT" || service == "Google Translate")
            {
                _currentTranslationService = service;
                _configValues[TRANSLATION_SERVICE] = service;
                SaveConfig();
                Console.WriteLine($"Translation service set to {service}");
            }
        }
        
        // Get current OCR method
        public string GetOcrMethod()
        {
            Console.WriteLine("Checking contents of _configValues in GetOcrMethod:");
            foreach (var key in _configValues.Keys)
            {
                Console.WriteLine($"  Config key: '{key}'");
            }
            
            string ocrMethod = GetValue(OCR_METHOD, "Windows OCR"); // Default to Windows OCR if not set
            Console.WriteLine($"ConfigManager.GetOcrMethod() returning: '{ocrMethod}'");
            return ocrMethod;
        }
        
        // Set current OCR method
        public void SetOcrMethod(string method)
        {
            Console.WriteLine($"ConfigManager.SetOcrMethod called with method: {method}");
            if (method == "Windows OCR" || method == "EasyOCR" || method == "PaddleOCR")
            {
                _configValues[OCR_METHOD] = method;
                SaveConfig();
                Console.WriteLine($"OCR method set to {method} and saved to config");
            }
            else
            {
                Console.WriteLine($"WARNING: Invalid OCR method: {method}. Must be 'Windows OCR' or 'EasyOCR' or 'PaddleOCR'");
            }
        }
        
        
        // Create service-specific config files if they don't exist
        private void EnsureServiceConfigFilesExist()
        {
            try
            {
                // Default prompts for each service
                string defaultGeminiPrompt = "Your task is to translate the source_language text in the following JSON data to target_language " +
                    "and output a new JSON in a specific format. This is text from OCR of a screenshot from a video game, " +
                    "so please try to infer the context and which parts are menu or dialog.\n" +
                    "You should:\n" +
                    "* Output ONLY the resulting JSON data.\n" +
                    "* The output JSON must have the exact same structure as the input JSON, with a source_language, target_language, and a text_blocks array.\n" +
                    "* Each element in the text_blocks array must include its id and its rect (the bounding box).\n" +
                    "* No extra text, explanations, or formatting should be included.\n" +
                    "* If \"previous_context\" data exist in the json, this should not be translated, but used to better understand the context of the text that IS being translated.\n" +
                    "* Example of output for a text block: If text_0 and text_1 were merged, the result would look like: " +
                    "{ \"id\": \"text_0\", \"text\": \"Translated text of text_0.\", \"rect\": { \"x\": 10, \"y\": 20, \"width\": 400, \"height\": 50 } }\n" +
                    "* Don't return the \"previous_context\" or \"game_info\" json parms, that's for input only, not what you output.\n" +
                    "* If the text looks like multiple options for the player to choose from, add a newline after each one " +
                    "so they aren't mushed together, but each on their own text line.\n\n" +
                    "Here is the input JSON:";
                string defaultOllamaPrompt = "Your task is to translate the source_language text in the following JSON data to target_language " +
                    "and output a new JSON in a specific format. This is text from OCR of a screenshot from a video game, " +
                    "so please try to infer the context and which parts are menu or dialog.\n" +
                    "You should:\n" +
                    "* Output ONLY the resulting JSON data.\n" +
                    "* The output JSON must have the exact same structure as the input JSON, with a source_language, target_language, and a text_blocks array.\n" +
                    "* Each element in the text_blocks array must include its id and its rect (the bounding box).\n" +
                    "* No extra text, explanations, or formatting should be included.\n" +
                    "* If \"previous_context\" data exist in the json, this should not be translated, but used to better understand the context of the text that IS being translated.\n" +
                    "* Example of output for a text block: If text_0 and text_1 were merged, the result would look like: " +
                    "{ \"id\": \"text_0\", \"text\": \"Translated text of text_0.\", \"rect\": { \"x\": 10, \"y\": 20, \"width\": 400, \"height\": 50 } }\n" +
                    "* Don't return the \"previous_context\" or \"game_info\" json parms, that's for input only, not what you output.\n" +
                    "* If the text looks like multiple options for the player to choose from, add a newline after each one " +
                    "so they aren't mushed together, but each on their own text line.\n\n" +
                    "Here is the input JSON:";
                string defaultChatGptPrompt = "Your task is to translate the source_language text in the following JSON data to target_language " +
                    "and output a new JSON in a specific format. This is text from OCR of a screenshot from a video game, " +
                    "so please try to infer the context and which parts are menu or dialog.\n" +
                    "You should:\n" +
                    "* Output ONLY the resulting JSON data.\n" +
                    "* The output JSON must have the exact same structure as the input JSON, with a source_language, target_language, and a text_blocks array.\n" +
                    "* Each element in the text_blocks array must include its id and its rect (the bounding box).\n" +
                    "* No extra text, explanations, or formatting should be included.\n" +
                    "* If \"previous_context\" data exist in the json, this should not be translated, but used to better understand the context of the text that IS being translated.\n" +
                    "* Example of output for a text block: If text_0 and text_1 were merged, the result would look like: " +
                    "{ \"id\": \"text_0\", \"text\": \"Translated text of text_0.\", \"rect\": { \"x\": 10, \"y\": 20, \"width\": 400, \"height\": 50 } }\n" +
                    "* Don't return the \"previous_context\" or \"game_info\" json parms, that's for input only, not what you output.\n" +
                    "* If the text looks like multiple options for the player to choose from, add a newline after each one " +
                    "so they aren't mushed together, but each on their own text line.\n\n" +
                    "Here is the input JSON:";
                // string defaultGoogleTranslatePrompt = "You are a translator using Google Translate API. Translate the text from the source language to the target language accurately while maintaining the original meaning and context.";
                
                // Check and create Gemini config file
                if (!File.Exists(_geminiConfigFilePath))
                {
                    string geminiContent = $"<llm_prompt_multi_start>\n{defaultGeminiPrompt}\n<llm_prompt_multi_end>";
                    File.WriteAllText(_geminiConfigFilePath, geminiContent);
                    Console.WriteLine("Created default Gemini config file");
                }
                
                // Check and create Ollama config file
                if (!File.Exists(_ollamaConfigFilePath))
                {
                    string ollamaContent = $"<llm_prompt_multi_start>\n{defaultOllamaPrompt}\n<llm_prompt_multi_end>";
                    File.WriteAllText(_ollamaConfigFilePath, ollamaContent);
                    Console.WriteLine("Created default Ollama config file");
                }
                
                // Check and create ChatGPT config file
                if (!File.Exists(_chatgptConfigFilePath))
                {
                    string chatgptContent = $"<llm_prompt_multi_start>\n{defaultChatGptPrompt}\n<llm_prompt_multi_end>";
                    File.WriteAllText(_chatgptConfigFilePath, chatgptContent);
                    Console.WriteLine("Created default ChatGPT config file");
                }
                
                // Google Translate doesn't use prompts, so no need to create config file
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error ensuring service config files: {ex.Message}");
            }
        }
        
        // Get LLM Prompt from the current translation service
        public string GetLlmPrompt()
        {
            return GetServicePrompt(_currentTranslationService);
        }
        
        // Get prompt for specific translation service
        public string GetServicePrompt(string service)
        {
            // Google Translate doesn't use prompts
            if (service == "Google Translate")
            {
                return "";
            }
            
            string filePath;
            
            switch (service)
            {
                case "Gemini":
                    filePath = _geminiConfigFilePath;
                    break;
                case "Ollama":
                    filePath = _ollamaConfigFilePath;
                    break;
                case "ChatGPT":
                    filePath = _chatgptConfigFilePath;
                    break;
                default:
                    filePath = _geminiConfigFilePath;
                    break;
            }
            
            try
            {
                if (File.Exists(filePath))
                {
                    string content = File.ReadAllText(filePath);
                    
                    // Extract prompt text using regex
                    string pattern = @"<llm_prompt_multi_start>(.*?)<llm_prompt_multi_end>";
                    Match match = Regex.Match(content, pattern, RegexOptions.Singleline);
                    
                    if (match.Success && match.Groups.Count >= 2)
                    {
                        return match.Groups[1].Value.Trim();
                    }
                }
                
                // Return default prompt if file doesn't exist or prompt not found
                return "You are a translator. Translate the text I'll provide into English. Keep it simple and conversational.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading service prompt: {ex.Message}");
                return "Error loading prompt";
            }
        }
        
        // Save prompt for specific translation service
        public bool SaveServicePrompt(string service, string prompt)
        {
            // Google Translate doesn't use prompts
            if (service == "Google Translate")
            {
                return true;
            }
            
            string filePath;
            
            switch (service)
            {
                case "Gemini":
                    filePath = _geminiConfigFilePath;
                    break;
                case "Ollama":
                    filePath = _ollamaConfigFilePath;
                    break;
                case "ChatGPT":
                    filePath = _chatgptConfigFilePath;
                    break;
                default:
                    filePath = _geminiConfigFilePath;
                    break;
            }
            
            try
            {
                string content = $"<llm_prompt_multi_start>\n{prompt}\n<llm_prompt_multi_end>";
                File.WriteAllText(filePath, content);
                Console.WriteLine($"Saved {service} prompt ({prompt.Length} chars)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving service prompt: {ex.Message}");
                return false;
            }
        }
        
        // Check if auto sizing text blocks is enabled
        public bool IsAutoSizeTextBlocksEnabled()
        {
            string value = GetValue(AUTO_SIZE_TEXT_BLOCKS, "true");
            return value.ToLower() == "true";
        }
        
        // Check if auto translate is enabled
        public bool IsAutoTranslateEnabled()
        {
            string value = GetValue(AUTO_TRANSLATE_ENABLED, "true");
            return value.ToLower() == "true";
        }
        
        // Set auto translate enabled
        public void SetAutoTranslateEnabled(bool enabled)
        {
            _configValues[AUTO_TRANSLATE_ENABLED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Auto translate enabled: {enabled}");
        }
        
        // Get ChatBox font family
        public string GetChatBoxFontFamily()
        {
            return GetValue(CHATBOX_FONT_FAMILY, "Segoe UI");
        }
        
        // Get ChatBox font size
        public double GetChatBoxFontSize()
        {
            string value = GetValue(CHATBOX_FONT_SIZE, "14");
            if (double.TryParse(value, out double fontSize) && fontSize > 0)
            {
                return fontSize;
            }
            return 14; // Default font size
        }
        
        // Get ChatBox font color
        public System.Windows.Media.Color GetChatBoxFontColor()
        {
            string value = GetValue(CHATBOX_FONT_COLOR, "#FFFFFFFF"); // Default: White
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing ChatBox font color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Colors.White;
        }
        
        // Get ChatBox background color
        public System.Windows.Media.Color GetChatBoxBackgroundColor()
        {
            string value = GetValue(CHATBOX_BACKGROUND_COLOR, "#80000000"); // Default: Semi-transparent black
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing ChatBox background color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Color.FromArgb(128, 0, 0, 0);
        }
        
        // Get ChatBox window opacity (0.1 to 1.0)
        public double GetChatBoxWindowOpacity()
        {
            string value = GetValue(CHATBOX_WINDOW_OPACITY, "1.0"); // Default: 100% opacity
            if (double.TryParse(value, out double opacity))
            {
                // Ensure minimum 10% opacity so window never disappears completely
                return Math.Max(0.1, Math.Min(1.0, opacity));
            }
            return 1.0; // Default opacity
        }
        
        // Get ChatBox background opacity (0.0 to 1.0)
        public double GetChatBoxBackgroundOpacity()
        {
            string value = GetValue(CHATBOX_BACKGROUND_OPACITY, "0.5"); // Default: 50% opacity
            if (double.TryParse(value, out double opacity) && opacity >= 0 && opacity <= 1)
            {
                return opacity;
            }
            return 0.5; // Default opacity
        }
        
        // Get Original Text color
        public System.Windows.Media.Color GetOriginalTextColor()
        {
            string value = GetValue(CHATBOX_ORIGINAL_TEXT_COLOR, "#FFF8E0A0"); // Default: Light gold
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Original Text color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Colors.LightGoldenrodYellow;
        }
        
        // Get Translated Text color
        public System.Windows.Media.Color GetTranslatedTextColor()
        {
            string value = GetValue(CHATBOX_TRANSLATED_TEXT_COLOR, "#FFFFFFFF"); // Default: White
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Translated Text color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Colors.White;
        }
        
        // Get ChatBox history size
        public int GetChatBoxHistorySize()
        {
            string value = GetValue(CHATBOX_LINES_OF_HISTORY, "20"); // Default: 20 entries
            if (int.TryParse(value, out int historySize) && historySize > 0)
            {
                return historySize;
            }
            return 20; // Default history size
        }
        
        // Get min ChatBox text size
        public int GetChatBoxMinTextSize()
        {
            string value = GetValue(CHATBOX_MIN_TEXT_SIZE, "2"); // Default: 2 characters
            if (int.TryParse(value, out int minSize) && minSize >= 0)
            {
                return minSize;
            }
            return 2; // Default min size
        }
        
        // Set min ChatBox text size
        public void SetChatBoxMinTextSize(int value)
        {
            if (value >= 0)
            {
                _configValues[CHATBOX_MIN_TEXT_SIZE] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Min ChatBox text size set to: {value}");
            }
        }
        
        // Get/Set translation context settings
        public int GetMaxContextPieces()
        {
            string value = GetValue(MAX_CONTEXT_PIECES, "3"); // Default: 3 pieces
            if (int.TryParse(value, out int maxContextPieces) && maxContextPieces >= 0)
            {
                return maxContextPieces;
            }
            return 3; // Default: 3 context pieces
        }
        
        public void SetMaxContextPieces(int value)
        {
            if (value >= 0)
            {
                _configValues[MAX_CONTEXT_PIECES] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Max context pieces set to: {value}");
            }
        }
        
        public int GetMinContextSize()
        {
            string value = GetValue(MIN_CONTEXT_SIZE, "20"); // Default: 20 characters
            if (int.TryParse(value, out int minContextSize) && minContextSize >= 0)
            {
                return minContextSize;
            }
            return 20; // Default: 20 characters
        }
        
        public void SetMinContextSize(int value)
        {
            if (value >= 0)
            {
                _configValues[MIN_CONTEXT_SIZE] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Min context size set to: {value}");
            }
        }
        
        // Get/Set game info
        public string GetGameInfo()
        {
            return GetValue(GAME_INFO, "");
        }
        
        public void SetGameInfo(string gameInfo)
        {
            _configValues[GAME_INFO] = gameInfo;
            SaveConfig();
            Console.WriteLine($"Game info set to: {gameInfo}");
        }
        
        // Get/Set minimum text fragment size
        public int GetMinTextFragmentSize()
        {
            string value = GetValue(MIN_TEXT_FRAGMENT_SIZE, "2"); // Default: 2 characters
            if (int.TryParse(value, out int minSize) && minSize >= 0)
            {
                return minSize;
            }
            return 2; // Default: 2 characters
        }
        
        public void SetMinTextFragmentSize(int value)
        {
            if (value >= 0)
            {
                _configValues[MIN_TEXT_FRAGMENT_SIZE] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Minimum text fragment size set to: {value}");
            }
        }
        
        // Get/Set minimum letter confidence
        public double GetMinLetterConfidence()
        {
            string value = GetValue(MIN_LETTER_CONFIDENCE, "0.1"); // Default: 0.1 (10%)
            if (double.TryParse(value, out double minConfidence) && minConfidence >= 0 && minConfidence <= 1)
            {
                return minConfidence;
            }
            return 0.1; // Default: 0.1 (10%)
        }
        
        public void SetMinLetterConfidence(double value)
        {
            if (value >= 0 && value <= 1)
            {
                _configValues[MIN_LETTER_CONFIDENCE] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Minimum letter confidence set to: {value}");
            }
            else
            {
                Console.WriteLine($"Invalid minimum letter confidence: {value}. Must be between 0 and 1.");
            }
        }
        
        // Get/Set minimum line confidence
        public double GetMinLineConfidence()
        {
            string value = GetValue(MIN_LINE_CONFIDENCE, "0.2"); // Default: 0.2 (20%)
            if (double.TryParse(value, out double minConfidence) && minConfidence >= 0 && minConfidence <= 1)
            {
                return minConfidence;
            }
            return 0.2; // Default: 0.2 (20%)
        }
        
        public void SetMinLineConfidence(double value)
        {
            if (value >= 0 && value <= 1)
            {
                _configValues[MIN_LINE_CONFIDENCE] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Minimum line confidence set to: {value}");
            }
            else
            {
                Console.WriteLine($"Invalid minimum line confidence: {value}. Must be between 0 and 1.");
            }
        }
        
        // Get/Set source language
        public string GetSourceLanguage()
        {
            return GetValue(SOURCE_LANGUAGE, "ja"); // Default to Japanese
        }
        
        public void SetSourceLanguage(string language)
        {
            if (!string.IsNullOrWhiteSpace(language))
            {
                _configValues[SOURCE_LANGUAGE] = language;
                SaveConfig();
                Console.WriteLine($"Source language set to: {language}");
            }
        }
        
        // Get/Set target language
        public string GetTargetLanguage()
        {
            return GetValue(TARGET_LANGUAGE, "en"); // Default to English
        }
        
        public void SetTargetLanguage(string language)
        {
            if (!string.IsNullOrWhiteSpace(language))
            {
                _configValues[TARGET_LANGUAGE] = language;
                SaveConfig();
                Console.WriteLine($"Target language set to: {language}");
            }
        }
        
        // Text-to-Speech methods
        
        // Get/Set TTS enabled
        public bool IsTtsEnabled()
        {
            string value = GetValue(TTS_ENABLED, "true");
            return value.ToLower() == "true";
        }
        
        public void SetTtsEnabled(bool enabled)
        {
            _configValues[TTS_ENABLED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"TTS enabled: {enabled}");
        }
        
        // Get/Set TTS service
        public string GetTtsService()
        {
            return GetValue(TTS_SERVICE, "ElevenLabs"); // Default to ElevenLabs
        }
        
        public void SetTtsService(string service)
        {
            if (!string.IsNullOrWhiteSpace(service))
            {
                _configValues[TTS_SERVICE] = service;
                SaveConfig();
                Console.WriteLine($"TTS service set to: {service}");
            }
        }
        
        // Get/Set ElevenLabs API key
        public string GetElevenLabsApiKey()
        {
            return GetValue(ELEVENLABS_API_KEY, "");
        }
        
        public void SetElevenLabsApiKey(string apiKey)
        {
            _configValues[ELEVENLABS_API_KEY] = apiKey;
            SaveConfig();
            Console.WriteLine("ElevenLabs API key updated");
        }
        
        // Get/Set ElevenLabs voice
        public string GetElevenLabsVoice()
        {
            return GetValue(ELEVENLABS_VOICE, "21m00Tcm4TlvDq8ikWAM"); // Default to Rachel
        }
        
        public void SetElevenLabsVoice(string voiceId)
        {
            if (!string.IsNullOrWhiteSpace(voiceId))
            {
                _configValues[ELEVENLABS_VOICE] = voiceId;
                SaveConfig();
                Console.WriteLine($"ElevenLabs voice set to: {voiceId}");
            }
        }
        
        // Google TTS methods
        
        // Get/Set Google TTS API key
        public string GetGoogleTtsApiKey()
        {
            return GetValue(GOOGLE_TTS_API_KEY, "");
        }
        
        public void SetGoogleTtsApiKey(string apiKey)
        {
            _configValues[GOOGLE_TTS_API_KEY] = apiKey;
            SaveConfig();
            Console.WriteLine("Google TTS API key updated");
        }
        
        // Get/Set Google TTS voice
        public string GetGoogleTtsVoice()
        {
            return GetValue(GOOGLE_TTS_VOICE, "ja-JP-Neural2-B"); // Default to Female - Neural2
        }
        
        public void SetGoogleTtsVoice(string voiceId)
        {
            if (!string.IsNullOrWhiteSpace(voiceId))
            {
                _configValues[GOOGLE_TTS_VOICE] = voiceId;
                SaveConfig();
                Console.WriteLine($"Google TTS voice set to: {voiceId}");
            }
        }
        
        // ChatGPT methods
        
        // Get/Set ChatGPT API key
        public string GetChatGptApiKey()
        {
            return GetValue(CHATGPT_API_KEY, "");
        }
        
        public void SetChatGptApiKey(string apiKey)
        {
            _configValues[CHATGPT_API_KEY] = apiKey;
            SaveConfig();
            Console.WriteLine("ChatGPT API key updated");
        }
        
        // Get/Set ChatGPT model
        public string GetChatGptModel()
        {
            return GetValue(CHATGPT_MODEL, "gpt-3.5-turbo"); // Default to gpt-3.5-turbo
        }
        
        public void SetChatGptModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                _configValues[CHATGPT_MODEL] = model;
                SaveConfig();
                Console.WriteLine($"ChatGPT model set to: {model}");
            }
        }
        
        // Gemini methods
        
        // Get Gemini model
        public string GetGeminiModel()
        {
            return GetValue(GEMINI_MODEL, "gemini-2.0-flash-lite"); // Default to 2.0 Flash-lite
        }
        
        // Set Gemini model
        public void SetGeminiModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                _configValues[GEMINI_MODEL] = model;
                SaveConfig();
                Console.WriteLine($"Gemini model set to: {model}");
            }
        }
        
        // OCR Display methods
        
        // Check if translation should stay onscreen until replaced
        public bool IsLeaveTranslationOnscreenEnabled()
        {
            string value = GetValue(LEAVE_TRANSLATION_ONSCREEN, "false");
            return value.ToLower() == "true";
        }
        
        // Set whether translation should stay onscreen until replaced
        public void SetLeaveTranslationOnscreenEnabled(bool enabled)
        {
            _configValues[LEAVE_TRANSLATION_ONSCREEN] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Leave translation onscreen enabled: {enabled}");
        }
        
        // Check if translated text should be kept until replaced
        public bool IsKeepTranslatedTextUntilReplacedEnabled()
        {
            string value = GetValue(KEEP_TRANSLATED_TEXT_UNTIL_REPLACED, "true");
            return value.ToLower() == "true";
        }
        
        // Set whether translated text should be kept until replaced
        public void SetKeepTranslatedTextUntilReplacedEnabled(bool enabled)
        {
            _configValues[KEEP_TRANSLATED_TEXT_UNTIL_REPLACED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Keep translated text until replaced enabled: {enabled}");
        }
        
        // Block Detection methods
        
        // Get Block Detection Scale
        public double GetBlockDetectionScale()
        {
            string value = GetValue(BLOCK_DETECTION_SCALE, "5.0");
            if (double.TryParse(value, out double scale) && scale > 0)
            {
                return scale;
            }
            return 5.0; // Default
        }
        
        // Set Block Detection Scale
        public void SetBlockDetectionScale(double scale)
        {
            if (scale > 0)
            {
                _configValues[BLOCK_DETECTION_SCALE] = scale.ToString("F2");
                SaveConfig();
                Console.WriteLine($"Block detection scale set to: {scale:F2}");
            }
        }
        
        // Get Block Detection Settle Time
        public double GetBlockDetectionSettleTime()
        {
            string value = GetValue(BLOCK_DETECTION_SETTLE_TIME, "0.15");
            if (double.TryParse(value, out double time) && time >= 0)
            {
                return time;
            }
            return 0.15; // Default
        }
        
        // Set Block Detection Settle Time
        public void SetBlockDetectionSettleTime(double seconds)
        {
            if (seconds >= 0)
            {
                _configValues[BLOCK_DETECTION_SETTLE_TIME] = seconds.ToString("F2");
                SaveConfig();
                Console.WriteLine($"Block detection settle time set to: {seconds:F2} seconds");
            }
        }

        // Get all ignore phrases as a list of tuples (phrase, exactMatch)

        //OPTIMIZE:  Why is the AI doing all this work over and over?  Should be caching the results
        public List<(string Phrase, bool ExactMatch)> GetIgnorePhrases()
        {
            List<(string, bool)> result = new List<(string, bool)>();
            string value = GetValue(IGNORE_PHRASES, "");
            
            if (!string.IsNullOrEmpty(value))
            {
                // Fix the format if it contains the key prefix (from old format)
                if (value.StartsWith(IGNORE_PHRASES + "|"))
                {
                    value = value.Substring((IGNORE_PHRASES + "|").Length);
                }
                
                // Format should be: phrase1|True|phrase2|False
                string[] parts = value.Split('|');
                
                // Process in pairs
                for (int i = 0; i < parts.Length - 1; i += 2)
                {
                    if (i + 1 < parts.Length)
                    {
                        string phrase = parts[i];
                        bool exactMatch = bool.TryParse(parts[i + 1], out bool match) && match;
                        
                        if (!string.IsNullOrEmpty(phrase))
                        {
                            result.Add((phrase, exactMatch));
                            //Console.WriteLine($"Loaded ignore phrase: '{phrase}' (Exact Match: {exactMatch})");
                        }
                    }
                }
            }
            
            return result;
        }
        
        // Save all ignore phrases
        public void SaveIgnorePhrases(List<(string Phrase, bool ExactMatch)> phrases)
        {
            StringBuilder sb = new StringBuilder();
            
            foreach (var (phrase, exactMatch) in phrases)
            {
                if (!string.IsNullOrEmpty(phrase))
                {
                    sb.Append(phrase);
                    sb.Append('|');
                    sb.Append(exactMatch.ToString());
                    sb.Append('|');
                }
            }
            
            _configValues[IGNORE_PHRASES] = sb.ToString();
            SaveConfig();
            Console.WriteLine($"Saved {phrases.Count} ignore phrases: {sb.ToString()}");
        }
        
        // Add a single ignore phrase
        public void AddIgnorePhrase(string phrase, bool exactMatch)
        {
            if (string.IsNullOrEmpty(phrase))
                return;
                
            var phrases = GetIgnorePhrases();
            
            // Check if the phrase already exists
            if (!phrases.Any(p => p.Phrase == phrase))
            {
                phrases.Add((phrase, exactMatch));
                SaveIgnorePhrases(phrases);
                Console.WriteLine($"Added ignore phrase: '{phrase}' (Exact Match: {exactMatch})");
            }
        }
        
        // Remove a single ignore phrase
        public void RemoveIgnorePhrase(string phrase)
        {
            if (string.IsNullOrEmpty(phrase))
                return;
                
            var phrases = GetIgnorePhrases();
            var originalCount = phrases.Count;
            
            phrases.RemoveAll(p => p.Phrase == phrase);
            
            if (phrases.Count < originalCount)
            {
                SaveIgnorePhrases(phrases);
                Console.WriteLine($"Removed ignore phrase: '{phrase}'");
            }
        }
        
        // Update exact match setting for a phrase
        public void UpdateIgnorePhraseExactMatch(string phrase, bool exactMatch)
        {
            if (string.IsNullOrEmpty(phrase))
                return;
                
            var phrases = GetIgnorePhrases();
            
            for (int i = 0; i < phrases.Count; i++)
            {
                if (phrases[i].Phrase == phrase)
                {
                    phrases[i] = (phrase, exactMatch);
                    SaveIgnorePhrases(phrases);
                    Console.WriteLine($"Updated ignore phrase: '{phrase}' (Exact Match: {exactMatch})");
                    break;
                }
            }
        }
        public string GetGoogleTranslateApiKey()
        {
            return GetValue(GOOGLE_TRANSLATE_API_KEY, "");
        }

        public void SetGoogleTranslateApiKey(string apiKey)
        {
            _configValues[GOOGLE_TRANSLATE_API_KEY] = apiKey;
            SaveConfig();
            Console.WriteLine("Google Translate API key updated");
        }

        public string GetAudioProcessingProvider()
        {
            return GetValue(AUDIO_PROCESSING_PROVIDER, "OpenAI Realtime API");
        }
        public void SetAudioProcessingProvider(string provider)
        {
            _configValues[AUDIO_PROCESSING_PROVIDER] = provider;
            SaveConfig();
        }
        public string GetOpenAiRealtimeApiKey()
        {
            return GetValue(OPENAI_REALTIME_API_KEY, "");
        }
        public void SetOpenAiRealtimeApiKey(string apiKey)
        {
            _configValues[OPENAI_REALTIME_API_KEY] = apiKey;
            SaveConfig();
        }
        // Get whether audio service should auto-translate transcripts
        public bool IsAudioServiceAutoTranslateEnabled()
        {
            return GetBoolValue(AUDIO_SERVICE_AUTO_TRANSLATE, false);
        }
        // Set whether audio service should auto-translate transcripts
        public void SetAudioServiceAutoTranslateEnabled(bool enabled)
        {
            _configValues[AUDIO_SERVICE_AUTO_TRANSLATE] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Audio service auto-translate enabled: {enabled}");
        }
    }
}