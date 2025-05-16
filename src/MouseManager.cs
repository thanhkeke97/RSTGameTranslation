using System;
using System.Runtime.InteropServices;

namespace RSTGameTranslation
{
    public class MouseManager
    {
        private static MouseManager? _instance;
        
        // For system-wide cursor management
        [DllImport("user32.dll")]
        private static extern int ShowCursor(bool bShow);
        
        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
        
        [DllImport("user32.dll")]
        private static extern IntPtr SetCursor(IntPtr hCursor);
        
        [DllImport("user32.dll")]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
        
        // Constants for SystemParametersInfo
        private const uint SPI_SETCURSORS = 0x0057;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;
        
        // Constants for mouse hook
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        
        // Delegate for the hook procedure
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        
        // For storing the hook procedure instance
        private LowLevelMouseProc? _mouseProc;
        private IntPtr _mouseHookHandle = IntPtr.Zero;
        
        // Configuration
        private bool _forceMouseCursorToAlwaysBeVisibleSystemWide = true;
        
        // Singleton pattern
        public static MouseManager Instance 
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MouseManager();
                }
                return _instance;
            }
        }
        
        // Constructor
        private MouseManager()
        {
            // Initialize from config (default to true if not set)
            string forceCursorValue = ConfigManager.Instance.GetValue(
                ConfigManager.FORCE_CURSOR_VISIBLE, "true");
            _forceMouseCursorToAlwaysBeVisibleSystemWide = forceCursorValue.ToLower() == "true";
            
            // Log the configuration
            Console.WriteLine($"MouseManager initialized. Force cursor visible: {_forceMouseCursorToAlwaysBeVisibleSystemWide}");
        }
        
        // Initialize and apply settings
        public void Initialize()
        {
            if (_forceMouseCursorToAlwaysBeVisibleSystemWide)
            {
                // Install a system-wide mouse hook to enforce cursor visibility
                InstallMouseHook();
                Console.WriteLine("Force cursor visibility enabled - using system-wide hook");
            }
        }
        
        // Cleanup resources
        public void Cleanup()
        {
            // Remove mouse hook if it was installed
            if (_mouseHookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookHandle);
                _mouseHookHandle = IntPtr.Zero;
                Console.WriteLine("Mouse hook removed");
            }
        }
        
        // Install a low-level mouse hook to ensure cursor visibility system-wide
        private void InstallMouseHook()
        {
            try
            {
                // Create the mouse procedure delegate - keep it as a class member to prevent garbage collection
                _mouseProc = new LowLevelMouseProc(MouseHookCallback);
                
                // Get current module handle
                IntPtr moduleHandle = GetModuleHandle(null);
                
                // Install the hook
                _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
                
                if (_mouseHookHandle == IntPtr.Zero)
                {
                    Console.WriteLine("Failed to install mouse hook: " + Marshal.GetLastWin32Error());
                }
                else
                {
                    Console.WriteLine("System-wide mouse hook installed successfully");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error installing mouse hook: {ex.Message}");
            }
        }
        
        // Callback for low-level mouse events
        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _forceMouseCursorToAlwaysBeVisibleSystemWide)
            {
                // Load the default arrow cursor
                IntPtr arrowCursor = LoadCursor(IntPtr.Zero, 32512); // IDC_ARROW = 32512
                
                // Set the cursor to the arrow
                SetCursor(arrowCursor);
                
                // Make sure it's visible
                ShowCursor(true);
            }
            
            // Call the next hook in the chain
            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }
        
        // Public method to manually show cursor
        public void ShowMouseCursor()
        {
            ShowCursor(true);
        }
        
        // Property to check status
        public bool ForceVisibilityEnabled => _forceMouseCursorToAlwaysBeVisibleSystemWide;
    }
}