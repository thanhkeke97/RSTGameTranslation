using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace RSTGameTranslation
{
    /// <summary>
    /// Outlines the translation areas on screen right after they are selected, so the user sees that
    /// the selection worked and, with several areas, which number each one has (Alt+number).
    /// Each area gets its own click-through window. The outline and the number badge are drawn just
    /// OUTSIDE the area: Windows 10 cannot leave our windows out of a screen capture, and anything
    /// drawn inside an area would be read by OCR. (With several areas the space between them is
    /// masked before OCR, so the outlines there are never read either.)
    /// </summary>
    public static class AreaBorderOverlay
    {
        // Sizes in DIPs, scaled to the area's monitor
        private const double BorderThicknessDip = 3;
        private const double BadgeHeightDip = 22;

        private static readonly Color NewestAreaColor = Color.FromRgb(34, 197, 94);  // green
        private static readonly Color OtherAreaColor = Color.FromRgb(59, 130, 246);  // blue

        private static readonly List<Window> s_windows = new List<Window>();
        private static DispatcherTimer? s_hideTimer;
        private static bool s_numbered;
        private static int s_highlightIndex;

        public static bool IsVisible => s_windows.Count > 0;

        /// <summary>
        /// Outline <paramref name="areas"/>. With <paramref name="numbered"/> each area also gets its
        /// number; <paramref name="highlightIndex"/> marks the one just added. With
        /// <paramref name="autoHide"/> the outlines disappear by themselves after that time,
        /// otherwise they stay until <see cref="Hide"/>.
        /// </summary>
        public static void Show(IReadOnlyList<TranslationAreaInfo> areas, bool numbered, int highlightIndex, TimeSpan? autoHide)
        {
            Hide();
            s_numbered = numbered;
            s_highlightIndex = highlightIndex;
            CreateWindows(areas);

            if (autoHide is TimeSpan delay && s_windows.Count > 0)
            {
                s_hideTimer = new DispatcherTimer { Interval = delay };
                s_hideTimer.Tick += (_, _) => Hide();
                s_hideTimer.Start();
            }
        }

        /// <summary>The areas moved (with the game window): redraw the visible outlines at the new place.</summary>
        public static void MoveTo(IReadOnlyList<TranslationAreaInfo> areas)
        {
            if (!IsVisible) return;
            CloseWindows();
            CreateWindows(areas);
        }

        public static void Hide()
        {
            s_hideTimer?.Stop();
            s_hideTimer = null;
            CloseWindows();
        }

        private static void CloseWindows()
        {
            foreach (var window in s_windows)
            {
                try { window.Close(); } catch { }
            }
            s_windows.Clear();
        }

        private static void CreateWindows(IReadOnlyList<TranslationAreaInfo> areas)
        {
            for (int i = 0; i < areas.Count; i++)
            {
                try
                {
                    s_windows.Add(CreateWindow(areas[i], i, s_numbered, i == s_highlightIndex));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AreaBorder] Could not outline area {i + 1}: {ex.Message}");
                }
            }
        }

        private static Window CreateWindow(TranslationAreaInfo area, int index, bool numbered, bool highlighted)
        {
            // The DPI saved with the area is that of its monitor, which is where this window goes
            double scale = area.DpiScaleY > 0 ? area.DpiScaleY : 1.0;
            int thickness = Math.Max(2, (int)Math.Round(BorderThicknessDip * scale));
            int badgeHeight = numbered ? (int)Math.Round(BadgeHeightDip * scale) : 0;

            var areaRect = new System.Drawing.Rectangle((int)area.X, (int)area.Y, (int)area.Width, (int)area.Height);
            int monitorTop = System.Windows.Forms.Screen.FromRectangle(areaRect).Bounds.Top;
            // Number above the area, or below it when the area touches the top of its monitor
            bool badgeAbove = areaRect.Top - thickness - badgeHeight >= monitorTop;

            int x = areaRect.Left - thickness;
            int y = areaRect.Top - thickness - (badgeAbove ? badgeHeight : 0);
            int width = areaRect.Width + 2 * thickness;
            int height = areaRect.Height + 2 * thickness + badgeHeight;

            Color color = highlighted ? NewestAreaColor : OtherAreaColor;
            var brush = new SolidColorBrush(color);

            var frame = new Border
            {
                BorderBrush = brush,
                BorderThickness = new Thickness(thickness / scale),
                Background = null
            };

            var grid = new Grid();
            if (numbered)
            {
                var badge = new Border
                {
                    Background = brush,
                    CornerRadius = badgeAbove ? new CornerRadius(4, 4, 0, 0) : new CornerRadius(0, 0, 4, 4),
                    Padding = new Thickness(8, 0, 8, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock
                    {
                        Text = (index + 1).ToString(),
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                grid.RowDefinitions.Add(new RowDefinition { Height = badgeAbove ? new GridLength(badgeHeight / scale) : new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = badgeAbove ? new GridLength(1, GridUnitType.Star) : new GridLength(badgeHeight / scale) });
                Grid.SetRow(badge, badgeAbove ? 0 : 1);
                Grid.SetRow(frame, badgeAbove ? 1 : 0);
                grid.Children.Add(badge);
            }
            grid.Children.Add(frame);

            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Focusable = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                // Close to the final place already (exact on the primary monitor), so the window does
                // not flash elsewhere before it is positioned in physical pixels below
                Left = x / scale,
                Top = y / scale,
                Width = width / scale,
                Height = height / scale,
                Content = grid
            };
            window.SourceInitialized += (_, _) => MakeClickThrough(window);
            window.Show();
            DpiHelper.PositionWindowPhysical(new WindowInteropHelper(window).Handle, x, y, width, height);
            return window;
        }

        // Click-through, never activated, not in Alt+Tab, and left out of screen captures where Windows can
        private static void MakeClickThrough(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
    }
}
