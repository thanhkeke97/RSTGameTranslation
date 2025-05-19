using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using System.Windows.Forms; // For Control.ModifierKeys

namespace RSTGameTranslation
{
    public static class KeyboardShortcuts
    {
        #region Events
        
        public static event EventHandler? StartStopRequested;
        public static event EventHandler? MonitorToggleRequested;
        public static event EventHandler? ChatBoxToggleRequested;
        public static event EventHandler? SettingsToggleRequested;
        public static event EventHandler? LogToggleRequested;
        public static event EventHandler? MainWindowVisibilityToggleRequested;
        
        #endregion
        
        #region Global Keyboard Hook
        
        // For global keyboard hook
        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr hInstance, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hInstance);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr idHook, int nCode, int wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        
        // For window focus checking
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        
        // Reference to our console window handle (set by MainWindow)
        private static IntPtr _consoleWindowHandle = IntPtr.Zero;
        
        // Set up global keyboard hook
        public static void InitializeGlobalHook()
        {
            if (_hookID == IntPtr.Zero) // Only set if not already set
            {
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule!)
                {
                    if (curModule != null)
                    {
                        _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
                        Console.WriteLine("Global keyboard hook initialized");
                    }
                }
            }
        }
        
        // Remove the hook
        public static void CleanupGlobalHook()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
                Console.WriteLine("Global keyboard hook removed");
            }
        }
        
        // Set the console window handle for proper hook handling
        public static void SetConsoleWindowHandle(IntPtr consoleHandle)
        {
            _consoleWindowHandle = consoleHandle;
        }
        
        // Keyboard hook callback
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    
                    // Check modifiers
                    bool isShiftPressed = (Control.ModifierKeys & Keys.Shift) != 0;
                    bool isAltPressed = (Control.ModifierKeys & Keys.Alt) != 0;
                    
                    // Convert the virtual key code to a Key
                    Key key = KeyInterop.KeyFromVirtualKey(vkCode);
                    
                    // Handle global shortcuts (Shift+Alt+S and Shift+Alt+H) regardless of focus
                    if (isShiftPressed && isAltPressed)
                    {
                        if (key == Key.S)
                        {
                            StartStopRequested?.Invoke(null, EventArgs.Empty);
                            return (IntPtr)1; // Prevent further processing
                        }
                        else if (key == Key.H)
                        {
                            MainWindowVisibilityToggleRequested?.Invoke(null, EventArgs.Empty);
                            return (IntPtr)1; // Prevent further processing
                        }
                    }
                    
                    // For other shortcuts, only process if our application is active
                    if (IsOurApplicationActive())
                    {
                        // Check if it's one of our shortcuts with just Shift (M, C, P, L)
                        if (isShiftPressed && !isAltPressed)
                        {
                            if (IsShortcutKey(key, ModifierKeys.Shift))
                            {
                                if (HandleRawKeyDown(key, ModifierKeys.Shift))
                                {
                                    return (IntPtr)1; // Prevent further processing
                                }
                            }
                        }
                    }
                }
                
                // Call the next hook in the chain
                return CallNextHookEx(_hookID, nCode, (int)wParam, lParam);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in keyboard hook: {ex.Message}");
                return CallNextHookEx(_hookID, nCode, (int)wParam, lParam);
            }
        }
        
        // Check if the foreground window belongs to our application process
        private static bool IsOurApplicationActive()
        {
            try
            {
                // Get the foreground window handle
                IntPtr foregroundWindow = GetForegroundWindow();
                
                // Get the process ID for the foreground window
                uint foregroundProcessId;
                GetWindowThreadProcessId(foregroundWindow, out foregroundProcessId);
                
                // Check if it's our process ID
                bool isOurProcessActive = (foregroundProcessId == (uint)Process.GetCurrentProcess().Id);
                
                // Also check if it's our console window
                if (!isOurProcessActive && _consoleWindowHandle != IntPtr.Zero)
                {
                    isOurProcessActive = (foregroundWindow == _consoleWindowHandle);
                }
                
                return isOurProcessActive;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking application focus: {ex.Message}");
                return false;
            }
        }
        
        #endregion
        
        #region Standard Shortcut Handling
        
        // Handle shortcut keys - for regular window event handling
        public static bool HandleKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                // Shift+Alt+S: Start/Stop OCR (global shortcut)
                if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Shift | ModifierKeys.Alt))
                {
                    StartStopRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
                // Shift+Alt+H: Toggle Main Window Visibility (global shortcut)
                else if (e.Key == Key.H && Keyboard.Modifiers == (ModifierKeys.Shift | ModifierKeys.Alt))
                {
                    MainWindowVisibilityToggleRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
                // Shift+M: Toggle Monitor Window
                else if (e.Key == Key.M && Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    MonitorToggleRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
                // Shift+C: Toggle ChatBox
                else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    ChatBoxToggleRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
                // Shift+P: Toggle Settings
                else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    SettingsToggleRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
                // Shift+L: Toggle Log
                else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    LogToggleRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling keyboard shortcut: {ex.Message}");
            }
            
            return false;
        }
        
        // Method to check if a key combination matches our shortcuts
        public static bool IsShortcutKey(Key key, ModifierKeys modifiers)
        {
            if (modifiers != ModifierKeys.Shift)
                return false;
                
            // Check if it's one of our shortcut keys (only M, C, P, L now)
            return key == Key.M || key == Key.C || 
                   key == Key.P || key == Key.L;
        }
        
        // Handle raw key input for global hook
        public static bool HandleRawKeyDown(Key key, ModifierKeys modifiers)
        {
            try
            {
                if (modifiers != ModifierKeys.Shift)
                    return false;
                    
                // Shift+M: Toggle Monitor Window
                if (key == Key.M)
                {
                    MonitorToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                }
                // Shift+C: Toggle ChatBox
                else if (key == Key.C)
                {
                    ChatBoxToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                }
                // Shift+P: Toggle Settings
                else if (key == Key.P)
                {
                    SettingsToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                }
                // Shift+L: Toggle Log
                else if (key == Key.L)
                {
                    LogToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling raw keyboard shortcut: {ex.Message}");
            }
            
            return false;
        }
        
        #endregion
    }
}