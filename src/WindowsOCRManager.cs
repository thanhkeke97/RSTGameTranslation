using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Globalization;
using System.IO;
using System.Text.Json;

using Application = System.Windows.Application;

namespace UGTLive
{
    class WindowsOCRManager
    {
        private static WindowsOCRManager? _instance;

        public static WindowsOCRManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new WindowsOCRManager();
                }
                return _instance;
            }
        }

        // Map of language codes to Windows language tags
        private readonly Dictionary<string, string> LanguageMap = new Dictionary<string, string>
        {
            { "en", "en-US" },
            { "ja", "ja-JP" },
            { "ch_sim", "zh-CN" },
            { "es", "es-ES" },
            { "fr", "fr-FR" },
            { "it", "it-IT" },
            { "de", "de-DE" },
            { "ru", "ru-RU" },
            { "id", "id-ID" },
            { "pl", "pl-PL" },
            { "hi", "hi-IN" },
            { "ko", "ko-KR" }
        };

        // Convert a System.Drawing.Bitmap to a Windows.Graphics.Imaging.SoftwareBitmap
        public async Task<SoftwareBitmap> ConvertBitmapToSoftwareBitmapAsync(System.Drawing.Bitmap bitmap)
        {
            try
            {
                // Convert the bitmap to a SoftwareBitmap
                using (var enhancedBitmap = OptimizeImageForOcr(bitmap))
                {
                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        // Using BMP because it's faster and doesn't require compression/decompression
                        enhancedBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Bmp);
                        memoryStream.Position = 0;
                        
                        using (var randomAccessStream = memoryStream.AsRandomAccessStream())
                        {
                            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
                            return await decoder.GetSoftwareBitmapAsync(
                                BitmapPixelFormat.Bgra8,
                                BitmapAlphaMode.Premultiplied);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting bitmap to SoftwareBitmap: {ex.Message}");
                throw; // Rethrow to be handled by caller
            }
        }

        // Optimize the image for OCR by applying a sharpening filter and adjusting brightness and contrast
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

        // Get OCR engine for the specified language
        private OcrEngine GetOcrEngine(string languageCode)
        {
            // Convert language code to Windows language tag
            if (LanguageMap.TryGetValue(languageCode, out string? languageTag))
            {
                // Check if the language is available for OCR
                if (OcrEngine.IsLanguageSupported(new Language(languageTag)))
                {
                    return OcrEngine.TryCreateFromLanguage(new Language(languageTag));
                }
                else
                {
                    Console.WriteLine($"Language {languageTag} not supported for Windows OCR, using user profile languages");
                    return OcrEngine.TryCreateFromUserProfileLanguages();
                }
            }
            else
            {
                // Fallback to user profile languages
                Console.WriteLine($"No mapping found for language {languageCode}, using user profile languages");
                return OcrEngine.TryCreateFromUserProfileLanguages();
            }
        }
        
      
        
        // Get OCR lines directly from a bitmap
        public async Task<List<Windows.Media.Ocr.OcrLine>> GetOcrLinesFromBitmapAsync(System.Drawing.Bitmap bitmap, string languageCode = "en")
        {
            try
            {
                // Convert the bitmap to a SoftwareBitmap
                SoftwareBitmap softwareBitmap = await ConvertBitmapToSoftwareBitmapAsync(bitmap);
                
                // Get the OCR engine
                OcrEngine ocrEngine = GetOcrEngine(languageCode);
                
                // Perform OCR
                var result = await ocrEngine.RecognizeAsync(softwareBitmap);
                
                // Return the lines directly
                return result.Lines.ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Windows OCR error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<Windows.Media.Ocr.OcrLine>();
            }
        }

        // Process Windows OCR results
        public Task ProcessWindowsOcrResults(List<Windows.Media.Ocr.OcrLine> textLines, string languageCode = "en")
        {
            try
            {
                // Create a JSON response similar to what EasyOCR would return, but at character level
                var results = new List<object>();
                
                // Set to true to enable character-level processing for both OCR engines
                bool useCharacterLevel = true;
                
                foreach (var line in textLines)
                {
                    // Skip empty lines
                    if (line.Words.Count == 0 || string.IsNullOrWhiteSpace(line.Text))
                    {
                        continue;
                    }

                    //Console.WriteLine($"Processing line: \"{line.Text}\" with {line.Words.Count} words");
                    
                    if (useCharacterLevel)
                    {
                        // Process at character level by splitting words into characters
                        foreach (var word in line.Words)
                        {
                            var wordRect = word.BoundingRect;
                            string wordText = word.Text;
                            
                            // Skip empty words
                            if (string.IsNullOrWhiteSpace(wordText))
                                continue;
                            
                            // For English text, add a special space marker *after* the word to help with spacing
                            // (unless it's the last word in the line)
                            bool addSpaceMarker = false;
                            
                            // Check if we should add a space marker after this word
                            // For Western languages like English, spaces between words are important
                            if (languageCode == "en" && word != line.Words.Last())
                            {
                                addSpaceMarker = true;
                            }
                                
                            // Calculate the width of each character in the word
                            double totalWidth = wordRect.Width;
                            double charWidth = totalWidth / wordText.Length;
                            
                    
                            double charPadding = charWidth * 0.15; //15% of character width
                            double effectiveCharWidth = charWidth - charPadding;

                            // Process each character in the word
                            for (int i = 0; i < wordText.Length; i++)
                            {
                                string charText = wordText[i].ToString();
                                
                                // Calculate the X-coordinate of the character
                                double charX = wordRect.X + (i * charWidth) + (charPadding / 2);
                                
                                // Create bounding rectangle for this character
                                var charRect = new Windows.Foundation.Rect(
                                    charX, 
                                    wordRect.Y, 
                                    effectiveCharWidth,
                                    wordRect.Height
                                );
                                
                                // Calculate box coordinates (polygon points) for the character
                                var charBox = new[] {
                                    new[] { (double)charRect.X, (double)charRect.Y },
                                    new[] { (double)(charRect.X + charRect.Width), (double)charRect.Y },
                                    new[] { (double)(charRect.X + charRect.Width), (double)(charRect.Y + charRect.Height) },
                                    new[] { (double)charRect.X, (double)(charRect.Y + charRect.Height) }
                                };
                                
                                // Add the character to the results
                                results.Add(new
                                {
                                    text = charText,
                                    confidence = 0.9, // Windows OCR doesn't provide confidence
                                    rect = charBox,
                                    is_character = true
                                });
                            }
                                
                            
                            // Add space marker after word if needed (for English/Western languages)
                            if (addSpaceMarker)
                            {
                                // Create space character after the word
                                // Position it just to the right of the last character
                                double spaceWidth = charWidth * 0.6; 
                                double spaceX = wordRect.X + wordRect.Width + (charWidth * 0.1);
                                
                                var spaceRect = new Windows.Foundation.Rect(
                                    spaceX,
                                    wordRect.Y,
                                    spaceWidth,
                                    wordRect.Height
                                );
                                
                                // Calculate box coordinates for the space character
                                var spaceBox = new[] {
                                    new[] { (double)spaceRect.X, (double)spaceRect.Y },
                                    new[] { (double)(spaceRect.X + spaceRect.Width), (double)spaceRect.Y },
                                    new[] { (double)(spaceRect.X + spaceRect.Width), (double)(spaceRect.Y + spaceRect.Height) },
                                    new[] { (double)spaceRect.X, (double)(spaceRect.Y + spaceRect.Height) }
                                };
                                
                                // Add the space character to results
                                results.Add(new
                                {
                                    text = " ", // Actual space character
                                    confidence = 0.95, // High confidence for this artificially added space
                                    rect = spaceBox,
                                    is_character = true
                                });
                            }
                        }
                    }
                    else
                    {
                        // Original line-based processing (as a fallback)
                        
                        // Get bounding box coordinates - use the words to build a bounding rectangle
                        var rectBox = line.Words[0].BoundingRect; // Start with first word
    
                        // Find the complete bounding box for the line (all words)
                        foreach (var word in line.Words)
                        {
                            var wordRect = word.BoundingRect;
                            // Expand the rectangle to include this word
                            rectBox.X = Math.Min(rectBox.X, wordRect.X);
                            rectBox.Y = Math.Min(rectBox.Y, wordRect.Y);
                            rectBox.Width = Math.Max(rectBox.Width, wordRect.X + wordRect.Width - rectBox.X);
                            rectBox.Height = Math.Max(rectBox.Height, wordRect.Y + wordRect.Height - rectBox.Y);
                        }
    
                        // Calculate box coordinates (polygon points)
                        var box = new[] {
                            new[] { (double)rectBox.X, (double)rectBox.Y },
                            new[] { (double)(rectBox.X + rectBox.Width), (double)rectBox.Y },
                            new[] { (double)(rectBox.X + rectBox.Width), (double)(rectBox.Y + rectBox.Height) },
                            new[] { (double)rectBox.X, (double)(rectBox.Y + rectBox.Height) }
                        };
    
                        // Process text for Japanese characters (remove extra spaces)
                        string processedText = line.Text;
                        
                        
                        /*
                        // If using Japanese language, remove spaces between Japanese characters
                        if (languageCode == "ja")
                        {
                            // Remove spaces between Japanese characters
                            // Apply the regex multiple times to catch all instances
                            for (int i = 0; i < 10; i++)  // Apply multiple passes to catch all instances
                            {
                                string before = processedText;
                                // Remove spaces between Japanese characters with improved regex
                                processedText = System.Text.RegularExpressions.Regex.Replace(
                                    processedText, 
                                    @"([\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}])\s+([\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}])", 
                                    "$1$2");
                                
                                // If no more changes, break the loop
                                if (before == processedText)
                                    break;
                            }
                        }

                        */
                        
                        // Add the text line to results
                        results.Add(new
                        {
                            text = processedText,
                            confidence = 0.9, // Windows OCR doesn't provide confidence
                            rect = box
                        });
                    }
                }

                // If no text blocks were found, try line-by-line approach with simple layout
                if (results.Count == 0)
                {
                    //Console.WriteLine("No lines found with Windows OCR, creating a simple layout");
                }

                // Create a JSON response
                var response = new
                {
                    status = "success",
                    results = results,
                    processing_time_seconds = 0.1,
                    char_level = useCharacterLevel // Indicate this is character-level data
                };

                // Convert to JSON
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string jsonResponse = JsonSerializer.Serialize(response, jsonOptions);

                //Console.WriteLine($"Generated Windows OCR JSON response with {results.Count} results");

                // Process the JSON response on the UI thread to handle STA requirements
                Application.Current.Dispatcher.Invoke((Action)(() => {
                    Logic.Instance.ProcessReceivedTextJsonData(jsonResponse);
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing Windows OCR results: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            // Return a completed task since this method doesn't use await
            return Task.CompletedTask;
        }

    }
}