using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text;
using TesseractOCR;
using TesseractOCR.Pix;
using OpenCvSharp;
using OpenCvSharp.Extensions;
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
                            float minCharConfidence = 0.4f; // adjust as needed
                            
                            // Get character level information
                            var results = new List<object>();
                            
                            // Try to get the LSTM box text which contains character-level information
                            string lstmBoxText = page.LstmBoxText;
                            
                            if (!string.IsNullOrEmpty(lstmBoxText))
                            {
                                Console.WriteLine("Using LSTM box text for character positioning");
                                
                                // Parse LSTM box text (format: "char left top right bottom page")
                                string[] lines = lstmBoxText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                
                                // Get image height for coordinate inversion
                                int imageHeight = image.Height;
                                
                                // Sort lines by top coordinate (ascending) to maintain correct reading order
                                var sortedCharInfos = new List<(string Char, int Left, int Top, int Right, int Bottom)>();
                                
                                foreach (string line in lines)
                                {
                                    string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                    
                                    // LSTM box format should have at least 6 parts: char left top right bottom page
                                    if (parts.Length >= 6)
                                    {
                                        string charText = parts[0];
                                        
                                        // Skip empty characters
                                        if (string.IsNullOrEmpty(charText))
                                            continue;
                                        
                                        // Parse bounding box coordinates
                                        if (int.TryParse(parts[1], out int left) &&
                                            int.TryParse(parts[2], out int top) &&
                                            int.TryParse(parts[3], out int right) &&
                                            int.TryParse(parts[4], out int bottom))
                                        {
                                            // Add to sorted list
                                            sortedCharInfos.Add((charText, left, top, right, bottom));
                                        }
                                    }
                                }
                                
                                // Sort by top coordinate (y-axis) first, then by left coordinate (x-axis)
                                // This ensures correct reading order: top to bottom, left to right
                                sortedCharInfos = sortedCharInfos.OrderBy(c => c.Top).ThenBy(c => c.Left).ToList();
                                
                                // Process each character
                                foreach (var charInfo in sortedCharInfos)
                                {
                                    // Use overall confidence for character
                                    float charConfidence = overallConfidence;
                                    
                                    // Skip character with low confidence
                                    if (charConfidence < minCharConfidence)
                                    {
                                        Console.WriteLine($"Skipping low confidence character: '{charInfo.Char}' ({charConfidence:P2})");
                                        continue;
                                    }
                                    
                                    // Create bounding box for this character
                                    // Format: [[top-left], [top-right], [bottom-right], [bottom-left]]
                                    var charBox = new[] {
                                        new[] { (double)charInfo.Left, (double)charInfo.Top },
                                        new[] { (double)charInfo.Right, (double)charInfo.Top },
                                        new[] { (double)charInfo.Right, (double)charInfo.Bottom },
                                        new[] { (double)charInfo.Left, (double)charInfo.Bottom }
                                    };
                                    
                                    // Add the character to results
                                    results.Add(new
                                    {
                                        text = charInfo.Char,
                                        confidence = charConfidence,
                                        rect = charBox,
                                        is_character = true
                                    });
                                }
                            }
                            else
                            {
                                Console.WriteLine("LSTM box text not available, falling back to estimating positions");
                            }
                            
                            // If LSTM boxes didn't provide any results, fall back to the original method
                            if (results.Count == 0)
                            {
                                Console.WriteLine("No results from LSTM boxes, using fallback method");
                                
                                // Fall back to original method (estimating positions)
                                if (!string.IsNullOrEmpty(text))
                                {
                                    // Get image dimensions
                                    int imageWidth = image.Width;
                                    int imageHeight = image.Height;
                                    
                                    // Split text into lines
                                    string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                    
                                    // Estimate line height based on image height and number of lines
                                    double lineHeight = imageHeight / Math.Max(lines.Length, 1);
                                    
                                    // Process each line from top to bottom
                                    for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                                    {
                                        string line = lines[lineIndex];
                                        
                                        // Calculate Y position for this line
                                        double lineTop = lineIndex * lineHeight;
                                        double lineBottom = lineTop + lineHeight;
                                        
                                        // Split line into words
                                        string[] words = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                        
                                        // Estimate word width based on image width and number of characters in line
                                        int totalCharsInLine = line.Length;
                                        double avgCharWidth = imageWidth / Math.Max(totalCharsInLine, 1);
                                        
                                        // Process each word
                                        double currentX = 0;
                                        
                                        foreach (string word in words)
                                        {
                                            // Skip empty words
                                            if (string.IsNullOrEmpty(word))
                                                continue;
                                            
                                            // Use overall confidence for word
                                            float wordConfidence = overallConfidence;
                                            
                                            // Skip word with low confidence
                                            if (wordConfidence < minCharConfidence)
                                            {
                                                Console.WriteLine($"Skipping low confidence word: '{word}' ({wordConfidence:P2})");
                                                continue;
                                            }
                                            
                                            // Process each character in the word
                                            for (int i = 0; i < word.Length; i++)
                                            {
                                                string charText = word[i].ToString();
                                                
                                                // Use word confidence for character
                                                float charConfidence = wordConfidence;
                                                
                                                // Skip character with low confidence
                                                if (charConfidence < minCharConfidence)
                                                {
                                                    Console.WriteLine($"Skipping low confidence character: '{charText}' ({charConfidence:P2})");
                                                    continue;
                                                }
                                                
                                                // Calculate character position
                                                double charLeft = currentX;
                                                double charRight = charLeft + avgCharWidth;
                                                
                                                // Create bounding box for this character
                                                var charBox = new[] {
                                                    new[] { charLeft, lineTop },
                                                    new[] { charRight, lineTop },
                                                    new[] { charRight, lineBottom },
                                                    new[] { charLeft, lineBottom }
                                                };
                                                
                                                // Add the character to results
                                                results.Add(new
                                                {
                                                    text = charText,
                                                    confidence = charConfidence,
                                                    rect = charBox,
                                                    is_character = true
                                                });
                                                
                                                // Move to next character position
                                                currentX += avgCharWidth;
                                            }
                                            
                                            // Add space after word if not the last word
                                            if (System.Array.IndexOf(words, word) < words.Length - 1)
                                            {
                                                double spaceLeft = currentX;
                                                double spaceRight = spaceLeft + avgCharWidth;
                                                
                                                var spaceBox = new[] {
                                                    new[] { spaceLeft, lineTop },
                                                    new[] { spaceRight, lineTop },
                                                    new[] { spaceRight, lineBottom },
                                                    new[] { spaceLeft, lineBottom }
                                                };
                                                
                                                results.Add(new
                                                {
                                                    text = " ",
                                                    confidence = wordConfidence,
                                                    rect = spaceBox,
                                                    is_character = true
                                                });
                                                
                                                // Move past the space
                                                currentX += avgCharWidth;
                                            }
                                        }
                                    }
                                }
                            }
                            
                            // Check if there is any result
                            if (results.Count == 0)
                            {
                                Console.WriteLine("No text detected with sufficient confidence");
                                return false;
                            }

                            // Create JSON response similar to other OCR engines
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
        private Bitmap OptimizeImageForOcr(Bitmap source)
        {
            try
            {
                // Convert Bitmap to OpenCV Mat
                using (var srcMat = source.ToMat())
                {
                    // Create grayscale Mat
                    using (var grayMat = new Mat())
                    {
                        // Convert to grayscale
                        if (srcMat.Channels() != 1)
                        {
                            Cv2.CvtColor(srcMat, grayMat, ColorConversionCodes.BGR2GRAY);
                        }
                        else
                        {
                            srcMat.CopyTo(grayMat);
                        }

                        // Apply CLAHE (Contrast Limited Adaptive Histogram Equalization)
                        using (var enhancedMat = new Mat())
                        {
                            // Apply CLAHE (Contrast Limited Adaptive Histogram Equalization)
                            var clahe = Cv2.CreateCLAHE(2.0, new OpenCvSharp.Size(8, 8));
                            clahe.Apply(grayMat, enhancedMat);

                            // Convert Mat back to Bitmap
                            return enhancedMat.ToBitmap();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OpenCV image optimization failed: {ex.Message}");
                return source; // return original image if error
            }
        }
    }
}