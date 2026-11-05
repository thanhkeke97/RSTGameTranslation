// DpiHelper.cs - Helper class for DPI scaling operations
using System;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using Point = System.Windows.Point;
using System.Runtime.InteropServices;

namespace RSTGameTranslation
{
    public static class DpiHelper
    {
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint dwFlags);
        
        [DllImport("Shcore.dll")]
        static extern IntPtr GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out System.Drawing.Point lpPoint);
        
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
        
        private const int MDT_EFFECTIVE_DPI = 0;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        
        // Cache DPI per screen to handle multi-monitor setups with different DPI
        private static double _cachedDpiScaleX = -1;
        private static double _cachedDpiScaleY = -1;
        private static int _lastScreenIndex = -1;
        private static DateTime _cacheTimestamp = DateTime.MinValue;
        private const double CACHE_DURATION_SECONDS = 5.0; // Cache valid for 5 seconds
        
        /// <summary>
        /// Get DPI scaling for a specific screen
        /// </summary>
        public static void GetDpiForScreen(Screen? screen, out double scaleX, out double scaleY)
        {
            try
            {
                if (screen == null)
                {
                    scaleX = 1.0;
                    scaleY = 1.0;
                    return;
                }
                
                System.Drawing.Point point = new System.Drawing.Point(
                    screen.Bounds.Left + screen.Bounds.Width / 2,
                    screen.Bounds.Top + screen.Bounds.Height / 2
                );
                
                IntPtr monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
                
                uint dpiX, dpiY;
                GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
                
                scaleX = dpiX / 96.0;
                scaleY = dpiY / 96.0;
                
                Console.WriteLine($"DpiHelper: Got DPI for screen {screen.DeviceName}: {dpiX}/{dpiY} ({scaleX:F2}x/{scaleY:F2}y)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting DPI scaling: {ex.Message}");
                scaleX = 1.0;
                scaleY = 1.0;
            }
        }
        
        /// <summary>
        /// Get DPI scaling for the current screen (where the mouse cursor is)
        /// This method checks if screen has changed and invalidates cache if needed
        /// </summary>
        public static void GetCurrentScreenDpi(out double scaleX, out double scaleY)
        {
            // Get current screen based on mouse position
            System.Drawing.Point cursorPos;
            GetCursorPos(out cursorPos);
            int currentScreenIndex = GetScreenIndexFromPoint(cursorPos);
            
            // Check if cache is still valid (same screen and within time window)
            bool cacheValid = (currentScreenIndex == _lastScreenIndex) &&
                              (_cachedDpiScaleX > 0) &&
                              (_cachedDpiScaleY > 0) &&
                              (DateTime.Now - _cacheTimestamp).TotalSeconds < CACHE_DURATION_SECONDS;
            
            if (cacheValid)
            {
                scaleX = _cachedDpiScaleX;
                scaleY = _cachedDpiScaleY;
                return;
            }
            
            // Cache invalid or screen changed - get fresh DPI
            var screens = Screen.AllScreens;
            
            if (currentScreenIndex >= 0 && currentScreenIndex < screens.Length)
            {
                GetDpiForScreen(screens[currentScreenIndex], out scaleX, out scaleY);
            }
            else
            {
                // Fallback to selected screen from config
                int selectedScreenIndex = ConfigManager.Instance.GetSelectedScreenIndex();
                if (selectedScreenIndex >= 0 && selectedScreenIndex < screens.Length)
                {
                    GetDpiForScreen(screens[selectedScreenIndex], out scaleX, out scaleY);
                }
                else
                {
                    GetDpiForScreen(Screen.PrimaryScreen, out scaleX, out scaleY);
                }
            }
            
            // Update cache
            _cachedDpiScaleX = scaleX;
            _cachedDpiScaleY = scaleY;
            _lastScreenIndex = currentScreenIndex;
            _cacheTimestamp = DateTime.Now;
        }
        
        /// <summary>
        /// Get DPI scaling for the screen selected in the application's screen selection ComboBox
        /// This is the most reliable method as it uses the user's explicit screen selection
        /// </summary>
        public static void GetDpiForSelectedScreen(out double scaleX, out double scaleY)
        {
            int selectedScreenIndex = ConfigManager.Instance.GetSelectedScreenIndex();
            var screens = Screen.AllScreens;
            
            if (selectedScreenIndex >= 0 && selectedScreenIndex < screens.Length)
            {
                GetDpiForScreen(screens[selectedScreenIndex], out scaleX, out scaleY);
                Console.WriteLine($"DpiHelper: Got DPI for selected screen {selectedScreenIndex} ({screens[selectedScreenIndex].DeviceName}): {scaleX:F2}x/{scaleY:F2}y");
            }
            else
            {
                GetDpiForScreen(Screen.PrimaryScreen, out scaleX, out scaleY);
                Console.WriteLine($"DpiHelper: Invalid screen index {selectedScreenIndex}, using primary screen: {scaleX:F2}x/{scaleY:F2}y");
            }
        }
        
        /// <summary>
        /// Get screen index from a point
        /// </summary>
        private static int GetScreenIndexFromPoint(System.Drawing.Point point)
        {
            var screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                if (screens[i].Bounds.Contains(point))
                {
                    return i;
                }
            }
            return 0; // Return 0 if point doesn't match any screen
        }
        
        /// <summary>
        /// Set known DPI scale (used when capturing a region)
        /// </summary>
        public static void SetKnownDpiScale(double dpiScaleX, double dpiScaleY, int screenIndex = -1)
        {
            _cachedDpiScaleX = dpiScaleX;
            _cachedDpiScaleY = dpiScaleY;
            _cacheTimestamp = DateTime.Now;
            
            // Also update the screen index so the cache isn't immediately invalidated
            if (screenIndex >= 0)
            {
                _lastScreenIndex = screenIndex;
            }
            else
            {
                // Try to determine from cursor position as fallback
                System.Drawing.Point cursorPos;
                GetCursorPos(out cursorPos);
                _lastScreenIndex = GetScreenIndexFromPoint(cursorPos);
            }
            
            Console.WriteLine($"DpiHelper: Set known DPI scale: {dpiScaleX:F2}x, {dpiScaleY:F2}y (screen index: {_lastScreenIndex})");
        }
        
        /// <summary>
        /// Invalidate DPI cache (call when screen configuration changes)
        /// </summary>
        public static void InvalidateCache()
        {
            _cachedDpiScaleX = -1;
            _cachedDpiScaleY = -1;
            _lastScreenIndex = -1;
            _cacheTimestamp = DateTime.MinValue;
            Console.WriteLine("DpiHelper: DPI cache invalidated");
        }
        
        /// <summary>
        /// Convert physical pixels to logical (WPF) units
        /// </summary>
        public static Point PhysicalToLogical(Point physicalPoint)
        {
            GetCurrentScreenDpi(out double scaleX, out double scaleY);
            return new Point(physicalPoint.X / scaleX, physicalPoint.Y / scaleY);
        }
        
        /// <summary>
        /// Convert logical (WPF) units to physical pixels
        /// </summary>
        public static Point LogicalToPhysical(Point logicalPoint)
        {
            GetCurrentScreenDpi(out double scaleX, out double scaleY);
            return new Point(logicalPoint.X * scaleX, logicalPoint.Y * scaleY);
        }
        
        /// <summary>
        /// Convert physical pixels to logical (WPF) units
        /// </summary>
        public static Rect PhysicalToLogical(Rect physicalRect)
        {
            GetCurrentScreenDpi(out double scaleX, out double scaleY);
            return new Rect(
                physicalRect.X / scaleX,
                physicalRect.Y / scaleY,
                physicalRect.Width / scaleX,
                physicalRect.Height / scaleY
            );
        }
        
        /// <summary>
        /// Convert logical (WPF) units to physical pixels
        /// </summary>
        public static Rect LogicalToPhysical(Rect logicalRect)
        {
            GetCurrentScreenDpi(out double scaleX, out double scaleY);
            return new Rect(
                logicalRect.X * scaleX,
                logicalRect.Y * scaleY,
                logicalRect.Width * scaleX,
                logicalRect.Height * scaleY
            );
        }
        
        /// <summary>
        /// Get DPI scaling for a specific window handle.
        /// Uses MonitorFromWindow to reliably determine which monitor the window is on,
        /// independent of cursor position.
        /// </summary>
        public static void GetDpiForWindowHandle(IntPtr hwnd, out double scaleX, out double scaleY)
        {
            try
            {
                if (hwnd == IntPtr.Zero)
                {
                    GetCurrentScreenDpi(out scaleX, out scaleY);
                    return;
                }
                
                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                uint dpiX, dpiY;
                GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
                
                scaleX = dpiX / 96.0;
                scaleY = dpiY / 96.0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DpiHelper: Error getting DPI for window handle: {ex.Message}");
                scaleX = 1.0;
                scaleY = 1.0;
            }
        }
        
        /// <summary>
        /// Position a window using physical screen pixel coordinates via Win32 SetWindowPos.
        /// This bypasses WPF's coordinate system which can misinterpret positions on
        /// non-primary monitors with different DPI scaling.
        /// </summary>
        public static void PositionWindowPhysical(IntPtr hwnd, int x, int y, int width, int height,
            IntPtr insertAfter = default, uint extraFlags = 0)
        {
            if (hwnd == IntPtr.Zero)
            {
                Console.WriteLine("DpiHelper: Cannot position window - handle is null");
                return;
            }

            uint flags = SWP_NOACTIVATE | SWP_NOOWNERZORDER | extraFlags;
            if (insertAfter == IntPtr.Zero)
                flags |= SWP_NOZORDER;

            SetWindowPos(hwnd, insertAfter, x, y, width, height, flags);

            // Moving onto a monitor with a different DPI sends WM_DPICHANGED during the call, and
            // WPF then resizes the window to the "suggested" rect (old size scaled by the DPI ratio),
            // so the window no longer matches the requested pixels. The window is on the target
            // monitor now, so a second call applies the exact rect without another DPI change.
            GetWindowRect(hwnd, out RECT rect);
            if (rect.Left != x || rect.Top != y || rect.Right - rect.Left != width || rect.Bottom - rect.Top != height)
            {
                SetWindowPos(hwnd, insertAfter, x, y, width, height, flags);
            }

            // Console.WriteLine($"DpiHelper: Positioned window at physical ({x}, {y}, {width}x{height})");
        }

        /// <summary>
        /// Scale WPF uses to render the given window (device pixels per DIP). Under PerMonitorV2
        /// this follows the monitor the window is on, which is what converts canvas units to the
        /// physical pixels of a capture shown in that window.
        /// </summary>
        public static void GetWindowRenderDpi(Window window, out double scaleX, out double scaleY)
        {
            try
            {
                if (window.Dispatcher.CheckAccess())
                {
                    if (new System.Windows.Interop.WindowInteropHelper(window).Handle != IntPtr.Zero)
                    {
                        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
                        scaleX = dpi.DpiScaleX;
                        scaleY = dpi.DpiScaleY;
                        return;
                    }
                }
                else
                {
                    IntPtr hwnd = window.Dispatcher.Invoke(() => new System.Windows.Interop.WindowInteropHelper(window).Handle);
                    if (hwnd != IntPtr.Zero)
                    {
                        GetDpiForWindowHandle(hwnd, out scaleX, out scaleY);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DpiHelper: Error getting render DPI for window: {ex.Message}");
            }

            // Window not created yet: it will be placed on the selected capture screen
            GetDpiForSelectedScreen(out scaleX, out scaleY);
        }
        
        /// <summary>
        /// Match a saved translation area to the screens connected now. The screen is found by its
        /// device name (the index shifts when monitors are plugged in or reordered), and an area that
        /// no longer lies inside that screen, because the layout or resolution changed since it was
        /// saved, is moved back into it instead of capturing the wrong spot or nothing at all.
        /// </summary>
        public static TranslationAreaInfo FitAreaToScreens(TranslationAreaInfo area, out bool adjusted)
        {
            adjusted = false;
            var screens = Screen.AllScreens;
            if (screens.Length == 0)
                return area;

            var bounds = area.Bounds;
            int centerX = (int)(bounds.X + bounds.Width / 2);
            int centerY = (int)(bounds.Y + bounds.Height / 2);
            var center = new System.Drawing.Point(centerX, centerY);

            Screen? target = null;
            if (!string.IsNullOrEmpty(area.ScreenDevice))
                target = screens.FirstOrDefault(s => string.Equals(s.DeviceName, area.ScreenDevice, StringComparison.OrdinalIgnoreCase));

            // Areas from older versions have no device name: where the area is now is the best guess
            target ??= screens.FirstOrDefault(s => s.Bounds.Contains(center));

            if (target == null)
            {
                int selected = ConfigManager.Instance.GetSelectedScreenIndex();
                target = selected >= 0 && selected < screens.Length ? screens[selected] : (Screen.PrimaryScreen ?? screens[0]);
            }

            var screenBounds = target.Bounds;
            double width = Math.Min(bounds.Width, screenBounds.Width);
            double height = Math.Min(bounds.Height, screenBounds.Height);
            double x = Math.Clamp(bounds.X, screenBounds.Left, screenBounds.Right - width);
            double y = Math.Clamp(bounds.Y, screenBounds.Top, screenBounds.Bottom - height);

            var fitted = new Rect(x, y, width, height);
            adjusted = Math.Abs(x - bounds.X) > 0.5 || Math.Abs(y - bounds.Y) > 0.5 ||
                       Math.Abs(width - bounds.Width) > 0.5 || Math.Abs(height - bounds.Height) > 0.5;
            if (adjusted)
            {
                Console.WriteLine($"DpiHelper: area {bounds} does not fit {target.DeviceName} {screenBounds}; moved to {fitted}");
            }

            GetDpiForScreen(target, out double scaleX, out double scaleY);
            return new TranslationAreaInfo(fitted, Array.IndexOf(screens, target), scaleX, scaleY, target.DeviceName);
        }

        /// <summary>
        /// Place a window in a corner of a screen's work area using physical pixels, so it lands on the
        /// right monitor whatever DPI that monitor (or the primary one) uses. The window's size is given
        /// in DIPs and converted with the target screen's DPI.
        /// </summary>
        public static void PlaceInWorkAreaCorner(Window window, Screen screen, bool right, bool bottom, int marginX, int marginY)
        {
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            GetDpiForScreen(screen, out double scaleX, out double scaleY);

            int width = (int)Math.Round(window.Width * scaleX);
            int height = (int)Math.Round(window.Height * scaleY);
            var work = screen.WorkingArea;

            int x = right ? work.Right - width - marginX : work.Left + marginX;
            int y = bottom ? work.Bottom - height - marginY : work.Top + marginY;
            x = Math.Max(work.Left, x);
            y = Math.Max(work.Top, y);

            PositionWindowPhysical(hwnd, x, y, width, height);
        }

        /// <summary>
        /// Get the physical screen-pixel bounding rectangle of a window via Win32 GetWindowRect.
        /// Returns (left, top, width, height) in physical pixels.
        /// </summary>
        public static (int Left, int Top, int Width, int Height) GetWindowPhysicalRect(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return (0, 0, 0, 0);
            
            GetWindowRect(hwnd, out RECT rect);
            return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        
        /// <summary>
        /// Check if DPI has changed since last cached value
        /// </summary>
        public static bool HasDpiChanged(out double oldScaleX, out double oldScaleY, out double newScaleX, out double newScaleY)
        {
            oldScaleX = _cachedDpiScaleX;
            oldScaleY = _cachedDpiScaleY;
            
            GetCurrentScreenDpi(out newScaleX, out newScaleY);
            
            bool changed = (Math.Abs(oldScaleX - newScaleX) > 0.01) || (Math.Abs(oldScaleY - newScaleY) > 0.01);
            
            if (changed)
            {
                Console.WriteLine($"DpiHelper: DPI changed from {oldScaleX:F2}x/{oldScaleY:F2}y to {newScaleX:F2}x/{newScaleY:F2}y");
            }
            
            return changed;
        }
    }
}