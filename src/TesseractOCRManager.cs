using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text;
using Tesseract;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace RSTGameTranslation
{
    public class TesseractOCRManager
    {
        private static TesseractOCRManager? _instance;
        private TesseractEngine? _engine;
        private string _currentLanguage = "eng";
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
        private readonly Dictionary<string, string> LanguageMap = new Dictionary<string, string>
        {
            { "en", "eng" },
            { "ch_sim", "chi_sim" },
            { "es", "spa" },
            { "fr", "fra" },
            { "it", "ita" },
            { "de", "deu" },
            { "ru", "rus" },
            { "ar", "ara" },
            { "pt", "por" },
            { "nl", "nld" },
            { "ja", "jpn" },
            { "ko", "kor" },
            { "vi", "vie" },
            { "pl", "pol" },
            { "ro", "ron" },
            { "hi", "hin" },
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
                if (LanguageMap.TryGetValue(languageCode, out string? tessLanguage))
                {
                    _currentLanguage = tessLanguage;
                }
                else
                {
                    _currentLanguage = languageCode;
                }

                // Check if language data file exists
                string langDataFile = Path.Combine(tessDataPath, $"{_currentLanguage}.traineddata");
                if (!File.Exists(langDataFile))
                {
                    Console.WriteLine($"Tesseract language data file not found: {langDataFile}");
                    return false;
                }

                // Initialize Tesseract engine
                _engine = new TesseractEngine(tessDataPath, _currentLanguage, EngineMode.LstmOnly);
                _engine.DefaultPageSegMode = PageSegMode.SingleBlock;
                if (_currentLanguage == "ch_sim" || _currentLanguage == "jpn")
                {
                    _engine.SetVariable("preserve_interword_spaces", 1);
                    _engine.SetVariable("chop_enable", true);
                    _engine.SetVariable("use_new_state_cost", false);
                    _engine.SetVariable("segment_segcost_rating", false);
                    _engine.SetVariable("enable_new_segsearch", 0);
                    _engine.SetVariable("language_model_ngram_on", 0);
                    _engine.SetVariable("textord_force_make_prop_words", false);
                    _engine.SetVariable("edges_max_children_per_outline", 40);
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

        public bool CheckLanguagePackInstall(string languageCode)
        {
            try
            {
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string tessDataPath = Path.Combine(baseDirectory, "tessdata");

                // Map language code if needed
                string tessLanguage;
                if (LanguageMap.TryGetValue(languageCode, out string? mappedLanguage))
                {
                    tessLanguage = mappedLanguage;
                }
                else
                {
                    tessLanguage = languageCode;
                }

                // Check if language data file exists
                string langDataFile = Path.Combine(tessDataPath, $"{tessLanguage}.traineddata");
                bool exists = File.Exists(langDataFile);

                Console.WriteLine($"Tesseract language pack {tessLanguage}: {(exists ? "Installed" : "Not installed")}");
                return exists;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking Tesseract language pack: {ex.Message}");
                return false;
            }
        }
        
        private Tesseract.Pix ConvertBitmapToPix(Bitmap bitmap)
        {
            try
            {
                // Preprocess the image for OCR
                using (var optimizedBitmap = PreprocessForOcr(bitmap))
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        // Use TIFF format without compression for best quality
                        optimizedBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Tiff);
                        memoryStream.Position = 0;
                        
                        // Create Pix from memory data
                        return Tesseract.Pix.LoadFromMemory(memoryStream.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting Bitmap to Pix: {ex.Message}");
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
                if (!_isInitialized || _currentLanguage != languageCode)
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
                    using (var pix = ConvertBitmapToPix(enhancedBitmap))
                    {
                        // Process the image with Tesseract
                        using (var page = _engine.Process(pix))
                        {
                            // Get text and confidence
                            var text = page.GetText();
                            byte[] bytes = Encoding.Default.GetBytes(text);
                            text = Encoding.UTF8.GetString(bytes);
                            var overallConfidence = page.GetMeanConfidence();

                            Console.WriteLine($"Tesseract OCR recognized text with {overallConfidence:P2} confidence");

                            // threshold for each character
                            float minCharConfidence = 0.4f; // adjust as needed
                            
                            // Get character level information
                            var results = new List<object>();
                            using (var iterator = page.GetIterator())
                            {
                                iterator.Begin();

                                do
                                {
                                    if (iterator.IsAtBeginningOf(PageIteratorLevel.TextLine))
                                    {
                                        // Get line bounding box
                                        Tesseract.Rect lineBounds;
                                        if (iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out lineBounds))
                                        {
                                            do
                                            {
                                                if (iterator.IsAtBeginningOf(PageIteratorLevel.Word))
                                                {
                                                    // Get word bounding box
                                                    Tesseract.Rect wordBounds;
                                                    if (iterator.TryGetBoundingBox(PageIteratorLevel.Word, out wordBounds))
                                                    {
                                                        // get word confidence
                                                        float wordConfidence = iterator.GetConfidence(PageIteratorLevel.Word) / 100.0f;
                                                        
                                                        string word = iterator.GetText(PageIteratorLevel.Word);
                                                        if (!string.IsNullOrEmpty(word))
                                                        {
                                                            // skip word with low confidence
                                                            if (wordConfidence < minCharConfidence)
                                                            {
                                                                Console.WriteLine($"Skipping low confidence word: '{word}' ({wordConfidence:P2})");
                                                                continue;
                                                            }
                                                            
                                                            // Process each character in the word
                                                            double charWidth = wordBounds.Width / word.Length;

                                                            for (int i = 0; i < word.Length; i++)
                                                            {
                                                                string charText = word[i].ToString();
                                                                
                                                                // get character confidence if available
                                                                float charConfidence = wordConfidence; // default use word confidence
                                                                
                                                                // try get character confidence if iterator supports
                                                                try
                                                                {
                                                                    if (iterator.TryGetBoundingBox(PageIteratorLevel.Symbol, out var symbolBounds))
                                                                    {
                                                                        charConfidence = iterator.GetConfidence(PageIteratorLevel.Symbol) / 100.0f;
                                                                    }
                                                                }
                                                                catch
                                                                {
                                                                    // skip error and use word confidence
                                                                }
                                                                
                                                                // skip character with low confidence
                                                                if (charConfidence < minCharConfidence)
                                                                {
                                                                    Console.WriteLine($"Skipping low confidence character: '{charText}' ({charConfidence:P2})");
                                                                    continue;
                                                                }

                                                                // Calculate character position
                                                                double charX = wordBounds.X1 + (i * charWidth);

                                                                // Create bounding box for this character
                                                                var charBox = new[] {
                                                                    new[] { charX, (double)wordBounds.Y1 },
                                                                    new[] { charX + charWidth, (double)wordBounds.Y1 },
                                                                    new[] { charX + charWidth, (double)wordBounds.Y2 },
                                                                    new[] { charX, (double)wordBounds.Y2 }
                                                                };

                                                                // Add the character to results
                                                                results.Add(new
                                                                {
                                                                    text = charText,
                                                                    confidence = charConfidence,
                                                                    rect = charBox,
                                                                    is_character = true
                                                                });
                                                            }

                                                            // Add space after word if not at end of line
                                                            if (!iterator.IsAtFinalOf(PageIteratorLevel.TextLine, PageIteratorLevel.Word))
                                                            {
                                                                double spaceX = wordBounds.X2;
                                                                double spaceWidth = charWidth * 0.6;

                                                                var spaceBox = new[] {
                                                                    new[] { spaceX, (double)wordBounds.Y1 },
                                                                    new[] { spaceX + spaceWidth, (double)wordBounds.Y1 },
                                                                    new[] { spaceX + spaceWidth, (double)wordBounds.Y2 },
                                                                    new[] { spaceX, (double)wordBounds.Y2 }
                                                                };

                                                                results.Add(new
                                                                {
                                                                    text = " ",
                                                                    confidence = wordConfidence, // use word confidence for space
                                                                    rect = spaceBox,
                                                                    is_character = true
                                                                });
                                                            }
                                                        }
                                                    }
                                                }
                                            } while (iterator.Next(PageIteratorLevel.Word));
                                        }
                                    }
                                } while (iterator.Next(PageIteratorLevel.TextLine));
                            }

                            // check if there is any result
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

        // Download and install Tesseract language pack
        public async Task<bool> InstallLanguagePack(string languageCode)
        {
            try
            {
                // Map language code if needed
                string tessLanguage;
                if (LanguageMap.TryGetValue(languageCode, out string? mappedLanguage))
                {
                    tessLanguage = mappedLanguage;
                }
                else
                {
                    tessLanguage = languageCode;
                }

                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string tessDataPath = Path.Combine(baseDirectory, "tessdata");

                // Ensure tessdata directory exists
                if (!Directory.Exists(tessDataPath))
                {
                    Directory.CreateDirectory(tessDataPath);
                }

                // Check if language data file already exists
                string langDataFile = Path.Combine(tessDataPath, $"{tessLanguage}.traineddata");
                if (File.Exists(langDataFile))
                {
                    Console.WriteLine($"Tesseract language pack {tessLanguage} is already installed");
                    return true;
                }

                // URL to download language data from GitHub
                string downloadUrl = $"https://github.com/tesseract-ocr/tessdata/raw/main/{tessLanguage}.traineddata";

                // Download the language data file
                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    Console.WriteLine($"Downloading Tesseract language pack: {tessLanguage}");

                    // set timeout for request
                    httpClient.Timeout = TimeSpan.FromMinutes(5); // increase timeout because language file can be large

                    // download file by HttpClient
                    var response = await httpClient.GetAsync(downloadUrl);

                    // ensure request is successful
                    response.EnsureSuccessStatusCode();

                    // read data as byte array
                    byte[] fileBytes = await response.Content.ReadAsByteArrayAsync();

                    // write data to file
                    await File.WriteAllBytesAsync(langDataFile, fileBytes);
                }

                Console.WriteLine($"Tesseract language pack {tessLanguage} installed successfully");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error installing Tesseract language pack: {ex.Message}");
                return false;
            }
        }
    }
}