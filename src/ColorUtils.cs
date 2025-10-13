using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Media;
using Color = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;

namespace RSTGameTranslation
{
    public static class ColorUtils
    {
        /// <summary>
        /// Extracts the dominant color from a bitmap region
        /// </summary>
        /// <param name="bitmap">The source bitmap</param>
        /// <param name="x">X coordinate of the region</param>
        /// <param name="y">Y coordinate of the region</param>
        /// <param name="width">Width of the region</param>
        /// <param name="height">Height of the region</param>
        /// <returns>The dominant color in the region</returns>
        public static Color GetDominantColor(Bitmap bitmap, int x, int y, int width, int height)
        {
            // Ensure coordinates are within bitmap bounds
            x = Math.Max(0, Math.Min(x, bitmap.Width - 1));
            y = Math.Max(0, Math.Min(y, bitmap.Height - 1));
            width = Math.Min(width, bitmap.Width - x);
            height = Math.Min(height, bitmap.Height - y);

            if (width <= 0 || height <= 0)
                return Color.Black;

            // Dictionary to count color occurrences
            Dictionary<int, ColorCount> colorCounts = new Dictionary<int, ColorCount>();

            // Sample pixels (don't need to check every pixel for performance)
            int sampleStep = Math.Max(1, Math.Min(width, height) / 10);
            
            for (int i = x; i < x + width; i += sampleStep)
            {
                for (int j = y; j < y + height; j += sampleStep)
                {
                    Color pixelColor = bitmap.GetPixel(i, j);
                    
                    // Skip fully transparent pixels
                    if (pixelColor.A < 10)
                        continue;
                        
                    // Quantize the color to reduce the number of unique colors
                    int quantizedColor = QuantizeColor(pixelColor);
                    
                    if (colorCounts.TryGetValue(quantizedColor, out ColorCount count))
                    {
                        count.Count++;
                    }
                    else
                    {
                        colorCounts[quantizedColor] = new ColorCount { Color = pixelColor, Count = 1 };
                    }
                }
            }

            // If no colors were found (e.g., all transparent), return black
            if (colorCounts.Count == 0)
                return Color.Black;

            // Get the most common color
            var dominantColor = colorCounts.Values.OrderByDescending(c => c.Count).First().Color;
            
            return dominantColor;
        }

        /// <summary>
        /// Quantizes a color to reduce the number of unique colors
        /// </summary>
        private static int QuantizeColor(Color color)
        {
            // Quantize to fewer bits per channel (e.g., 3 bits per channel)
            int r = color.R & 0xE0; // 3 most significant bits
            int g = color.G & 0xE0;
            int b = color.B & 0xE0;
            
            return (r << 16) | (g << 8) | b;
        }

        /// <summary>
        /// Determines if a color is light or dark
        /// </summary>
        public static bool IsLightColor(Color color)
        {
            // Calculate perceived brightness using the formula:
            // (0.299*R + 0.587*G + 0.114*B)
            double brightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            return brightness > 0.5;
        }

        /// <summary>
        /// Converts System.Drawing.Color to System.Windows.Media.Color
        /// </summary>
        public static MediaColor ToMediaColor(this Color color)
        {
            return MediaColor.FromArgb(color.A, color.R, color.G, color.B);
        }

        /// <summary>
        /// Gets a contrasting text color (black or white) based on background color
        /// </summary>
        public static MediaColor GetContrastingTextColor(Color backgroundColor)
        {
            double brightness = (0.299 * backgroundColor.R + 0.587 * backgroundColor.G + 0.114 * backgroundColor.B) / 255;
            
            if (brightness > 0.7) 
            {
                return MediaColor.FromRgb(0, 0, 0);
            }
            else if (brightness > 0.5) 
            {
                return MediaColor.FromRgb(20, 20, 20);
            }
            else if (brightness < 0.2)
            {
                return MediaColor.FromRgb(255, 255, 255);
            }
            else 
            {
                return MediaColor.FromRgb(240, 240, 240);
            }
        }
        
        /// <summary>
        /// Creates a background color based on the dominant color with full opacity
        /// </summary>
        public static MediaColor CreateBackgroundColor(Color dominantColor, byte alpha = 255)
        {
            return MediaColor.FromArgb(
                alpha, 
                dominantColor.R,
                dominantColor.G,
                dominantColor.B
            );
        }

        private class ColorCount
        {
            public Color Color { get; set; }
            public int Count { get; set; }
        }
    }
}