// Tạo file mới: DpiHelper.cs
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
        
        private const int MDT_EFFECTIVE_DPI = 0;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        
        
        private static double _cachedDpiScaleX = -1;
        private static double _cachedDpiScaleY = -1;
        
        
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
                
                
                _cachedDpiScaleX = scaleX;
                _cachedDpiScaleY = scaleY;
                
                Console.WriteLine($"DpiHelper: Got DPI for screen {screen.DeviceName}: {dpiX}/{dpiY} ({scaleX:F2}x/{scaleY:F2}y)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting DPI scaling: {ex.Message}");
              
                scaleX = 1.0;
                scaleY = 1.0;
            }
        }
        
        public static void GetCurrentScreenDpi(out double scaleX, out double scaleY)
        {
            if (_cachedDpiScaleX > 0 && _cachedDpiScaleY > 0)
            {
                scaleX = _cachedDpiScaleX;
                scaleY = _cachedDpiScaleY;
                return;
            }
            
            int selectedScreenIndex = ConfigManager.Instance.GetSelectedScreenIndex();
            var screens = Screen.AllScreens;
            
            if (selectedScreenIndex >= 0 && selectedScreenIndex < screens.Length)
            {
                GetDpiForScreen(screens[selectedScreenIndex], out scaleX, out scaleY);
            }
            else
            {
                GetDpiForScreen(Screen.PrimaryScreen, out scaleX, out scaleY);
            }
        }
        
        public static void SetKnownDpiScale(double dpiScaleX, double dpiScaleY)
        {
            _cachedDpiScaleX = dpiScaleX;
            _cachedDpiScaleY = dpiScaleY;
            Console.WriteLine($"DpiHelper: Set known DPI scale: {dpiScaleX:F2}x, {dpiScaleY:F2}y");
        }
        
        public static Point PhysicalToLogical(Point physicalPoint)
        {
            GetCurrentScreenDpi(out double scaleX, out double scaleY);
            return new Point(physicalPoint.X / scaleX, physicalPoint.Y / scaleY);
        }
        
        public static Point LogicalToPhysical(Point logicalPoint)
        {
            GetCurrentScreenDpi(out double scaleX, out double scaleY);
            return new Point(logicalPoint.X * scaleX, logicalPoint.Y * scaleY);
        }
        
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
    }
}