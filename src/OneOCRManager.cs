using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Linq;

using Application = System.Windows.Application;

namespace RSTGameTranslation
{
    [StructLayout(LayoutKind.Sequential)]
    struct Img
    {
        public int t;
        public int col;
        public int row;
        public int _unk;
        public long step;
        public IntPtr data_ptr;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct BoundingBox
    {
        public float x1;
        public float y1;
        public float x2;
        public float y2;
        public float x3;
        public float y3;
        public float x4;
        public float y4;
    }
    static partial class NativeMethods
    {
        [LibraryImport("oneocr.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial long CreateOcrInitOptions(out long ctx);

        [LibraryImport("oneocr.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial long GetOcrLineCount(long instance, out long count);

        [LibraryImport("oneocr.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial long GetOcrLine(long instance, long index, out long line);

        [LibraryImport("oneocr.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial long GetOcrLineContent(long line, out IntPtr content);

        [LibraryImport("oneocr.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial long GetOcrLineBoundingBox(long line, out IntPtr boundingBoxPtr);

        [LibraryImport("oneocr.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial long GetOcrLineWordCount(long instance, out long count);

        [LibraryImport("oneocr.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial long GetOcrWord(long instance, long index, out long line);

        [LibraryImport("oneocr.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial long GetOcrWordContent(long line, out IntPtr content);

        [LibraryImport("oneocr.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial long GetOcrWordBoundingBox(long line, out IntPtr boundingBoxPtr);

        [LibraryImport("oneocr.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial long OcrProcessOptionsSetMaxRecognitionLineCount(long opt, long count);

        [LibraryImport("oneocr.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial long RunOcrPipeline(long pipeline, ref Img img, long opt, out long instance);

        [LibraryImport("oneocr.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial long CreateOcrProcessOptions(out long opt);

        [LibraryImport("oneocr.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial long OcrInitOptionsSetUseModelDelayLoad(long ctx, byte flag);

        [LibraryImport("oneocr.dll", StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial long CreateOcrPipeline(string modelPath, string key, long ctx, out long pipeline);

    }

    public class OneOCRManager
    {
        private static readonly Lazy<OneOCRManager> _instance = new Lazy<OneOCRManager>(() => new OneOCRManager());
        private long Context { get; set; }
        private bool _initialized;
        private bool _initializing;
        
        private long _pipeline;
        private long _processOptions;
        private bool _pipelineInitialized;

        // Singleton instance
        public static OneOCRManager Instance => _instance.Value;

        // Constructor
        private OneOCRManager()
        {
            _pipelineInitialized = false;
        }


        public ValueTask InitializeAsync()
        {
            if (_initialized)
            {
                return ValueTask.CompletedTask;
            }

            if (_initializing)
            {
                return ValueTask.CompletedTask;
            }

            _initializing = true;
            try
            {
                long res = NativeMethods.CreateOcrInitOptions(out long ctx);
                if (res == 0)
                {
                    Context = ctx;
                    res = NativeMethods.OcrInitOptionsSetUseModelDelayLoad(ctx, 1);
                    if (res == 0)
                    {
                        _initialized = true;
                        
                        InitializePipeline();
                    }
                }
            }
            finally
            {
                _initializing = false;
            }
            
            return ValueTask.CompletedTask;
        }

        private void InitializePipeline()
        {
            try
            {
                string key = "kj)TGtrK>f]b[Piow.gU+nC@s\"\"\"\"\"\"4";
                string modelPath = "oneocr.onemodel";

                long res = NativeMethods.CreateOcrPipeline(modelPath, key, Context, out _pipeline);
                if (res != 0)
                {
                    Console.Error.WriteLine("Failed to create OCR pipeline. Error code: " + res);
                    return;
                }

                res = NativeMethods.CreateOcrProcessOptions(out _processOptions);
                if (res != 0)
                {
                    Console.Error.WriteLine("Failed to create OCR process options.");
                    return;
                }

                res = NativeMethods.OcrProcessOptionsSetMaxRecognitionLineCount(_processOptions, 1000);
                if (res != 0)
                {
                    Console.Error.WriteLine("Failed to set max recognition line count.");
                    return;
                }

                _pipelineInitialized = true;
                Console.WriteLine("OCR pipeline initialized successfully");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error initializing OCR pipeline: {ex.Message}");
            }
        }

        private string? PtrToStringUTF8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return null;

            // Get the length of the string (read until null terminator)
            int length = 0;
            while (Marshal.ReadByte(ptr, length) != 0)
            {
                length++;
            }

            // Create a byte array and copy data from the pointer
            byte[] buffer = new byte[length];
            Marshal.Copy(ptr, buffer, 0, length);

            // Decode with UTF-8 encoding
            return Encoding.UTF8.GetString(buffer);
        }

        private Line[]? RunOcr(Img img)
        {
            if (!_pipelineInitialized)
            {
                InitializePipeline();
                if (!_pipelineInitialized)
                {
                    Console.Error.WriteLine("Failed to initialize OCR pipeline");
                    return null;
                }
            }

            long res = NativeMethods.RunOcrPipeline(_pipeline, ref img, _processOptions, out long instance);
            if (res != 0)
            {
                Console.Error.WriteLine("Failed to run OCR pipeline. Error code: " + res);
                return null;
            }

            // Get the number of recognized lines
            res = NativeMethods.GetOcrLineCount(instance, out long lineCount);
            if (res != 0)
            {
                Console.Error.WriteLine("Failed to get OCR line count.");
                return null;
            }

            List<Line> lines = new List<Line>();

            // Get the content of each line
            for (long i = 0; i < lineCount; i++)
            {
                res = NativeMethods.GetOcrLine(instance, i, out long line);
                if (res != 0 || line == 0)
                {
                    continue;
                }

                res = NativeMethods.GetOcrLineContent(line, out IntPtr lineContentPtr);
                if (res != 0)
                {
                    continue;
                }

                string? lineContent = PtrToStringUTF8(lineContentPtr);

                // Get the pointer to the bounding box
                res = NativeMethods.GetOcrLineBoundingBox(line, out IntPtr boundingBoxPtr);
                if (res == 0)
                {
                    // Map the pointer to the structure
                    BoundingBox boundingBox = Marshal.PtrToStructure<BoundingBox>(boundingBoxPtr);

                    Line data = new Line
                    {
                        Text = lineContent,
                        X1 = boundingBox.x1,
                        Y1 = boundingBox.y1,
                        X2 = boundingBox.x2,
                        Y2 = boundingBox.y2,
                        X3 = boundingBox.x3,
                        Y3 = boundingBox.y3,
                        X4 = boundingBox.x4,
                        Y4 = boundingBox.y4
                    };

                    res = NativeMethods.GetOcrLineWordCount(line, out long wordCount);
                    if (res != 0)
                    {
                        Console.Error.WriteLine("Failed to get OCR word count.");
                        return null;
                    }
                    List<Word> words = new List<Word>();
                    for (long j = 0; j < wordCount; j++)
                    {
                        res = NativeMethods.GetOcrWord(line, j, out long word);
                        if (res != 0 || word == 0)
                        {
                            continue;
                        }

                        res = NativeMethods.GetOcrWordContent(word, out IntPtr wordContentPtr);
                        if (res != 0)
                        {
                            continue;
                        }

                        string? wordContent = PtrToStringUTF8(wordContentPtr);

                        // Get the pointer to the bounding box
                        res = NativeMethods.GetOcrWordBoundingBox(word, out IntPtr wordBoundingBoxPtr);
                        if (res == 0)
                        {
                            // Map the pointer to the structure
                            BoundingBox wordBoundingBox = Marshal.PtrToStructure<BoundingBox>(wordBoundingBoxPtr);
                            Word w = new Word
                            {
                                Text = wordContent,
                                X1 = wordBoundingBox.x1,
                                Y1 = wordBoundingBox.y1,
                                X2 = wordBoundingBox.x2,
                                Y2 = wordBoundingBox.y2,
                                X3 = wordBoundingBox.x3,
                                Y3 = wordBoundingBox.y3,
                                X4 = wordBoundingBox.x4,
                                Y4 = wordBoundingBox.y4
                            };
                            words.Add(w);
                        }
                        else
                        {
                            Console.Error.WriteLine("Failed to get bounding box.");
                        }
                    }
                    data.Words = words.ToArray();
                    lines.Add(data);
                }
                else
                {
                    Console.Error.WriteLine("Failed to get bounding box.");
                }
            }
            return lines.ToArray();
        }

        // Get OCR lines from bitmap
        public async Task<List<Line>> GetOcrLinesFromBitmapAsync(System.Drawing.Bitmap bitmap)
        {
            await InitializeAsync();
            
            if (!_initialized)
            {
                Console.WriteLine("OneOCR is not available");
                return new List<Line>();
            }

            try
            {
                // Convert the image format to BGRA with optional preprocessing
                bool enablePreprocessing = ConfigManager.Instance.IsHDRSupportEnabled();
                Bitmap sourceImage = enablePreprocessing ? PreprocessBitmapForOCR(bitmap) : bitmap;

                try
                {
                    using (Bitmap imgRgba = new Bitmap(sourceImage.Width, sourceImage.Height, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(imgRgba))
                        {
                            g.DrawImage(sourceImage, 0, 0);
                        }

                        int rows = imgRgba.Height;
                        int cols = imgRgba.Width;
                        int step = System.Drawing.Image.GetPixelFormatSize(imgRgba.PixelFormat) / 8 * cols;

                        // Get pixel data
                        BitmapData bitmapData = imgRgba.LockBits(
                            new Rectangle(0, 0, imgRgba.Width, imgRgba.Height), 
                            ImageLockMode.ReadOnly, 
                            imgRgba.PixelFormat);
                        
                        IntPtr dataPtr = bitmapData.Scan0;

                        // Create an instance of the Img structure
                        Img formattedImage = new Img
                        {
                            t = 3,
                            col = cols,
                            row = rows,
                            _unk = 0,
                            step = step,
                            data_ptr = dataPtr
                        };

                        // Execute OCR processing
                        Line[]? result = RunOcr(formattedImage);
                        imgRgba.UnlockBits(bitmapData);
                        return result != null ? result.ToList() : new List<Line>();
                    }
                }
                finally
                {
                    // Cleanup preprocessed image if it was created
                    if (enablePreprocessing && sourceImage != bitmap)
                    {
                        sourceImage?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OneOCR error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<Line>();
            }
        }

        // Preprocess bitmap for better OCR with HDR support
        private System.Drawing.Bitmap PreprocessBitmapForOCR(System.Drawing.Bitmap source)
        {
            try
            {
                var result = new System.Drawing.Bitmap(source.Width, source.Height);
                
                using (var graphics = System.Drawing.Graphics.FromImage(result))
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    
                    using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                    {
                        // Analyze brightness (simple sampling)
                        float avgBrightness = AnalyzeBrightness(source);
                        
                        // Adaptive contrast based on brightness
                        float contrast = avgBrightness < 55 || avgBrightness > 200 ? 1.5f : 1.2f;
                        float brightness = (128 - avgBrightness) / 255.0f * 0.15f;
                        
                        // Color matrix for enhancement
                        float[][] colorMatrix = {
                            new float[] {contrast, 0, 0, 0, 0},
                            new float[] {0, contrast, 0, 0, 0},
                            new float[] {0, 0, contrast, 0, 0},
                            new float[] {0, 0, 0, 1, 0},
                            new float[] {brightness, brightness, brightness, 0, 1}
                        };
                        
                        attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(colorMatrix));
                        attributes.SetGamma(1.1f);
                        
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
                Console.WriteLine($"Preprocessing failed: {ex.Message}");
                return new System.Drawing.Bitmap(source);
            }
        }

        // Fast brightness analysis using LockBits for better performance
        private float AnalyzeBrightness(System.Drawing.Bitmap bitmap)
        {
            try
            {
                int sampleSize = Math.Max(1, Math.Min(bitmap.Width, bitmap.Height) / 20);
                
                BitmapData? bitmapData = null;
                try
                {
                    bitmapData = bitmap.LockBits(
                        new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format24bppRgb);
                    
                    unsafe
                    {
                        int bytesPerPixel = Image.GetPixelFormatSize(bitmapData.PixelFormat) / 8;
                        int stride = bitmapData.Stride;
                        byte* ptr = (byte*)bitmapData.Scan0;
                        
                        float totalBrightness = 0;
                        int samples = 0;
                        
                        for (int y = 0; y < bitmap.Height; y += sampleSize)
                        {
                            for (int x = 0; x < bitmap.Width; x += sampleSize)
                            {
                                int pixelIndex = y * stride + x * bytesPerPixel;
                                float brightness = 0.299f * ptr[pixelIndex + 2] + 0.587f * ptr[pixelIndex + 1] + 0.114f * ptr[pixelIndex];
                                totalBrightness += brightness;
                                samples++;
                            }
                        }
                        
                        return samples > 0 ? totalBrightness / samples : 128f;
                    }
                }
                finally
                {
                    if (bitmapData != null)
                    {
                        bitmap?.UnlockBits(bitmapData);
                    }
                }
            }
            catch
            {
                return 128f;
            }
        }

        // Process OneOCR results
        public Task ProcessOneOcrResults(List<Line> textLines, string languageCode = "en")
        {
            try
            {
                // Create a JSON response similar to what EasyOCR would return
                var results = new List<object>();
                
                // Set to true to enable character-level processing
                bool useCharacterLevel = false;
                
                foreach (var line in textLines)
                {
                    // Skip empty lines
                    if (line.Words == null || line.Words.Length == 0 || string.IsNullOrWhiteSpace(line.Text))
                    {
                        continue;
                    }

                    if (useCharacterLevel)
                    {
                        // Process at character level
                        foreach (var word in line.Words)
                        {
                            var wordText = word.Text;
                            
                            // Skip empty words
                            if (string.IsNullOrWhiteSpace(wordText))
                                continue;
                            
                            // Determine if we should add a space marker after this word
                            bool addSpaceMarker = false;
                            
                            // Check if we should add a space marker after this word based on language
                            if (ShouldAddSpaceBetweenWords(languageCode) && word != line.Words.Last())
                            {
                                addSpaceMarker = true;
                            }
                                
                            // Calculate the width of each character in the word
                            double totalWidth = Math.Max(
                                Math.Abs(word.X2 - word.X1), 
                                Math.Abs(word.X4 - word.X3)
                            );
                            double charWidth = totalWidth / wordText.Length;
                            
                            double charPadding = charWidth * 0.15; // 15% of character width
                            double effectiveCharWidth = charWidth - charPadding;

                            // Calculate base coordinates
                            double baseX = Math.Min(word.X1, word.X4);
                            double baseY = Math.Min(word.Y1, word.Y2);
                            double height = Math.Max(
                                Math.Abs(word.Y3 - word.Y1),
                                Math.Abs(word.Y4 - word.Y2)
                            );

                            // Process each character in the word
                            for (int i = 0; i < wordText.Length; i++)
                            {
                                string charText = wordText[i].ToString();
                                
                                // Calculate the X-coordinate of the character
                                double charX = baseX + (i * charWidth) + (charPadding / 2);
                                
                                // Calculate box coordinates (polygon points) for the character
                                var charBox = new[] {
                                    new[] { charX, baseY },
                                    new[] { charX + effectiveCharWidth, baseY },
                                    new[] { charX + effectiveCharWidth, baseY + height },
                                    new[] { charX, baseY + height }
                                };
                                
                                // Add the character to the results
                                results.Add(new
                                {
                                    text = charText,
                                    confidence = 0.9, // OneOCR doesn't provide confidence
                                    rect = charBox,
                                    is_character = true
                                });
                            }
                            
                            // Add space marker after word if needed
                            if (addSpaceMarker)
                            {
                                // Create space character after the word
                                double spaceWidth = charWidth * 0.6; 
                                double spaceX = baseX + totalWidth + (charWidth * 0.1);
                                
                                // Calculate box coordinates for the space character
                                var spaceBox = new[] {
                                    new[] { spaceX, baseY },
                                    new[] { spaceX + spaceWidth, baseY },
                                    new[] { spaceX + spaceWidth, baseY + height },
                                    new[] { spaceX, baseY + height }
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
                        // Line-based processing (as a fallback)
                        // Calculate box coordinates (polygon points)
                        var box = new[] {
                            new[] { (double)line.X1, (double)line.Y1 },
                            new[] { (double)line.X2, (double)line.Y2 },
                            new[] { (double)line.X3, (double)line.Y3 },
                            new[] { (double)line.X4, (double)line.Y4 }
                        };

                        // Process text based on language
                        string processedText = ProcessTextByLanguage(line.Text, languageCode);
                        
                        // Add the text line to results
                        results.Add(new
                        {
                            text = processedText,
                            confidence = 0.9, // OneOCR doesn't provide confidence
                            rect = box
                        });
                    }
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

                // Process the JSON response on the UI thread to handle STA requirements
                Application.Current.Dispatcher.Invoke((Action)(() => {
                    Logic.Instance.ProcessReceivedTextJsonData(jsonResponse);
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing OneOCR results: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            // Return a completed task since this method doesn't use await
            return Task.CompletedTask;
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
                    
                case "ch_sim": // Simplified Chinese
                    // Remove spaces between Chinese characters
                    for (int i = 0; i < 10; i++)
                    {
                        string before = processedText;
                        processedText = System.Text.RegularExpressions.Regex.Replace(
                            processedText, 
                            @"([\p{IsCJKUnifiedIdeographs}])\s+([\p{IsCJKUnifiedIdeographs}])", 
                            "$1$2");
                        
                        if (before == processedText)
                            break;
                    }
                    break;
                    
                default:
                    // For other languages, keep the text as is
                    break;
            }
            
            return processedText;
        }

        public class Word
        {
            public string? Text { get; set; }
            public float X1 { get; set; }
            public float Y1 { get; set; }
            public float X2 { get; set; }
            public float Y2 { get; set; }
            public float X3 { get; set; }
            public float Y3 { get; set; }
            public float X4 { get; set; }
            public float Y4 { get; set; }
        }

        public class Line
        {
            public string? Text { get; set; }
            public float X1 { get; set; }
            public float Y1 { get; set; }
            public float X2 { get; set; }
            public float Y2 { get; set; }
            public float X3 { get; set; }
            public float Y3 { get; set; }
            public float X4 { get; set; }
            public float Y4 { get; set; }

            public Word[]? Words { get; set; }

            public override string ToString()
            {
                return $"{Text}: ({X1},{Y1}),({X2},{Y2}),({X3},{Y3}),({X4},{Y4})";
            }
        }
    }
}
