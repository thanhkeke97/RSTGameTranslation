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

        public string? _currentLanguageCode = null;


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

        public bool CheckLanguagePackInstall(string languageCode)
        {
            var availableLanguages = OcrEngine.AvailableRecognizerLanguages;
            foreach (var language in availableLanguages)
            {
                Console.WriteLine($"------------------------------------------{language.LanguageTag}");
                if (LanguageMap.TryGetValue(languageCode, out string? languageTag))
                {
                    _currentLanguageCode = languageTag;
                    if (language.LanguageTag == languageTag || language.LanguageTag == languageCode)
                    {
                        Console.WriteLine($"Language pack is installed for {languageCode}");
                        return true;
                    }
                }
                else
                {
                    _currentLanguageCode = null;
                    return false;
                }
                
            }
            Console.WriteLine($"Language pack is not installed for {languageCode}");
            return false;
        }

        // Map of language codes to Windows language tags
        private readonly Dictionary<string, string> LanguageMap = new Dictionary<string, string>
        {
            { "en", "en-US" },
            { "ch_sim", "zh-Hans-CN" },
            { "es", "es-ES" },
            { "fr", "fr-FR" },
            { "it", "it-IT" },
            { "de", "de-DE" },
            { "ru", "ru-RU" },
            { "ar", "ar-SA" },
            { "pt", "pt-BR" },
            { "nl", "nl-NL" },
            { "ja", "ja-JP" },
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
                
                // Minimum confidence threshold for including results
                double confidenceThreshold = 0.7; // Adjust this value based on your needs
                
                foreach (var line in textLines)
                {
                    // Skip empty lines
                    if (line.Words.Count == 0 || string.IsNullOrWhiteSpace(line.Text))
                    {
                        continue;
                    }

                    // Get the line confidence if available
                    double lineConfidence = 0.9; // Default confidence
                    
                    if (useCharacterLevel)
                    {
                        // Track the last character's right edge to prevent overlapping
                        double lastCharRightEdge = 0;
                        
                        // Process at character level by splitting words into characters
                        foreach (var word in line.Words)
                        {
                            var wordRect = word.BoundingRect;
                            string wordText = word.Text;
                            
                            // Skip empty words
                            if (string.IsNullOrWhiteSpace(wordText))
                                continue;
                            
                            // Get word confidence if available
                            double wordConfidence = CalculateWordConfidence(word, lineConfidence);
                            
                            // Skip words with confidence below threshold
                            if (wordConfidence < confidenceThreshold)
                                continue;
                            
                            // Determine if we should add a space marker after this word
                            bool addSpaceMarker = false;
                            
                            // Check if we should add a space marker after this word based on language
                            // For Western languages and some others, spaces between words are important
                            if (ShouldAddSpaceBetweenWords(languageCode) && word != line.Words.Last())
                            {
                                addSpaceMarker = true;
                            }
                            
                            // Ensure this word doesn't overlap with the previous one
                            if (wordRect.X < lastCharRightEdge)
                            {
                                // Adjust the word's X position to avoid overlap
                                double adjustment = lastCharRightEdge - wordRect.X + 1; // +1 for a small gap
                                wordRect = new Windows.Foundation.Rect(
                                    lastCharRightEdge + 1,
                                    wordRect.Y,
                                    Math.Max(1, wordRect.Width - adjustment), // Ensure width is at least 1
                                    wordRect.Height
                                );
                            }
                            
                            // If the word is too narrow after adjustment, skip it
                            if (wordRect.Width < 2)
                                continue;
                                
                            // Calculate the width of each character in the word
                            double totalWidth = wordRect.Width;
                            double charWidth = totalWidth / wordText.Length;
                            
                            // Ensure minimum character width
                            charWidth = Math.Max(charWidth, 2.0);
                            
                            double charPadding = Math.Min(charWidth * 0.1, 1.0); // Reduced padding to 10% with a maximum of 1 pixel
                            double effectiveCharWidth = charWidth - (charPadding * 2); // Padding on both sides
                            
                            // Process each character in the word
                            for (int i = 0; i < wordText.Length; i++)
                            {
                                string charText = wordText[i].ToString();
                                
                                // Calculate character confidence based on position and context
                                double charConfidence = CalculateCharacterConfidence(charText, i, wordText, wordConfidence);
                                
                                // Skip characters with confidence below threshold
                                if (charConfidence < confidenceThreshold)
                                    continue;
                                
                                // Calculate the X-coordinate of the character, ensuring no overlap
                                double charX = Math.Max(
                                    wordRect.X + (i * charWidth),
                                    lastCharRightEdge + 1 // Ensure at least 1 pixel gap
                                );
                                
                                // Create bounding rectangle for this character
                                var charRect = new Windows.Foundation.Rect(
                                    charX, 
                                    wordRect.Y, 
                                    effectiveCharWidth,
                                    wordRect.Height
                                );
                                
                                // Update the last character's right edge
                                lastCharRightEdge = charRect.X + charRect.Width;
                                
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
                                    confidence = charConfidence,
                                    rect = charBox,
                                    is_character = true
                                });
                            }
                                
                            // Add space marker after word if needed
                            if (addSpaceMarker)
                            {
                                // Create space character after the word
                                // Position it just to the right of the last character with no overlap
                                double spaceWidth = Math.Max(charWidth * 0.5, 2.0); // At least 2 pixels wide
                                double spaceX = lastCharRightEdge + 1; // 1 pixel gap
                                
                                // Space confidence is slightly higher than the word confidence
                                double spaceConfidence = Math.Min(0.95, wordConfidence + 0.05);
                                
                                var spaceRect = new Windows.Foundation.Rect(
                                    spaceX,
                                    wordRect.Y,
                                    spaceWidth,
                                    wordRect.Height
                                );
                                
                                // Update the last character's right edge
                                lastCharRightEdge = spaceRect.X + spaceRect.Width;
                                
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
                                    confidence = spaceConfidence,
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

                        // Process text based on language
                        string processedText = ProcessTextByLanguage(line.Text, languageCode);
                        
                        // Calculate line confidence based on word confidences
                        double lineConfidenceCalculated = CalculateLineConfidence(line);
                        
                        // Skip lines with confidence below threshold
                        if (lineConfidenceCalculated < confidenceThreshold)
                            continue;
                        
                        // Add the text line to results
                        results.Add(new
                        {
                            text = processedText,
                            confidence = lineConfidenceCalculated,
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

        // Calculate confidence for a word based on its characteristics
        private double CalculateWordConfidence(Windows.Media.Ocr.OcrWord word, double lineConfidence)
        {
            // Windows OCR doesn't provide word-level confidence, so we need to estimate it
            double baseConfidence = 0.85; // Start with a reasonable base confidence
            
            // Adjust confidence based on word length (very short words might be less reliable)
            if (word.Text.Length < 2)
                baseConfidence *= 0.9;
            
            // Adjust confidence based on word height/width ratio (extremely narrow or wide words might be less reliable)
            double ratio = word.BoundingRect.Width / word.BoundingRect.Height;
            if (ratio < 0.2 || ratio > 10)
                baseConfidence *= 0.85;
            
            // Adjust confidence based on special characters or numbers
            if (word.Text.Any(c => !char.IsLetterOrDigit(c)))
                baseConfidence *= 0.95;
            
            // Consider line confidence as a factor
            return Math.Min(0.98, baseConfidence * (0.7 + 0.3 * lineConfidence));
        }

        // Calculate confidence for a character based on its position and context
        private double CalculateCharacterConfidence(string charText, int position, string wordText, double wordConfidence)
        {
            double charConfidence = wordConfidence; // Start with word confidence
            
            // Adjust confidence for special characters
            if (!char.IsLetterOrDigit(charText[0]))
                charConfidence *= 0.95;
            
            // Characters at word boundaries might be less reliable
            if (position == 0 || position == wordText.Length - 1)
                charConfidence *= 0.98;
            
            // Uncommon character combinations might indicate OCR errors
            if (position > 0)
            {
                char prevChar = wordText[position - 1];
                char currentChar = charText[0];
                
                // Check for unusual character combinations (customize this for your specific use case)
                bool isUnusualCombination = false;
                
                // Example: mixing different scripts
                bool isPrevLatinOrDigit = (char.IsLetter(prevChar) && IsLatinCharacter(prevChar)) || char.IsDigit(prevChar);
                bool isCurrentLatinOrDigit = (char.IsLetter(currentChar) && IsLatinCharacter(currentChar)) || char.IsDigit(currentChar);
                
                if (isPrevLatinOrDigit != isCurrentLatinOrDigit)
                    isUnusualCombination = true;
                
                if (isUnusualCombination)
                    charConfidence *= 0.9;
            }
            
            return Math.Min(0.98, charConfidence);
        }

        // Helper method to check if a character is from the Latin script
        private bool IsLatinCharacter(char c)
        {
            // Basic Latin characters
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                // Latin-1 Supplement
                (c >= '\u00C0' && c <= '\u00FF');
        }

        // Calculate confidence for a line based on its words
        private double CalculateLineConfidence(Windows.Media.Ocr.OcrLine line)
        {
            // If the line has no words, return a low confidence
            if (line.Words.Count == 0)
                return 0.5;
            
            // Calculate average confidence of words in the line
            double totalConfidence = 0;
            foreach (var word in line.Words)
            {
                totalConfidence += CalculateWordConfidence(word, 0.9);
            }
            
            return totalConfidence / line.Words.Count;
        }

        // Determine if spaces should be added between words for the given language
        private bool ShouldAddSpaceBetweenWords(string languageCode)
        {
            // Languages that use spaces between words
            var languagesWithSpaces = new HashSet<string> 
            { 
                "en", // English
                "es", // Spanish
                "fr", // French
                "it", // Italian
                "de", // German
                "ru", // Russian
                "ar", // Arabic
                "pt", // Portuguese
                "nl", // Dutch
                "ko"  // Korean (modern Korean uses spaces between words)
            };
            
            // Languages that don't typically use spaces between words
            var languagesWithoutSpaces = new HashSet<string>
            {
                "ja",     // Japanese
                "ch_sim", // Simplified Chinese
                // "ch_tra"  // Traditional Chinese
            };
            
            // Default to adding spaces if language is not specifically handled
            if (languagesWithoutSpaces.Contains(languageCode))
                return false;
            
            return true; // Default to adding spaces
        }

        // Process text based on language-specific rules
        private string ProcessTextByLanguage(string text, string languageCode)
        {
            string processedText = text;
            
            switch (languageCode)
            {
                case "ja": // Japanese
                    // Remove spaces between Japanese characters
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
                    break;
                    
                // case "ch_sim": // Simplified Chinese
                // case "ch_tra": // Traditional Chinese
                //     // Remove spaces between Chinese characters
                //     for (int i = 0; i < 10; i++)
                //     {
                //         string before = processedText;
                //         processedText = System.Text.RegularExpressions.Regex.Replace(
                //             processedText, 
                //             @"([\p{IsCJKUnifiedIdeographs}])\s+([\p{IsCJKUnifiedIdeographs}])", 
                //             "$1$2");
                        
                //         if (before == processedText)
                //             break;
                //     }
                //     break;
                    
                // case "th": // Thai
                //     // Thai doesn't use spaces between words, but may have spaces for sentence breaks
                //     // Remove excess spaces but keep sentence breaks
                //     processedText = System.Text.RegularExpressions.Regex.Replace(
                //         processedText,
                //         @"([^\s])\s+([^\s])",
                //         "$1$2");
                //     break;
                    
                // case "ar": // Arabic
                // case "fa": // Persian/Farsi
                //     // For right-to-left languages, ensure proper spacing
                //     // This is a simplified approach; more complex handling might be needed
                //     processedText = System.Text.RegularExpressions.Regex.Replace(
                //         processedText,
                //         @"\s{2,}", // Replace multiple spaces with a single space
                //         " ");
                //     break;
                    
                default:
                    // For other languages, keep the text as is
                    break;
            }
            
            return processedText;
        }

    }
}