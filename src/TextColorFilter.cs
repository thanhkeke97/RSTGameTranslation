using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace RSTGameTranslation
{
    // Keeps only the pixels whose color is close to the subtitle text color, drawn as dark text on a
    // white background, before the frame goes to OCR. A faint (semi-transparent) watermark under the
    // subtitles is far from the text color, so it disappears instead of being read into the text.
    internal static class TextColorFilter
    {
        // Pixels a little beyond the tolerance fade to white instead of cutting off, so letter edges
        // stay anti-aliased: OCR reads smooth edges better than jagged ones.
        private const int SoftEdge = 24;

        // Set by the settings window; the next filtered frame is saved here and opened.
        private static string? _pendingPreviewPath;

        public static void RequestPreview(string path) => _pendingPreviewPath = path;

        // Returns a new filtered bitmap, or null when the filter is off or fails.
        public static Bitmap? ApplyIfEnabled(Bitmap source)
        {
            var config = ConfigManager.Instance;
            if (!config.IsTextColorFilterEnabled())
                return null;

            Bitmap? result = Apply(source, config.GetTextColorFilterColor(), config.GetTextColorFilterTolerance());
            if (result != null)
                SavePreviewIfRequested(result);
            return result;
        }

        // Pixel buffer reused between frames (frames are filtered one at a time on the capture thread)
        private static byte[] _buffer = Array.Empty<byte>();
        private static readonly object _bufferLock = new object();

        public static Bitmap? Apply(Bitmap source, Color textColor, int tolerance)
        {
            int width = source.Width;
            int height = source.Height;
            if (width < 1 || height < 1)
                return null;

            var rect = new Rectangle(0, 0, width, height);
            // Read the frame in its own 32-bit layout when it has one: asking GDI+ for another
            // format makes it convert the whole frame first
            PixelFormat format = source.PixelFormat is PixelFormat.Format32bppArgb or PixelFormat.Format32bppRgb or PixelFormat.Format32bppPArgb
                ? source.PixelFormat
                : PixelFormat.Format32bppArgb;

            Bitmap? result = null;
            try
            {
                lock (_bufferLock)
                {
                    BitmapData srcData = source.LockBits(rect, ImageLockMode.ReadOnly, format);
                    int stride = srcData.Stride;
                    int size;
                    try
                    {
                        if (stride != width * 4)
                            return null; // bottom-up or padded layout: not produced by the capture paths
                        size = stride * height;
                        if (_buffer.Length < size)
                            _buffer = new byte[size];
                        Marshal.Copy(srcData.Scan0, _buffer, 0, size);
                    }
                    finally
                    {
                        source.UnlockBits(srcData);
                    }

                    // Lookup tables: per-channel distance to the text color, and distance (0..255) to gray level
                    int[] distB = new int[256], distG = new int[256], distR = new int[256];
                    byte[] levels = new byte[256];
                    for (int v = 0; v < 256; v++)
                    {
                        distB[v] = Math.Abs(v - textColor.B);
                        distG[v] = Math.Abs(v - textColor.G);
                        distR[v] = Math.Abs(v - textColor.R);
                        levels[v] = v <= tolerance ? (byte)0
                                  : v >= tolerance + SoftEdge ? (byte)255
                                  : (byte)((v - tolerance) * 255 / SoftEdge);
                    }

                    byte[] buf = _buffer;
                    int bands = Math.Max(1, Math.Min(Environment.ProcessorCount, height / 32));
                    System.Threading.Tasks.Parallel.For(0, bands, band =>
                    {
                        int start = (int)((long)height * band / bands) * stride;
                        int end = (int)((long)height * (band + 1) / bands) * stride;
                        for (int i = start; i < end; i += 4)
                        {
                            // Pixel layout is B, G, R, A. Distance is the largest per-channel difference.
                            int d = distB[buf[i]];
                            int g = distG[buf[i + 1]];
                            int r = distR[buf[i + 2]];
                            if (g > d) d = g;
                            if (r > d) d = r;

                            byte level = levels[d];
                            buf[i] = level;
                            buf[i + 1] = level;
                            buf[i + 2] = level;
                            buf[i + 3] = 255;
                        }
                    });

                    result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    BitmapData dstData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        // Same size and 4 bytes per pixel, so the same stride
                        Marshal.Copy(buf, 0, dstData.Scan0, size);
                    }
                    finally
                    {
                        result.UnlockBits(dstData);
                    }
                    return result;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error applying text color filter: {ex.Message}");
                result?.Dispose();
                return null;
            }
        }

        private static void SavePreviewIfRequested(Bitmap filtered)
        {
            string? path = _pendingPreviewPath;
            if (path == null)
                return;
            _pendingPreviewPath = null;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                filtered.Save(path, ImageFormat.Png);
                Console.WriteLine($"Text color filter preview saved to {path}");
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving text color filter preview: {ex.Message}");
            }
        }

        public static bool TryParseColor(string? value, out Color color)
        {
            color = Color.White;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string hex = value.Trim().TrimStart('#');
            if (hex.Length == 8)
                hex = hex.Substring(2); // drop alpha (#AARRGGBB)
            if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
                return false;

            color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }

        public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
