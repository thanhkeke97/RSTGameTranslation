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
        public static partial long GetOcrWordConfidence(long word, out float confidence);

        // Frees what RunOcrPipeline allocated; every line/word handle of that result is invalid afterwards
        [LibraryImport("oneocr.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ReleaseOcrResult(long instance);

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
                PreloadAppOnnxRuntime();

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

        // oneocr.dll needs ONNX Runtime API 23+ (the onnxruntime.dll shipped next to rst.exe). Recent
        // Windows builds also have an older onnxruntime.dll (1.17) in System32; if that one is in the
        // process first, oneocr.dll binds to it and CreateOcrPipeline fails with error 5. Loading ours
        // by full path first makes oneocr.dll's import resolve to it.
        private static void PreloadAppOnnxRuntime()
        {
            string ortPath = Path.Combine(AppContext.BaseDirectory, "onnxruntime.dll");
            if (!File.Exists(ortPath))
                return;

            if (!NativeLibrary.TryLoad(ortPath, out _))
            {
                Console.WriteLine($"OneOCR: could not load {ortPath}");
            }
        }

        // When pipeline creation fails, wait this long before trying again instead of retrying every frame
        private static readonly TimeSpan PipelineRetryInterval = TimeSpan.FromSeconds(10);
        private DateTime _lastPipelineAttempt = DateTime.MinValue;

        private void InitializePipeline()
        {
            if (DateTime.UtcNow - _lastPipelineAttempt < PipelineRetryInterval)
                return;
            _lastPipelineAttempt = DateTime.UtcNow;

            // The model path is relative: oneocr.dll does not take every non-ASCII absolute path, so it
            // is opened from the app folder. The working directory is not always that folder (shortcut
            // with another "Start in", launched from another app, a file dialog changed it), so switch
            // to it for the call.
            string appDir = AppContext.BaseDirectory;
            string previousDir = Environment.CurrentDirectory;
            bool switchDir = !string.Equals(Path.GetFullPath(previousDir).TrimEnd('\\'),
                                            Path.GetFullPath(appDir).TrimEnd('\\'),
                                            StringComparison.OrdinalIgnoreCase);
            try
            {
                string key = "kj)TGtrK>f]b[Piow.gU+nC@s\"\"\"\"\"\"4";
                string modelPath = "oneocr.onemodel";

                if (!File.Exists(Path.Combine(appDir, modelPath)))
                {
                    Console.WriteLine($"OneOCR: model file not found: {Path.Combine(appDir, modelPath)}");
                    return;
                }

                if (switchDir) Environment.CurrentDirectory = appDir;
                long res = NativeMethods.CreateOcrPipeline(modelPath, key, Context, out _pipeline);
                if (res != 0)
                {
                    Console.WriteLine($"OneOCR: failed to create OCR pipeline. Error code: {res}. " +
                        "Check that oneocr.dll, oneocr.onemodel and onnxruntime.dll in the app folder come from the same release.");
                    return;
                }

                res = NativeMethods.CreateOcrProcessOptions(out _processOptions);
                if (res != 0)
                {
                    Console.WriteLine("OneOCR: failed to create OCR process options.");
                    return;
                }

                res = NativeMethods.OcrProcessOptionsSetMaxRecognitionLineCount(_processOptions, 1000);
                if (res != 0)
                {
                    Console.WriteLine("OneOCR: failed to set max recognition line count.");
                    return;
                }

                _pipelineInitialized = true;
                Console.WriteLine("OCR pipeline initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing OCR pipeline: {ex.Message}");
            }
            finally
            {
                if (switchDir)
                {
                    try { Environment.CurrentDirectory = previousDir; } catch { }
                }
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
                    return null;
                }
            }

            long res = NativeMethods.RunOcrPipeline(_pipeline, ref img, _processOptions, out long instance);
            if (res != 0)
            {
                Console.WriteLine("OneOCR: failed to run OCR pipeline. Error code: " + res);
                return null;
            }

            try
            {
                return ReadOcrResult(instance);
            }
            finally
            {
                // The result is native memory: without this every OCR pass leaked it (about 9 KB for two
                // lines of text, several passes a second)
                NativeMethods.ReleaseOcrResult(instance);
            }
        }

        private Line[]? ReadOcrResult(long instance)
        {
            // Get the number of recognized lines
            long res = NativeMethods.GetOcrLineCount(instance, out long lineCount);
            if (res != 0)
            {
                Console.WriteLine("Failed to get OCR line count.");
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
                        Console.WriteLine("Failed to get OCR word count.");
                        return null;
                    }
                    List<Word> words = new List<Word>();
                    List<float> wordConfidences = new List<float>();
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

                            if (NativeMethods.GetOcrWordConfidence(word, out float wordConfidence) == 0)
                            {
                                wordConfidences.Add(wordConfidence);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Failed to get bounding box.");
                        }
                    }
                    data.Words = words.ToArray();
                    if (wordConfidences.Count > 0)
                    {
                        data.Confidence = wordConfidences.Average();
                    }
                    lines.Add(data);
                }
                else
                {
                    Console.WriteLine("Failed to get bounding box.");
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
        public Task ProcessOneOcrResults(List<Line> textLines, string languageCode = "en", Bitmap? sourceBitmap = null)
        {
            try
            {
                // Create a JSON response similar to what EasyOCR would return
                var results = new List<object>();

                // In manga mode, tag each line with its speech bubble so block detection
                // doesn't merge text from bubbles that happen to sit close together
                var bubbleIds = new int?[textLines.Count];
                if (sourceBitmap != null && ConfigManager.Instance.IsMangaModeEnabled())
                {
                    try
                    {
                        var boxes = textLines.Select(l =>
                        {
                            float minX = Math.Min(Math.Min(l.X1, l.X2), Math.Min(l.X3, l.X4));
                            float minY = Math.Min(Math.Min(l.Y1, l.Y2), Math.Min(l.Y3, l.Y4));
                            float maxX = Math.Max(Math.Max(l.X1, l.X2), Math.Max(l.X3, l.X4));
                            float maxY = Math.Max(Math.Max(l.Y1, l.Y2), Math.Max(l.Y3, l.Y4));
                            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
                        }).ToList();
                        bubbleIds = SpeechBubbleDetector.AssignBubbleIds(sourceBitmap, boxes);
                        Console.WriteLine($"Speech bubbles: {bubbleIds.Count(id => id != null)}/{textLines.Count} lines inside {bubbleIds.Where(id => id != null).Distinct().Count()} bubbles");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Speech bubble detection failed: {ex.Message}");
                    }
                }

                for (int lineIndex = 0; lineIndex < textLines.Count; lineIndex++)
                {
                    var line = textLines[lineIndex];
                    // Skip empty lines
                    if (line.Words == null || line.Words.Length == 0 || string.IsNullOrWhiteSpace(line.Text))
                    {
                        continue;
                    }

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
                        confidence = line.Confidence,
                        rect = box,
                        bubble_id = bubbleIds[lineIndex]
                    });
                }

                // Create a JSON response
                var response = new
                {
                    status = "success",
                    results = results,
                    processing_time_seconds = 0.1
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

        // Process text based on language-specific rules
        private string ProcessTextByLanguage(string text, string languageCode)
        {
            string processedText = text;
            
            switch (languageCode)
            {
                case "ja": // Japanese
                case LanguageCatalog.Auto: // Only touches kana/kanji, so safe whatever the language
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
                    
                case "zh-CN": // Simplified Chinese
                case "zh-TW": // Traditional Chinese
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

            // Average of the words' confidences (0-1). Measured on a manga page: real dialogue 0.75-1.0,
            // stray sound-effect lettering 0.5-0.7, so this only separates them under a raised threshold.
            public double Confidence { get; set; } = 0.9;

            public override string ToString()
            {
                return $"{Text}: ({X1},{Y1}),({X2},{Y2}),({X3},{Y3}),({X4},{Y4})";
            }
        }
    }
}
