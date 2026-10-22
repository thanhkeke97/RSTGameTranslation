using System;
using System.Collections.Generic;
using System.Windows;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace RSTGameTranslation
{
    /// <summary>
    /// Wraps its children like a horizontal WrapPanel, then widens the children of each row so the row
    /// fills the whole width (the leftover space is shared equally). Keeps the toolbar from ending a row
    /// with a large empty gap when the next group does not fit.
    /// </summary>
    public class JustifiedWrapPanel : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            double maxWidth = availableSize.Width;
            double rowWidth = 0, rowHeight = 0, totalHeight = 0, widest = 0;

            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Size size = child.DesiredSize;
                if (rowWidth > 0 && rowWidth + size.Width > maxWidth)
                {
                    widest = Math.Max(widest, rowWidth);
                    totalHeight += rowHeight;
                    rowWidth = rowHeight = 0;
                }
                rowWidth += size.Width;
                rowHeight = Math.Max(rowHeight, size.Height);
            }
            widest = Math.Max(widest, rowWidth);
            totalHeight += rowHeight;

            return new Size(double.IsInfinity(maxWidth) ? widest : maxWidth, totalHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var row = new List<UIElement>();
            double rowWidth = 0, rowHeight = 0, y = 0;

            foreach (UIElement child in InternalChildren)
            {
                Size size = child.DesiredSize;
                if (row.Count > 0 && rowWidth + size.Width > finalSize.Width)
                {
                    ArrangeRow(row, rowWidth, rowHeight, y, finalSize.Width);
                    y += rowHeight;
                    row.Clear();
                    rowWidth = rowHeight = 0;
                }
                row.Add(child);
                rowWidth += size.Width;
                rowHeight = Math.Max(rowHeight, size.Height);
            }
            ArrangeRow(row, rowWidth, rowHeight, y, finalSize.Width);

            return finalSize;
        }

        private static void ArrangeRow(List<UIElement> row, double rowWidth, double rowHeight, double y, double width)
        {
            // Collapsed children take no space and get no share of the leftover width
            int visible = row.FindAll(c => c.DesiredSize.Width > 0).Count;
            double extra = visible > 0 ? Math.Max(0, width - rowWidth) / visible : 0;

            double x = 0;
            foreach (UIElement child in row)
            {
                double w = child.DesiredSize.Width > 0 ? child.DesiredSize.Width + extra : 0;
                child.Arrange(new Rect(x, y, w, rowHeight));
                x += w;
            }
        }
    }
}
