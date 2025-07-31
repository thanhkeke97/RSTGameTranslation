using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text;
using Tesseract;

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
                _engine = new TesseractEngine(tessDataPath, _currentLanguage, EngineMode.Default);
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
                // Lưu bitmap vào một MemoryStream
                using (var memoryStream = new MemoryStream())
                {
                    // Lưu bitmap dưới dạng PNG để tránh mất thông tin
                    bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Bmp);
                    memoryStream.Position = 0;
                    
                    // Tạo Pix từ MemoryStream
                    return Tesseract.Pix.LoadFromMemory(memoryStream.ToArray());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting Bitmap to Pix: {ex.Message}");
                throw;
            }
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
                    // Process the image with Tesseract
                    using (var page = _engine.Process(ConvertBitmapToPix(enhancedBitmap)))
                    {
                        // Get text and confidence
                        var text = page.GetText();
                        byte[] bytes = Encoding.Default.GetBytes(text);
                        text = Encoding.UTF8.GetString(bytes);
                        var confidence = page.GetMeanConfidence();

                        Console.WriteLine($"Tesseract OCR recognized text with {confidence:P2} confidence");

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
                                    Rect lineBounds;
                                    if (iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out lineBounds))
                                    {
                                        do
                                        {
                                            if (iterator.IsAtBeginningOf(PageIteratorLevel.Word))
                                            {
                                                // Get word bounding box
                                                Rect wordBounds;
                                                if (iterator.TryGetBoundingBox(PageIteratorLevel.Word, out wordBounds))
                                                {
                                                    string word = iterator.GetText(PageIteratorLevel.Word);
                                                    if (!string.IsNullOrEmpty(word))
                                                    {
                                                        // Process each character in the word
                                                        double charWidth = wordBounds.Width / word.Length;

                                                        for (int i = 0; i < word.Length; i++)
                                                        {
                                                            string charText = word[i].ToString();

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
                                                                confidence = confidence,
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
                                                                confidence = confidence,
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
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing image with Tesseract OCR: {ex.Message}");
            }
            return true;
        }

        // Optimize the image for OCR by applying filters
        private Bitmap OptimizeImageForOcr(Bitmap source)
        {
            // Create a new bitmap to hold the optimized image
            var result = new Bitmap(source.Width, source.Height);
            
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
                return new Bitmap(source);
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
                    
                    // Đặt timeout cho request
                    httpClient.Timeout = TimeSpan.FromMinutes(5); // Tăng timeout vì file ngôn ngữ có thể lớn
                    
                    // Tải file bằng HttpClient
                    var response = await httpClient.GetAsync(downloadUrl);
                    
                    // Đảm bảo request thành công
                    response.EnsureSuccessStatusCode();
                    
                    // Đọc dữ liệu dưới dạng mảng byte
                    byte[] fileBytes = await response.Content.ReadAsByteArrayAsync();
                    
                    // Ghi dữ liệu vào file
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