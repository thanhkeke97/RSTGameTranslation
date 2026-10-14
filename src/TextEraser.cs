using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using MediaColor = System.Windows.Media.Color;
using PixelFormats = System.Windows.Media.PixelFormats;

namespace RSTGameTranslation
{
    /// <summary>
    /// Removes the original text from a captured region by filling its strokes from the surrounding
    /// background (inpainting), so the translation can sit on the original texture, gradient or art
    /// instead of a flat box. The patch fades out over its margin to blend into the real screen.
    /// </summary>
    internal static class TextEraser
    {
        // Pixels this far (RGB distance) from the background are text strokes. The threshold adapts to
        // the contrast of the text in the box, between these bounds: a fixed 60 left faint text
        // (hints, greyed-out options) fully visible, while 25 still ignores compression noise.
        private const double MaxInkDistance = 60;
        private const double MinInkDistance = 25;
        // Share of the strongest contrast in the box that counts as a stroke
        private const double InkContrastShare = 0.45;
        // Strokes are grown by this many pixels to also cover their anti-aliased edges
        private const int MaskDilation = 2;
        // Largest gap (pixels) between strokes still followed when erasing glyph parts outside the box
        private const int MarginStrokeGap = 2;
        // When strokes cover more of the box than this, the "background" inside is not reliable: refill it all
        private const double MaxInkShare = 0.6;
        private const int SmoothingPasses = 3;

        /// <summary>
        /// Builds the cleaned patch for a text box (bitmap pixels). Returns null when the box is unusable.
        /// <paramref name="patchBounds"/> receives the patch position in bitmap pixels: the box plus a margin.
        /// </summary>
        public static BitmapSource? CreateErasedPatch(Bitmap bitmap, Rectangle textBox, MediaColor background, out Rectangle patchBounds)
        {
            var image = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            int margin = Math.Clamp(Math.Min(textBox.Width, textBox.Height) / 4, 3, 12);
            patchBounds = Rectangle.Intersect(Rectangle.Inflate(textBox, margin, margin), image);
            Rectangle inner = Rectangle.Intersect(textBox, image);
            if (inner.Width <= 0 || inner.Height <= 0)
                return null;

            int w = patchBounds.Width, h = patchBounds.Height;
            float[] r = new float[w * h], g = new float[w * h], b = new float[w * h];
            ReadPixels(bitmap, patchBounds, r, g, b);

            // Local coordinates of the text box inside the patch
            int left = inner.X - patchBounds.X, top = inner.Y - patchBounds.Y;
            int right = left + inner.Width, bottom = top + inner.Height;

            double inkDistance = EstimateInkDistance(r, g, b, w, left, top, right, bottom, background);
            bool[] mask = BuildMask(r, g, b, w, left, top, right, bottom, background, inkDistance);
            var inkColor = DominantInkColor(r, g, b, w, left, top, right, bottom, background, inkDistance);

            // OCR boxes are tight and often cut glyph parts off (descenders, accents, the edge of the
            // last letter); those parts sit in the margin and stayed visible next to the translation.
            // Follow the strokes from inside the box into the margin and erase what stays connected.
            if (inkColor != null)
                GrowMaskIntoMargin(mask, r, g, b, w, h, left, top, right, bottom, background, inkDistance, inkColor.Value);

            // Other strokes in the margin (neighbouring text, bubble outlines) stay as they are but must
            // not be smeared into the fill. Judge them by closeness to the ink color rather than distance
            // from one background color, which would reject half a gradient.
            var usable = new bool[w * h];
            for (int i = 0; i < usable.Length; i++)
                usable[i] = !mask[i] && (inkColor == null || !IsCloserToInk(r[i], g[i], b[i], inkColor.Value, background));

            // Start from the background color, so pixels no usable neighbour reaches still look right
            for (int i = 0; i < mask.Length; i++)
                if (mask[i])
                {
                    r[i] = background.R; g[i] = background.G; b[i] = background.B;
                }

            Fill(r, g, b, mask, usable, w, h);

            return ToBitmapSource(r, g, b, mask, w, h, left, top, right, bottom);
        }

        // Stroke threshold for this box: a share of its strongest contrast (ignoring the top 2% as
        // outliers), within [MinInkDistance, MaxInkDistance]
        private static double EstimateInkDistance(float[] r, float[] g, float[] b, int w, int left, int top, int right, int bottom, MediaColor bg)
        {
            var distances = new List<double>((right - left) * (bottom - top));
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                {
                    int i = y * w + x;
                    double dr = r[i] - bg.R, dg = g[i] - bg.G, db = b[i] - bg.B;
                    distances.Add(Math.Sqrt(dr * dr + dg * dg + db * db));
                }
            if (distances.Count == 0)
                return MaxInkDistance;

            distances.Sort();
            double strongest = distances[(int)((distances.Count - 1) * 0.98)];
            return Math.Clamp(strongest * InkContrastShare, MinInkDistance, MaxInkDistance);
        }

        // Follow strokes from inside the text box out into the margin (8-connected, through pixels that
        // look like the ink) and mask them, dilated like the strokes inside
        private static void GrowMaskIntoMargin(bool[] mask, float[] r, float[] g, float[] b, int w, int h,
                                               int left, int top, int right, int bottom, MediaColor bg,
                                               double inkDistance, (float R, float G, float B) inkColor)
        {
            bool InBox(int x, int y) => x >= left && x < right && y >= top && y < bottom;
            bool IsStroke(int i) => IsInk(r[i], g[i], b[i], bg, inkDistance) && IsCloserToInk(r[i], g[i], b[i], inkColor, bg);

            var grown = new bool[w * h];
            var queue = new Queue<int>();
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                {
                    int i = y * w + x;
                    if (IsStroke(i))
                        queue.Enqueue(i);
                }

            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, y = i / w;
                // Steps of up to MarginStrokeGap + 1 pixels also reach marks detached from their letter
                // (Vietnamese/French accents, the dot of i/j) without crossing the gap to the next line
                for (int dy = -MarginStrokeGap - 1; dy <= MarginStrokeGap + 1; dy++)
                    for (int dx = -MarginStrokeGap - 1; dx <= MarginStrokeGap + 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h || InBox(nx, ny))
                            continue;
                        int j = ny * w + nx;
                        if (!grown[j] && IsStroke(j))
                        {
                            grown[j] = true;
                            queue.Enqueue(j);
                        }
                    }
            }

            // Dilate the grown strokes to cover their anti-aliased edges too
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!grown[y * w + x])
                        continue;
                    for (int dy = -MaskDilation; dy <= MaskDilation; dy++)
                        for (int dx = -MaskDilation; dx <= MaskDilation; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < w && ny < h)
                                mask[ny * w + nx] = true;
                        }
                }
        }

        // Text strokes inside the box, dilated; the whole box when strokes dominate it
        private static bool[] BuildMask(float[] r, float[] g, float[] b, int w, int left, int top, int right, int bottom, MediaColor bg, double inkDistance)
        {
            int h = r.Length / w;
            var ink = new bool[w * h];
            int inkCount = 0;
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                {
                    int i = y * w + x;
                    if (IsInk(r[i], g[i], b[i], bg, inkDistance))
                    {
                        ink[i] = true;
                        inkCount++;
                    }
                }

            var mask = new bool[w * h];
            bool wholeBox = inkCount > (right - left) * (bottom - top) * MaxInkShare;
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                {
                    if (wholeBox)
                    {
                        mask[y * w + x] = true;
                        continue;
                    }
                    for (int dy = -MaskDilation; dy <= MaskDilation && !mask[y * w + x]; dy++)
                        for (int dx = -MaskDilation; dx <= MaskDilation; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= left && nx < right && ny >= top && ny < bottom && ink[ny * w + nx])
                            {
                                mask[y * w + x] = true;
                                break;
                            }
                        }
                }
            return mask;
        }

        // Most common stroke color inside the box (coarse bins), null when there are no strokes
        private static (float R, float G, float B)? DominantInkColor(float[] r, float[] g, float[] b, int w,
                                                                       int left, int top, int right, int bottom, MediaColor bg, double inkDistance)
        {
            var bins = new Dictionary<int, (float R, float G, float B, int Count)>();
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                {
                    int i = y * w + x;
                    if (!IsInk(r[i], g[i], b[i], bg, inkDistance))
                        continue;
                    int key = ((int)r[i] >> 4 << 8 | (int)g[i] >> 4) << 4 | (int)b[i] >> 4;
                    bins.TryGetValue(key, out var bin);
                    bins[key] = (bin.R + r[i], bin.G + g[i], bin.B + b[i], bin.Count + 1);
                }

            if (bins.Count == 0)
                return null;
            var mode = bins.Values.MaxBy(v => v.Count);
            return (mode.R / mode.Count, mode.G / mode.Count, mode.B / mode.Count);
        }

        // Within the stroke side of the midpoint between the ink and background colors
        private static bool IsCloserToInk(float r, float g, float b, (float R, float G, float B) ink, MediaColor bg)
        {
            float ir = r - ink.R, ig = g - ink.G, ib = b - ink.B;
            float br = r - bg.R, bgg = g - bg.G, bb = b - bg.B;
            return ir * ir + ig * ig + ib * ib < br * br + bgg * bgg + bb * bb;
        }

        private static bool IsInk(float r, float g, float b, MediaColor bg, double inkDistance)
        {
            float dr = r - bg.R, dg = g - bg.G, db = b - bg.B;
            return dr * dr + dg * dg + db * db > inkDistance * inkDistance;
        }

        // Fill masked pixels from the outside in (each one averages its already known neighbours),
        // then smooth the filled area so the fill order doesn't leave streaks
        private static void Fill(float[] r, float[] g, float[] b, bool[] mask, bool[] usable, int w, int h)
        {
            var known = (bool[])usable.Clone();
            var queued = new bool[w * h];
            var queue = new Queue<int>();
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i] && HasKnownNeighbour(known, i, w, h))
                {
                    queue.Enqueue(i);
                    queued[i] = true;
                }
            }

            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, y = i / w;
                float sr = 0, sg = 0, sb = 0;
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= w || ny >= h)
                            continue;
                        int j = ny * w + nx;
                        if (known[j])
                        {
                            sr += r[j]; sg += g[j]; sb += b[j]; n++;
                        }
                        else if (mask[j] && !queued[j])
                        {
                            queue.Enqueue(j);
                            queued[j] = true;
                        }
                    }

                if (n > 0)
                {
                    r[i] = sr / n; g[i] = sg / n; b[i] = sb / n;
                }
                known[i] = true;
            }

            for (int pass = 0; pass < SmoothingPasses; pass++)
            {
                for (int i = 0; i < mask.Length; i++)
                {
                    if (!mask[i])
                        continue;
                    int x = i % w, y = i / w;
                    float sr = r[i], sg = g[i], sb = b[i];
                    int n = 1;
                    void Add(int j)
                    {
                        if (!mask[j] && !usable[j])
                            return;
                        sr += r[j]; sg += g[j]; sb += b[j]; n++;
                    }
                    if (x > 0) Add(i - 1);
                    if (x < w - 1) Add(i + 1);
                    if (y > 0) Add(i - w);
                    if (y < h - 1) Add(i + w);
                    r[i] = sr / n; g[i] = sg / n; b[i] = sb / n;
                }
            }
        }

        private static bool HasKnownNeighbour(bool[] known, int i, int w, int h)
        {
            int x = i % w, y = i / w;
            return (x > 0 && known[i - 1]) || (x < w - 1 && known[i + 1])
                || (y > 0 && known[i - w]) || (y < h - 1 && known[i + w]);
        }

        // Opaque over the text box and over erased strokes in the margin, fading to transparent across
        // the rest of the margin
        private static BitmapSource ToBitmapSource(float[] r, float[] g, float[] b, bool[] mask, int w, int h, int left, int top, int right, int bottom)
        {
            byte[] pixels = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    int dx = Math.Max(0, Math.Max(left - x, x - (right - 1)));
                    int dy = Math.Max(0, Math.Max(top - y, y - (bottom - 1)));
                    int outside = Math.Max(dx, dy);
                    int fade = Math.Max(Math.Max(left, top), Math.Max(w - right, h - bottom));
                    double alpha = outside == 0 || fade == 0 || mask[i] ? 1.0 : Math.Max(0, 1.0 - (double)outside / (fade + 1));

                    pixels[i * 4] = (byte)Math.Clamp(b[i], 0, 255);
                    pixels[i * 4 + 1] = (byte)Math.Clamp(g[i], 0, 255);
                    pixels[i * 4 + 2] = (byte)Math.Clamp(r[i], 0, 255);
                    pixels[i * 4 + 3] = (byte)(alpha * 255);
                }

            // Pbgra would need premultiplied values; Bgra32 keeps the colors straight
            var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
            source.Freeze();
            return source;
        }

        private static void ReadPixels(Bitmap bitmap, Rectangle area, float[] r, float[] g, float[] b)
        {
            var data = bitmap.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                byte[] row = new byte[Math.Abs(data.Stride)];
                for (int y = 0; y < area.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                    for (int x = 0; x < area.Width; x++)
                    {
                        int i = y * area.Width + x;
                        b[i] = row[x * 3];
                        g[i] = row[x * 3 + 1];
                        r[i] = row[x * 3 + 2];
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
    }
}
