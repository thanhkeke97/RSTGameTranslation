using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Globalization;
using System.IO;
using System.Text.Json;

using Application = System.Windows.Application;

namespace RSTGameTranslation
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
                        // Vẫn giữ BMP vì nó nhanh hơn
                        enhancedBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Bmp);
                        memoryStream.Position = 0;
                        
                        using (var randomAccessStream = memoryStream.AsRandomAccessStream())
                        {
                            // Tối ưu hóa quá trình giải mã
                            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
                            
                            // Sử dụng GetSoftwareBitmapAsync với các tham số tối ưu
                            return await decoder.GetSoftwareBitmapAsync(
                                BitmapPixelFormat.Bgra8,  // Định dạng pixel phổ biến và hiệu quả
                                BitmapAlphaMode.Premultiplied  // Chế độ alpha phổ biến
                            );
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
                // Kiểm tra nhanh xem có thể là văn bản trắng trên nền sáng không
                bool isLightTextOnBrightBg = QuickCheckForLightTextOnBrightBackground(source);
                
                // 2. Upscale ảnh nhỏ nếu cần (tương tự EasyOCR)
                if (source.Width < 800 || source.Height < 600)
                {
                    source = UpscaleImage(source, 800, 600);
                }
                
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
                        float contrast, brightness, gamma;
                        
                        if (isLightTextOnBrightBg)
                        {
                            // Thiết lập cho văn bản trắng trên nền sáng
                            // Đảo ngược màu và tăng độ tương phản (tương tự EasyOCR)
                            contrast = -2.0f;  // Tăng độ tương phản như EasyOCR (2.0)
                            brightness = 1.0f; 
                            gamma = 0.7f;
                        }
                        else
                        {
                            // Thiết lập mặc định cho văn bản tối trên nền sáng
                            contrast = 2.0f;   // Tăng lên 2.0 như EasyOCR
                            brightness = 0.05f;
                            gamma = 1.3f;
                        }
                        
                        // Create a color matrix to adjust brightness and contrast
                        float[][] colorMatrix = {
                            new float[] {contrast, 0, 0, 0, 0},
                            new float[] {0, contrast, 0, 0, 0},
                            new float[] {0, 0, contrast, 0, 0},
                            new float[] {0, 0, 0, 1, 0},
                            new float[] {brightness, brightness, brightness, 0, 1}
                        };
                        
                        attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(colorMatrix));
                        
                        // Adjust gamma
                        attributes.SetGamma(gamma);
                        
                        // Apply a sharpening filter to the image
                        float sharpenAmount = isLightTextOnBrightBg ? 0.7f : 0.5f;
                        attributes.SetColorMatrix(
                            CreateSharpenMatrix(sharpenAmount), 
                            System.Drawing.Imaging.ColorMatrixFlag.Default, 
                            System.Drawing.Imaging.ColorAdjustType.Bitmap
                        );
                        
                        // Draw the source bitmap onto the result bitmap, applying the color matrix and gamma correction
                        graphics.DrawImage(
                            source,
                            new System.Drawing.Rectangle(0, 0, result.Width, result.Height),
                            0, 0, source.Width, source.Height,
                            System.Drawing.GraphicsUnit.Pixel,
                            attributes
                        );
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

        // Phương thức upscale ảnh (tương tự EasyOCR)
        private System.Drawing.Bitmap UpscaleImage(System.Drawing.Bitmap source, int minWidth, int minHeight)
        {
            // Kiểm tra xem có cần upscale không
            if (source.Width >= minWidth && source.Height >= minHeight)
            {
                return source;
            }
            
            // Tính toán tỷ lệ mới
            float scale = Math.Max((float)minWidth / source.Width, (float)minHeight / source.Height);
            int newWidth = (int)(source.Width * scale);
            int newHeight = (int)(source.Height * scale);
            
            Console.WriteLine($"Upscaling image from {source.Width}x{source.Height} to {newWidth}x{newHeight}");
            
            // Tạo bitmap mới với kích thước đã tăng
            var result = new System.Drawing.Bitmap(newWidth, newHeight);
            
            // Vẽ với chất lượng cao
            using (var graphics = System.Drawing.Graphics.FromImage(result))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, 0, 0, newWidth, newHeight);
            }
            
            return result;
        }

        // Kiểm tra nhanh xem có thể là văn bản trắng trên nền sáng không
        private bool QuickCheckForLightTextOnBrightBackground(System.Drawing.Bitmap source)
        {
            // Lấy mẫu ít điểm ảnh để kiểm tra nhanh
            int sampleSize = 20;
            int width = source.Width;
            int height = source.Height;
            
            int brightPixels = 0;
            int totalSamples = 0;
            
            // Lấy mẫu theo lưới để tăng tốc độ
            for (int y = 0; y < height; y += height / sampleSize)
            {
                for (int x = 0; x < width; x += width / sampleSize)
                {
                    System.Drawing.Color pixel = source.GetPixel(x, y);
                    int brightness = (pixel.R + pixel.G + pixel.B) / 3;
                    
                    if (brightness > 200) // Ngưỡng cho pixel sáng
                        brightPixels++;
                    
                    totalSamples++;
                }
            }
            
            // Nếu hơn 70% pixel là sáng, có thể là văn bản trắng trên nền sáng
            return (double)brightPixels / totalSamples > 0.7;
        }


        private System.Drawing.Imaging.ColorMatrix CreateSharpenMatrix(float amount)
        {
            // Ma trận làm sắc nét đơn giản và hiệu quả
            float[][] matrix = {
                new float[] {1, 0, 0, 0, 0},
                new float[] {0, 1, 0, 0, 0},
                new float[] {0, 0, 1, 0, 0},
                new float[] {0, 0, 0, 1, 0},
                new float[] {0, 0, 0, 0, 1}
            };
            
            // Điều chỉnh mức độ làm sắc nét
            matrix[0][0] += amount;
            matrix[1][1] += amount;
            matrix[2][2] += amount;
            
            return new System.Drawing.Imaging.ColorMatrix(matrix);
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
                
                // Sử dụng bộ nhớ đệm cho OCR engines để tránh tạo mới mỗi lần
                OcrEngine ocrEngine = GetOcrEngineFromCache(languageCode);
                
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

        private readonly Dictionary<string, OcrEngine> _ocrEngineCache = new Dictionary<string, OcrEngine>();

        private OcrEngine GetOcrEngineFromCache(string languageCode)
        {
            // Kiểm tra xem đã có engine trong bộ nhớ đệm chưa
            if (_ocrEngineCache.TryGetValue(languageCode, out OcrEngine? cachedEngine))
            {
                return cachedEngine;
            }
            
            // Nếu chưa có, tạo mới và lưu vào bộ nhớ đệm
            OcrEngine newEngine = GetOcrEngine(languageCode);
            _ocrEngineCache[languageCode] = newEngine;
            return newEngine;
        }

        // Process Windows OCR results
        // Process Windows OCR results
        public Task ProcessWindowsOcrResults(List<Windows.Media.Ocr.OcrLine> textLines, string languageCode = "en")
        {
            try
            {
                // Tạo danh sách với dung lượng ban đầu để tránh phân bổ lại bộ nhớ
                var results = new List<object>(textLines.Count * 10); // Ước tính mỗi dòng có khoảng 10 ký tự
                
                // Set to true to enable character-level processing for both OCR engines
                bool useCharacterLevel = true;
                
                foreach (var line in textLines)
                {
                    // Skip empty lines
                    if (line.Words.Count == 0 || string.IsNullOrWhiteSpace(line.Text))
                    {
                        continue;
                    }
                    
                    if (useCharacterLevel)
                    {
                        ProcessCharacterLevel(line, languageCode, results);
                    }
                    else
                    {
                        ProcessLineLevel(line, results);
                    }
                }

                // Create a JSON response
                var response = new
                {
                    status = "success",
                    results = results,
                    processing_time_seconds = 0.1,
                    char_level = useCharacterLevel
                };

                // Tối ưu hóa tuỳ chọn JSON
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = false, // Tắt định dạng để giảm kích thước
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                
                string jsonResponse = JsonSerializer.Serialize(response, jsonOptions);

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

        // Xử lý ở cấp độ ký tự
        private void ProcessCharacterLevel(Windows.Media.Ocr.OcrLine line, string languageCode, List<object> results)
        {
            foreach (var word in line.Words)
            {
                var wordRect = word.BoundingRect;
                string wordText = word.Text;
                
                // Skip empty words
                if (string.IsNullOrWhiteSpace(wordText))
                    continue;
                
                // For English text, add a special space marker *after* the word to help with spacing
                bool addSpaceMarker = languageCode == "en" && word != line.Words.Last();
                
                // Tính toán trước để tối ưu
                double totalWidth = wordRect.Width;
                double charWidth = totalWidth / wordText.Length;
                double charPadding = charWidth * 0.15;
                double effectiveCharWidth = charWidth - charPadding;

                // Process each character in the word
                for (int i = 0; i < wordText.Length; i++)
                {
                    double charX = wordRect.X + (i * charWidth) + (charPadding / 2);
                    
                    // Tạo hình chữ nhật giới hạn cho ký tự này
                    var charRect = new Windows.Foundation.Rect(
                        charX, 
                        wordRect.Y, 
                        effectiveCharWidth,
                        wordRect.Height
                    );
                    
                    // Tính toán tọa độ hộp (điểm đa giác) cho ký tự
                    var charBox = new[] {
                        new[] { (double)charRect.X, (double)charRect.Y },
                        new[] { (double)(charRect.X + charRect.Width), (double)charRect.Y },
                        new[] { (double)(charRect.X + charRect.Width), (double)(charRect.Y + charRect.Height) },
                        new[] { (double)charRect.X, (double)(charRect.Y + charRect.Height) }
                    };
                    
                    // Thêm ký tự vào kết quả
                    results.Add(new
                    {
                        text = wordText[i].ToString(),
                        confidence = 0.9,
                        rect = charBox,
                        is_character = true
                    });
                }
                
                // Thêm dấu cách sau từ nếu cần
                if (addSpaceMarker)
                {
                    double spaceWidth = charWidth * 0.6;
                    double spaceX = wordRect.X + wordRect.Width + (charWidth * 0.1);
                    
                    var spaceRect = new Windows.Foundation.Rect(
                        spaceX,
                        wordRect.Y,
                        spaceWidth,
                        wordRect.Height
                    );
                    
                    var spaceBox = new[] {
                        new[] { (double)spaceRect.X, (double)spaceRect.Y },
                        new[] { (double)(spaceRect.X + spaceRect.Width), (double)spaceRect.Y },
                        new[] { (double)(spaceRect.X + spaceRect.Width), (double)(spaceRect.Y + spaceRect.Height) },
                        new[] { (double)spaceRect.X, (double)(spaceRect.Y + spaceRect.Height) }
                    };
                    
                    results.Add(new
                    {
                        text = " ",
                        confidence = 0.95,
                        rect = spaceBox,
                        is_character = true
                    });
                }
            }
        }

        // Xử lý ở cấp độ dòng
        private void ProcessLineLevel(Windows.Media.Ocr.OcrLine line, List<object> results)
        {
            // Xử lý dựa trên dòng (mã hiện tại của bạn)
            var rectBox = line.Words[0].BoundingRect;
            
            foreach (var word in line.Words)
            {
                var wordRect = word.BoundingRect;
                rectBox.X = Math.Min(rectBox.X, wordRect.X);
                rectBox.Y = Math.Min(rectBox.Y, wordRect.Y);
                rectBox.Width = Math.Max(rectBox.Width, wordRect.X + wordRect.Width - rectBox.X);
                rectBox.Height = Math.Max(rectBox.Height, wordRect.Y + wordRect.Height - rectBox.Y);
            }
            
            var box = new[] {
                new[] { (double)rectBox.X, (double)rectBox.Y },
                new[] { (double)(rectBox.X + rectBox.Width), (double)rectBox.Y },
                new[] { (double)(rectBox.X + rectBox.Width), (double)(rectBox.Y + rectBox.Height) },
                new[] { (double)rectBox.X, (double)(rectBox.Y + rectBox.Height) }
            };
            
            results.Add(new
            {
                text = line.Text,
                confidence = 0.9,
                rect = box
            });
        }
    }
}