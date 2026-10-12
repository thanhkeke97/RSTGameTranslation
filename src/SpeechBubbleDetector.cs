using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RSTGameTranslation
{
    /// <summary>
    /// Finds which manga speech bubble each text box sits in by flood-filling the bright background
    /// around the text. Boxes whose background is the same enclosed bright region share a bubble id.
    /// Boxes on page margins, gutters or artwork (region too large, or no bright background) get null.
    /// </summary>
    internal static class SpeechBubbleDetector
    {
        // Pixels at least this bright count as bubble background
        private const byte BackgroundMinLuma = 180;
        // A bright region larger than this fraction of the image is page background, not a bubble
        private const double MaxBubbleAreaRatio = 0.25;
        // A bright region smaller than this fraction of the text box is a hole inside a glyph
        private const double MinBubbleAreaToTextRatio = 0.5;
        // Text fills a good part of a bubble; a region with far more background than the text inside it
        // is a panel (or the page) holding loose text and open bubbles, not one speech bubble
        private const double MaxBackgroundToTextRatio = 6.0;

        private const int Unvisited = 0;
        private const int NotABubble = -1;

        public static int?[] AssignBubbleIds(Bitmap bitmap, IReadOnlyList<RectangleF> textBoxes)
        {
            var ids = new int?[textBoxes.Count];
            if (textBoxes.Count == 0)
                return ids;

            int width = bitmap.Width;
            int height = bitmap.Height;
            byte[] luma = ReadLuma(bitmap);
            int[] labels = new int[width * height];
            int maxArea = (int)(width * height * MaxBubbleAreaRatio);
            int nextId = 1;
            var region = new List<int>();
            var stack = new Stack<int>();
            var regionAreas = new Dictionary<int, int>();

            for (int i = 0; i < textBoxes.Count; i++)
            {
                RectangleF box = textBoxes[i];
                int seed = FindSeed(luma, width, height, box);
                if (seed < 0)
                    continue;

                if (labels[seed] != Unvisited)
                {
                    if (labels[seed] > 0)
                        ids[i] = labels[seed];
                    continue;
                }

                int id = nextId++;
                bool isBubble = Fill(luma, labels, width, height, seed, id, maxArea, region, stack);
                if (isBubble && region.Count < box.Width * box.Height * MinBubbleAreaToTextRatio)
                    isBubble = false;

                if (isBubble)
                {
                    ids[i] = id;
                    regionAreas[id] = region.Count;
                }
                else
                {
                    // Remember the rejection so other boxes in the same region don't refill it
                    // (including pixels queued but not yet visited when the fill gave up)
                    foreach (int p in region)
                        labels[p] = NotABubble;
                    foreach (int p in stack)
                        labels[p] = NotABubble;
                }
            }

            // Drop regions that are mostly empty background compared to the text they hold
            foreach (var group in Enumerable.Range(0, ids.Length).Where(i => ids[i] != null).GroupBy(i => ids[i]!.Value))
            {
                double textArea = group.Sum(i => (double)textBoxes[i].Width * textBoxes[i].Height);
                if (regionAreas[group.Key] > textArea * MaxBackgroundToTextRatio)
                {
                    foreach (int i in group)
                        ids[i] = null;
                }
            }

            return ids;
        }

        // 4-connected fill over bright pixels; false when the region is too large or joins a rejected one
        private static bool Fill(byte[] luma, int[] labels, int width, int height, int seed, int id,
                                 int maxArea, List<int> region, Stack<int> stack)
        {
            region.Clear();
            stack.Clear();
            stack.Push(seed);
            labels[seed] = id;

            while (stack.Count > 0)
            {
                int p = stack.Pop();
                region.Add(p);
                if (region.Count > maxArea)
                    return false;

                int x = p % width;
                int y = p / width;
                if (x > 0 && !TryVisit(p - 1)) return false;
                if (x < width - 1 && !TryVisit(p + 1)) return false;
                if (y > 0 && !TryVisit(p - width)) return false;
                if (y < height - 1 && !TryVisit(p + width)) return false;
            }

            return true;

            bool TryVisit(int q)
            {
                if (labels[q] == NotABubble)
                    return false;
                if (labels[q] == Unvisited && luma[q] >= BackgroundMinLuma)
                {
                    labels[q] = id;
                    stack.Push(q);
                }
                return true;
            }
        }

        // A bright pixel just outside the text box: bubbles leave a margin around their text,
        // whereas the white inside a glyph (e.g. the hole in 口) is enclosed and tiny
        private static int FindSeed(byte[] luma, int width, int height, RectangleF box)
        {
            float margin = Math.Max(2f, Math.Min(box.Width, box.Height) * 0.25f);
            float centerX = box.X + box.Width / 2;
            float centerY = box.Y + box.Height / 2;
            var probes = new[]
            {
                new PointF(box.Left - margin, centerY),
                new PointF(box.Right + margin, centerY),
                new PointF(centerX, box.Top - margin),
                new PointF(centerX, box.Bottom + margin),
                new PointF(box.Left - margin, box.Top - margin),
                new PointF(box.Right + margin, box.Top - margin),
                new PointF(box.Left - margin, box.Bottom + margin),
                new PointF(box.Right + margin, box.Bottom + margin),
            };

            foreach (PointF probe in probes)
            {
                int x = (int)probe.X;
                int y = (int)probe.Y;
                if (x < 0 || y < 0 || x >= width || y >= height)
                    continue;

                int p = y * width + x;
                if (luma[p] >= BackgroundMinLuma)
                    return p;
            }

            return -1;
        }

        private static byte[] ReadLuma(Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] row = new byte[stride];
                byte[] luma = new byte[width * height];
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, stride);
                    for (int x = 0; x < width; x++)
                    {
                        int i = x * 3; // BGR
                        luma[y * width + x] = (byte)((row[i + 2] * 299 + row[i + 1] * 587 + row[i] * 114) / 1000);
                    }
                }
                return luma;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
    }
}
