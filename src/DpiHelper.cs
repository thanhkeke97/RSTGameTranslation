// DpiHelper.cs - Helper class for DPI scaling operations
using System;
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
        public static void PositionWindowPhysical(IntPtr hwnd, int x, int y, int width, int height)
        {
            if (hwnd == IntPtr.Zero)
            {
                Console.WriteLine("DpiHelper: Cannot position window - handle is null");
                return;
            }
            
            SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            
            Console.WriteLine($"DpiHelper: Positioned window at physical ({x}, {y}, {width}x{height})");
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