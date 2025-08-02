using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text;
using TesseractOCR;
using TesseractOCR.Pix;
using TesseractOCR.Enums;

namespace RSTGameTranslation
{
    public class TesseractOCRManager
    {
        private static TesseractOCRManager? _instance;
        private Engine? _engine;
        private Language _currentLanguage = Language.English;
        private string _currentTessdataCode = "eng";
        private bool _isInitialized = false;

        // Singleton pattern
        public static TesseractOCRManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TesseractOCRManager();
                }
                return _instance;
            }
        }

        private TesseractOCRManager()
        {
            // Private constructor for singleton
        }

        // Map of language codes to Tesseract language codes
        private readonly Dictionary<string, Language> LanguageMap = new Dictionary<string, Language>
        {
            { "en", Language.English },
            { "eng", Language.English },
            { "ch_sim", Language.ChineseSimplified },
            { "chi_sim", Language.ChineseSimplified },
            { "es", Language.SpanishCastilian },
            { "spa", Language.SpanishCastilian },
            { "fr", Language.French },
            { "fra", Language.French },
            { "it", Language.Italian },
            { "ita", Language.Italian },
            { "de", Language.German },
            { "deu", Language.German },
            { "ru", Language.Russian },
            { "rus", Language.Russian },
            { "ja", Language.Japanese },
            { "jpn", Language.Japanese },
            { "ko", Language.Korean },
            { "kor", Language.Korean },
            { "vi", Language.Vietnamese },
            { "vie", Language.Vietnamese },
        };
        
        private readonly Dictionary<Language, string> tessdatamap = new Dictionary<Language, string>
        {
            {Language.English, "eng"},
            {Language.ChineseSimplified, "chi_sim"},
            {Language.SpanishCastilian, "spa"},
            {Language.French, "fra"},
            {Language.Italian, "ita"},
            {Language.German, "deu"},
            {Language.Russian, "rus"},
            {Language.Japanese, "jpn"},
            {Language.Korean, "kor"},
            {Language.Vietnamese, "vie"},
        };

        public bool Initialize(string languageCode = "eng")
        {
            try
            {
                // Get the base directory of the application
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string tessDataPath = Path.Combine(baseDirectory, "tessdata");

                // Ensure tessdata directory exists
                if (!Directory.Exists(tessDataPath))
                {
                    Directory.CreateDirectory(tessDataPath);
                    Console.WriteLine($"Created tessdata directory at {tessDataPath}");
                }

                // Map language code if needed
                if (LanguageMap.TryGetValue(languageCode, out Language tessLanguage))
                {
                    _currentLanguage = tessLanguage;
                }
                else
                {
                    // Default to English if language not found
                    _currentLanguage = Language.English;
                    Console.WriteLine($"Language {languageCode} not found, using English instead");
                }

                // Map language code if needed
                if (tessdatamap.TryGetValue(_currentLanguage, out string tessdataCode))
                {
                    _currentTessdataCode = tessdataCode;
                }
                else
                {
                    // Default to English if language not found
                    _currentLanguage = Language.English;
                    Console.WriteLine($"Language {languageCode} not found, using English instead");
                }

                // Check if language data file exists
                string langDataFile = Path.Combine(tessDataPath, $"{_currentTessdataCode}.traineddata");
                if (!File.Exists(langDataFile))
                {
                    Console.WriteLine($"Tesseract language data file not found: {langDataFile}");
                    return false;
                }

                // Initialize Tesseract engine
                _engine = new Engine(tessDataPath, _currentLanguage, EngineMode.LstmOnly);
                _engine.SetVariable("tessedit_pageseg_mode", "6"); // PSM_SINGLE_BLOCK

                if (_currentLanguage == Language.ChineseSimplified || _currentLanguage == Language.Japanese)
                {
                    _engine.SetVariable("preserve_interword_spaces", "1");
                    _engine.SetVariable("chop_enable", "1");
                    _engine.SetVariable("use_new_state_cost", "0");
                    _engine.SetVariable("segment_segcost_rating", "0");
                    _engine.SetVariable("enable_new_segsearch", "0");
                    _engine.SetVariable("language_model_ngram_on", "0");
                    _engine.SetVariable("textord_force_make_prop_words", "0");
                    _engine.SetVariable("edges_max_children_per_outline", "40");
                }
                _isInitialized = true;

                Console.WriteLine($"Tesseract OCR initialized with language: {_currentLanguage}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing Tesseract OCR: {ex.Message}");
                return false;
            }
        }

        private TesseractOCR.Pix.Image ConvertBitmapToPix(Bitmap bitmap)
        {
            try
            {
                // Preprocess the image for OCR
                using (var optimizedBitmap = PreprocessForOcr(bitmap))
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        // Use PNG format for good quality and compatibility
                        optimizedBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                        memoryStream.Position = 0;
                        
                        // Create Image from memory data
                        return TesseractOCR.Pix.Image.LoadFromMemory(memoryStream.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting Bitmap to Pix Image: {ex.Message}");
                throw;
            }
        }

        private Bitmap PreprocessForOcr(Bitmap source)
        {
            // Check and convert pixel format if needed
            if (source.PixelFormat != System.Drawing.Imaging.PixelFormat.Format24bppRgb &&
                source.PixelFormat != System.Drawing.Imaging.PixelFormat.Format8bppIndexed)
            {
                var newBitmap = new Bitmap(source.Width, source.Height, 
                                        System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(newBitmap))
                {
                    g.DrawImage(source, 0, 0, source.Width, source.Height);
                }
                return newBitmap;
            }
            
            return new Bitmap(source);
        }



        public async Task<bool> ProcessImageAsync(Bitmap bitmap, string languageCode = "eng")
        {
            try
            {
                // Ensure engine is initialized with correct language
                if (!_isInitialized || !IsCurrentLanguage(languageCode))
                {
                    if (!Initialize(languageCode))
                    {
                        Console.WriteLine("Failed to initialize Tesseract OCR engine");
                        return false;
                    }
                }

                if (_engine == null)
                {
                    Console.WriteLine("Tesseract engine is null");
                    return false;
                }

                // Optimize image for OCR
                using (var enhancedBitmap = OptimizeImageForOcr(bitmap))
                {
                    // Convert the bitmap to a format Tesseract can use
                    using (var image = ConvertBitmapToPix(enhancedBitmap))
                    {
                        // Process the image with Tesseract
                        using (var page = _engine.Process(image))
                        {
                            // Get text and confidence
                            var text = page.Text;
                            byte[] bytes = Encoding.Default.GetBytes(text);
                            text = Encoding.UTF8.GetString(bytes);
                            var overallConfidence = page.MeanConfidence;

                            Console.WriteLine($"Tesseract OCR recognized text with {overallConfidence:P2} confidence");

                            // Threshold for each character
                            float minCharConfidence = 0.9f;
                            
                            // Get character level information
                            var results = new List<object>();
                            
                            // Try to get HOCR text
                            string hocrText = page.HOcrText();
                            
                            if (!string.IsNullOrEmpty(hocrText))
                            {
                                Console.WriteLine("Using HOCR text for character positioning");
                                Console.WriteLine($"HOCR sample: {hocrText.Substring(0, Math.Min(500, hocrText.Length))}...");
                                results = ParseHocrText(hocrText, image.Width, image.Height, overallConfidence, minCharConfidence);
                            }
                            
                            // If HOCR didn't work, fall back to simple method
                            if (results.Count == 0)
                            {
                                Console.WriteLine("HOCR failed, using simple method");
                                results = CreateSimpleCharacterResults(text, image.Width, image.Height, overallConfidence, minCharConfidence);
                            }
                            
                            // Check if there is any result
                            if (results.Count == 0)
                            {
                                Console.WriteLine("No text detected with sufficient confidence");
                                return false;
                            }

                            // Create JSON response
                            var response = new
                            {
                                status = "success",
                                results = results,
                                processing_time_seconds = 0.1,
                                char_level = true
                            };

                            // Convert to JSON
                            var jsonOptions = new JsonSerializerOptions
                            {
                                WriteIndented = true,
                                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                            };
                            string jsonResponse = JsonSerializer.Serialize(response, jsonOptions);

                            // Process the JSON response on the UI thread
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                Logic.Instance.ProcessReceivedTextJsonData(jsonResponse);
                            });
                            
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing image with Tesseract OCR: {ex.Message}");
                return false;
            }
        }

        private List<object> ParseHocrText(string hocrText, int imageWidth, int imageHeight, float overallConfidence, float minConfidence)
        {
            var results = new List<object>();
            
            try
            {
                // Parse HOCR HTML properly
                var doc = new System.Xml.XmlDocument();
                
                // Clean up HTML for XML parsing
                string cleanHocr = hocrText;
                
                // Remove DOCTYPE and HTML wrapper to get just the body content
                var bodyMatch = System.Text.RegularExpressions.Regex.Match(hocrText, @"<body[^>]*>(.*?)</body>", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (bodyMatch.Success)
                {
                    cleanHocr = "<root>" + bodyMatch.Groups[1].Value + "</root>";
                }
                
                // Replace HTML entities and fix self-closing tags
                cleanHocr = cleanHocr.Replace("&nbsp;", " ");
                cleanHocr = System.Text.RegularExpressions.Regex.Replace(cleanHocr, @"<(\w+)([^>]*?)/>", "<$1$2></$1>");
                
                try
                {
                    doc.LoadXml(cleanHocr);
                }
                catch
                {
                    // If XML parsing fails, fall back to regex
                    return ParseHocrWithRegex(hocrText, imageWidth, imageHeight, overallConfidence, minConfidence);
                }
                
                // Find all word elements
                var wordNodes = doc.SelectNodes("//span[@class='ocrx_word']");
                
                if (wordNodes == null || wordNodes.Count == 0)
                {
                    Console.WriteLine("No word nodes found in HOCR");
                    return ParseHocrWithRegex(hocrText, imageWidth, imageHeight, overallConfidence, minConfidence);
                }
                
                var allWords = new List<(string text, int left, int top, int right, int bottom, float confidence)>();
                
                foreach (System.Xml.XmlNode wordNode in wordNodes)
                {
                    string titleAttr = wordNode.Attributes?["title"]?.Value ?? "";
                    string wordText = wordNode.InnerText?.Trim() ?? "";
                    
                    if (string.IsNullOrEmpty(wordText)) continue;
                    
                    // Extract bounding box from title attribute
                    var bboxMatch = System.Text.RegularExpressions.Regex.Match(titleAttr, @"bbox\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)");
                    if (!bboxMatch.Success) continue;
                    
                    int left = int.Parse(bboxMatch.Groups[1].Value);
                    int top = int.Parse(bboxMatch.Groups[2].Value);
                    int right = int.Parse(bboxMatch.Groups[3].Value);
                    int bottom = int.Parse(bboxMatch.Groups[4].Value);
                    
                    // Extract confidence if available
                    float wordConfidence = overallConfidence;
                    var confMatch = System.Text.RegularExpressions.Regex.Match(titleAttr, @"x_wconf\s+(\d+)");
                    if (confMatch.Success)
                    {
                        wordConfidence = float.Parse(confMatch.Groups[1].Value) / 100.0f;
                    }
                    
                    Console.WriteLine($"Word: '{wordText}' at ({left},{top},{right},{bottom}) conf: {wordConfidence:P2}");
                    
                    allWords.Add((wordText, left, top, right, bottom, wordConfidence));
                }
                
                // Sort words by position (top to bottom, left to right)
                allWords = allWords.OrderBy(w => w.top).ThenBy(w => w.left).ToList();
                
                // Process each word
                for (int wordIndex = 0; wordIndex < allWords.Count; wordIndex++)
                {
                    var word = allWords[wordIndex];
                    
                    // Skip low confidence words
                    if (word.confidence < minConfidence)
                    {
                        Console.WriteLine($"Skipping low confidence word: '{word.text}' ({word.confidence:P2})");
                        continue;
                    }
                    
                    // Calculate character width within the word
                    double charWidth = (double)(word.right - word.left) / word.text.Length;
                    
                    // Add each character in the word
                    for (int charIndex = 0; charIndex < word.text.Length; charIndex++)
                    {
                        char c = word.text[charIndex];
                        
                        // Skip whitespace characters
                        if (char.IsWhiteSpace(c)) continue;
                        
                        // Calculate character position
                        double charLeft = word.left + (charIndex * charWidth);
                        double charRight = charLeft + charWidth;
                        
                        var charBox = new[] {
                            new[] { charLeft, (double)word.top },
                            new[] { charRight, (double)word.top },
                            new[] { charRight, (double)word.bottom },
                            new[] { charLeft, (double)word.bottom }
                        };
                        
                        results.Add(new
                        {
                            text = c.ToString(),
                            confidence = word.confidence,
                            rect = charBox,
                            is_character = true
                        });
                    }
                    
                    // Add space after word (except for last word)
                    if (wordIndex < allWords.Count - 1)
                    {
                        var nextWord = allWords[wordIndex + 1];
                        
                        // Check if we need space
                        bool needSpace = ShouldAddSpaceBetweenWords(word, nextWord);
                        
                        if (needSpace)
                        {
                            double spaceLeft = word.right;
                            double spaceRight = nextWord.left;
                            double spaceTop = Math.Min(word.top, nextWord.top);
                            double spaceBottom = Math.Max(word.bottom, nextWord.bottom);
                            
                            // Ensure reasonable space width
                            if (spaceRight <= spaceLeft)
                            {
                                spaceRight = spaceLeft + (word.right - word.left) * 0.3; // 30% of word width
                            }
                            
                            var spaceBox = new[] {
                                new[] { spaceLeft, spaceTop },
                                new[] { spaceRight, spaceTop },
                                new[] { spaceRight, spaceBottom },
                                new[] { spaceLeft, spaceBottom }
                            };
                            
                            results.Add(new
                            {
                                text = " ",
                                confidence = Math.Min(word.confidence, nextWord.confidence),
                                rect = spaceBox,
                                is_character = true
                            });
                            
                            Console.WriteLine($"Added space between '{word.text}' and '{nextWord.text}'");
                        }
                    }
                }
                
                Console.WriteLine($"Parsed {results.Count} characters from HOCR XML");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing HOCR XML: {ex.Message}");
                return ParseHocrWithRegex(hocrText, imageWidth, imageHeight, overallConfidence, minConfidence);
            }
            
            return results;
        }

        private List<object> ParseHocrWithRegex(string hocrText, int imageWidth, int imageHeight, float overallConfidence, float minConfidence)
        {
            var results = new List<object>();
            
            try
            {
                Console.WriteLine("Using regex fallback for HOCR parsing");
                
                // Regex pattern to match word spans with bounding boxes
                var wordPattern = @"<span\s+class=['""]ocrx_word['""][^>]*title=['""]([^'""]*)['""][^>]*>([^<]+)</span>";
                var matches = System.Text.RegularExpressions.Regex.Matches(hocrText, wordPattern);
                
                var allWords = new List<(string text, int left, int top, int right, int bottom, float confidence)>();
                
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        string titleAttr = match.Groups[1].Value;
                        string wordText = match.Groups[2].Value.Trim();
                        
                        if (string.IsNullOrEmpty(wordText)) continue;
                        
                        // Extract bounding box
                        var bboxMatch = System.Text.RegularExpressions.Regex.Match(titleAttr, @"bbox\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)");
                        if (!bboxMatch.Success) continue;
                        
                        int left = int.Parse(bboxMatch.Groups[1].Value);
                        int top = int.Parse(bboxMatch.Groups[2].Value);
                        int right = int.Parse(bboxMatch.Groups[3].Value);
                        int bottom = int.Parse(bboxMatch.Groups[4].Value);
                        
                        // Extract confidence
                        float wordConfidence = overallConfidence;
                        var confMatch = System.Text.RegularExpressions.Regex.Match(titleAttr, @"x_wconf\s+(\d+)");
                        if (confMatch.Success)
                        {
                            wordConfidence = float.Parse(confMatch.Groups[1].Value) / 100.0f;
                        }
                        
                        allWords.Add((wordText, left, top, right, bottom, wordConfidence));
                    }
                }
                
                // Sort and process words (same logic as XML version)
                allWords = allWords.OrderBy(w => w.top).ThenBy(w => w.left).ToList();
                
                for (int wordIndex = 0; wordIndex < allWords.Count; wordIndex++)
                {
                    var word = allWords[wordIndex];
                    
                    if (word.confidence < minConfidence) continue;
                    
                    double charWidth = (double)(word.right - word.left) / word.text.Length;
                    
                    for (int charIndex = 0; charIndex < word.text.Length; charIndex++)
                    {
                        char c = word.text[charIndex];
                        if (char.IsWhiteSpace(c)) continue;
                        
                        double charLeft = word.left + (charIndex * charWidth);
                        double charRight = charLeft + charWidth;
                        
                        var charBox = new[] {
                            new[] { charLeft, (double)word.top },
                            new[] { charRight, (double)word.top },
                            new[] { charRight, (double)word.bottom },
                            new[] { charLeft, (double)word.bottom }
                        };
                        
                        results.Add(new
                        {
                            text = c.ToString(),
                            confidence = word.confidence,
                            rect = charBox,
                            is_character = true
                        });
                    }
                    
                    // Add space logic
                    if (wordIndex < allWords.Count - 1)
                    {
                        var nextWord = allWords[wordIndex + 1];
                        if (ShouldAddSpaceBetweenWords(word, nextWord))
                        {
                            double spaceLeft = word.right;
                            double spaceRight = nextWord.left;
                            if (spaceRight <= spaceLeft)
                            {
                                spaceRight = spaceLeft + (word.right - word.left) * 0.3;
                            }
                            
                            var spaceBox = new[] {
                                new[] { spaceLeft, (double)Math.Min(word.top, nextWord.top) },
                                new[] { spaceRight, (double)Math.Min(word.top, nextWord.top) },
                                new[] { spaceRight, (double)Math.Max(word.bottom, nextWord.bottom) },
                                new[] { spaceLeft, (double)Math.Max(word.bottom, nextWord.bottom) }
                            };
                            
                            results.Add(new
                            {
                                text = " ",
                                confidence = Math.Min(word.confidence, nextWord.confidence),
                                rect = spaceBox,
                                is_character = true
                            });
                        }
                    }
                }
                
                Console.WriteLine($"Parsed {results.Count} characters from HOCR regex");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in regex HOCR parsing: {ex.Message}");
            }
            
            return results;
        }

        private bool ShouldAddSpaceBetweenWords((string text, int left, int top, int right, int bottom, float confidence) word1,
                                            (string text, int left, int top, int right, int bottom, float confidence) word2)
        {
            // Different lines - check if vertical distance is significant
            int lineHeight = word1.bottom - word1.top;
            if (Math.Abs(word1.top - word2.top) > lineHeight * 0.5)
            {
                return true;
            }
            
            // Same line - check horizontal gap
            int horizontalGap = word2.left - word1.right;
            int avgCharWidth = ((word1.right - word1.left) + (word2.right - word2.left)) / (word1.text.Length + word2.text.Length);
            
            // Add space if gap is larger than 20% of average character width
            return horizontalGap > avgCharWidth * 0.2;
        }

        private List<object> CreateSimpleCharacterResults(string text, int imageWidth, int imageHeight, float overallConfidence, float minConfidence)
        {
            var results = new List<object>();
            
            if (string.IsNullOrEmpty(text)) return results;
            
            // Split text into lines
            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            double lineHeight = (double)imageHeight / Math.Max(lines.Length, 1);
            
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                if (string.IsNullOrEmpty(line)) continue;
                
                double lineTop = lineIndex * lineHeight;
                double lineBottom = lineTop + lineHeight;
                
                // Split into words
                string[] words = line.Split(' ');
                
                // Calculate positions
                double totalChars = line.Length;
                double charWidth = imageWidth / totalChars;
                double currentX = 0;
                
                for (int wordIndex = 0; wordIndex < words.Length; wordIndex++)
                {
                    string word = words[wordIndex];
                    
                    // Add characters in word
                    for (int i = 0; i < word.Length; i++)
                    {
                        char c = word[i];
                        
                        double charLeft = currentX;
                        double charRight = currentX + charWidth;
                        
                        var charBox = new[] {
                            new[] { charLeft, lineTop },
                            new[] { charRight, lineTop },
                            new[] { charRight, lineBottom },
                            new[] { charLeft, lineBottom }
                        };
                        
                        results.Add(new
                        {
                            text = c.ToString(),
                            confidence = overallConfidence,
                            rect = charBox,
                            is_character = true
                        });
                        
                        currentX += charWidth;
                    }
                    
                    // Add space after word (except last word)
                    if (wordIndex < words.Length - 1)
                    {
                        double spaceLeft = currentX;
                        double spaceRight = currentX + charWidth;
                        
                        var spaceBox = new[] {
                            new[] { spaceLeft, lineTop },
                            new[] { spaceRight, lineTop },
                            new[] { spaceRight, lineBottom },
                            new[] { spaceLeft, lineBottom }
                        };
                        
                        results.Add(new
                        {
                            text = " ",
                            confidence = overallConfidence,
                            rect = spaceBox,
                            is_character = true
                        });
                        
                        currentX += charWidth;
                    }
                }
            }
            
            return results;
        }






        // Helper method to check if current language matches requested language
        private bool IsCurrentLanguage(string languageCode)
        {
            if (LanguageMap.TryGetValue(languageCode, out Language tessLanguage))
            {
                return _currentLanguage == tessLanguage;
            }
            return false;
        }

        // Optimize the image for OCR by applying filters
        private System.Drawing.Bitmap OptimizeImageForOcr(System.Drawing.Bitmap source)
        {
            // Create a new bitmap to hold the optimized image
            var result = new System.Drawing.Bitmap(source.Width, source.Height);
            
            try
            {
                // Create a graphics object for drawing on the result bitmap
                using (var graphics = System.Drawing.Graphics.FromImage(result))
                {
                    // Set the graphics object to use high-quality interpolation and smoothing
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    
                    // Create a color matrix to adjust brightness and contrast
                    using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                    {
                        // Increase contrast and brightness
                        float contrast = 1.2f;
                        // Increase brightness
                        float brightness = 0.02f;
                        
                        // Create a color matrix to adjust brightness and contrast
                        float[][] colorMatrix = {
                            new float[] {contrast, 0, 0, 0, 0},
                            new float[] {0, contrast, 0, 0, 0},
                            new float[] {0, 0, contrast, 0, 0},
                            new float[] {0, 0, 0, 1, 0},
                            new float[] {brightness, brightness, brightness, 0, 1}
                        };
                        
                        attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(colorMatrix));
                        
                        // Increase gamma to make the image brighter
                        attributes.SetGamma(1.1f);
                        
                        // Draw the source bitmap onto the result bitmap, applying the color matrix and gamma correction
                        graphics.DrawImage(
                            source,
                            new System.Drawing.Rectangle(0, 0, result.Width, result.Height),
                            0, 0, source.Width, source.Height,
                            System.Drawing.GraphicsUnit.Pixel,
                            attributes);
                    }
                }
                               
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Image optimization failed: {ex.Message}");
                result.Dispose();
                return new System.Drawing.Bitmap(source);
            }
        }
    }
}