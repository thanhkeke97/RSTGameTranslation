using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using System.Windows.Forms; // For Control.ModifierKeys
using System.Threading;
using System.Threading.Tasks;

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
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr hInstance, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hInstance);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr idHook, int nCode, int wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        
        // For window focus checking
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        
        // For getting key state
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        
        // For registering hotkeys
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        
        // Constants for RegisterHotKey
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_NOREPEAT = 0x4000;
        
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_HOTKEY = 0x0312;
        
        // Virtual key codes
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // ALT key
        private const int VK_F = 0x46;    // F key
        private const int VK_G = 0x47;    // G key
        private const int VK_H = 0x48;    // H key
        private const int VK_C = 0x43;    // C key
        private const int VK_L = 0x4C;    // L key
        private const int VK_P = 0x50;    // P key
        
        // Hotkey IDs
        private const int HOTKEY_ID_ALT_G = 1;
        private const int HOTKEY_ID_ALT_H = 2;
        private const int HOTKEY_ID_ALT_F = 3;
        private const int HOTKEY_ID_ALT_C = 4; 
        private const int HOTKEY_ID_ALT_P = 5;  
        private const int HOTKEY_ID_ALT_L = 6; 
        
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        
        // Reference to our window handles
        private static IntPtr _consoleWindowHandle = IntPtr.Zero;
        private static IntPtr _mainWindowHandle = IntPtr.Zero;
        
        // Track the last key press time to prevent multiple triggers
        private static DateTime _lastKeyPressTime = DateTime.MinValue;
        private static int _lastKeyCode = 0;
        private static readonly TimeSpan _keyPressThreshold = TimeSpan.FromMilliseconds(300);
        
        // Key polling system for global hotkeys only
        private static CancellationTokenSource? _pollingCts;
        private static bool _isPolling = false;
        
        // Set up global keyboard hook and hotkeys
        public static void InitializeGlobalHook()
        {
            // Set up low-level keyboard hook
            if (_hookID == IntPtr.Zero) // Only set if not already set
            {
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule!)
                {
                    if (curModule != null)
                    {
                        _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
                        if (_hookID == IntPtr.Zero)
                        {
                            int errorCode = Marshal.GetLastWin32Error();
                            Console.WriteLine($"Failed to set global keyboard hook. Error code: {errorCode}");
                        }
                        else
                        {
                            Console.WriteLine("Global keyboard hook initialized successfully");
                        }
                    }
                }
            }
            
            // Start key polling as a backup method for global shortcuts only
            StartKeyPolling();
        }
        
        // Set the main window handle for hotkey registration
        public static void SetMainWindowHandle(IntPtr mainWindowHandle)
        {
            _mainWindowHandle = mainWindowHandle;
            
            // Register hotkeys if we have a valid window handle
            if (_mainWindowHandle != IntPtr.Zero)
            {
                // Try to register Alt+G as a hotkey
                if (RegisterHotKey(_mainWindowHandle, HOTKEY_ID_ALT_G, MOD_ALT | MOD_NOREPEAT, (uint)VK_G))
                {
                    Console.WriteLine("Registered Alt+G as global hotkey");
                }
                else
                {
                    Console.WriteLine($"Failed to register Alt+G hotkey. Error: {Marshal.GetLastWin32Error()}");
                }
                
                // Try to register Alt+H as a hotkey
                if (RegisterHotKey(_mainWindowHandle, HOTKEY_ID_ALT_H, MOD_ALT | MOD_NOREPEAT, (uint)VK_H))
                {
                    Console.WriteLine("Registered Alt+H as global hotkey");
                }
                else
                {
                    Console.WriteLine($"Failed to register Alt+H hotkey. Error: {Marshal.GetLastWin32Error()}");
                }
                
                // Try to register Alt+F as a hotkey
                if (RegisterHotKey(_mainWindowHandle, HOTKEY_ID_ALT_F, MOD_ALT | MOD_NOREPEAT, (uint)VK_F))
                {
                    Console.WriteLine("Registered Alt+F as global hotkey");
                }
                else
                {
                    Console.WriteLine($"Failed to register Alt+F hotkey. Error: {Marshal.GetLastWin32Error()}");
                }
                
                // Try to register Alt+C as a hotkey (thay thế Shift+C)
                if (RegisterHotKey(_mainWindowHandle, HOTKEY_ID_ALT_C, MOD_ALT | MOD_NOREPEAT, (uint)VK_C))
                {
                    Console.WriteLine("Registered Alt+C as global hotkey");
                }
                else
                {
                    Console.WriteLine($"Failed to register Alt+C hotkey. Error: {Marshal.GetLastWin32Error()}");
                }
                
                // Try to register Alt+P as a hotkey (thay thế Shift+P)
                if (RegisterHotKey(_mainWindowHandle, HOTKEY_ID_ALT_P, MOD_ALT | MOD_NOREPEAT, (uint)VK_P))
                {
                    Console.WriteLine("Registered Alt+P as global hotkey");
                }
                else
                {
                    Console.WriteLine($"Failed to register Alt+P hotkey. Error: {Marshal.GetLastWin32Error()}");
                }
                
                // Try to register Alt+L as a hotkey (thay thế Shift+L)
                if (RegisterHotKey(_mainWindowHandle, HOTKEY_ID_ALT_L, MOD_ALT | MOD_NOREPEAT, (uint)VK_L))
                {
                    Console.WriteLine("Registered Alt+L as global hotkey");
                }
                else
                {
                    Console.WriteLine($"Failed to register Alt+L hotkey. Error: {Marshal.GetLastWin32Error()}");
                }
            }
        }
        
        // Process WM_HOTKEY messages in the main window
        public static bool ProcessHotKey(IntPtr wParam)
        {
            int id = wParam.ToInt32();
            
            switch (id)
            {
                case HOTKEY_ID_ALT_G:
                    Console.WriteLine("Hotkey detected: Alt+G");
                    StartStopRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                    
                case HOTKEY_ID_ALT_H:
                    Console.WriteLine("Hotkey detected: Alt+H");
                    MainWindowVisibilityToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                    
                case HOTKEY_ID_ALT_F:
                    Console.WriteLine("Hotkey detected: Alt+F");
                    MonitorToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                    
                case HOTKEY_ID_ALT_C:
                    Console.WriteLine("Hotkey detected: Alt+C");
                    ChatBoxToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                    
                case HOTKEY_ID_ALT_P:
                    Console.WriteLine("Hotkey detected: Alt+P");
                    SettingsToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                    
                case HOTKEY_ID_ALT_L:
                    Console.WriteLine("Hotkey detected: Alt+L");
                    LogToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;
            }
            
            return false;
        }
        
        // Start polling for key states as a backup method - ONLY for global shortcuts
        private static void StartKeyPolling()
        {
            if (_isPolling)
                return;
                
            _isPolling = true;
            _pollingCts = new CancellationTokenSource();
            
            Task.Run(async () => 
            {
                Console.WriteLine("Key polling started as backup method for global shortcuts");
                
                bool altGWasPressed = false;
                bool altHWasPressed = false;
                bool altFWasPressed = false;
                bool altCWasPressed = false;
                bool altPWasPressed = false;
                bool altLWasPressed = false;
                DateTime lastAltGTime = DateTime.MinValue;
                DateTime lastAltHTime = DateTime.MinValue;
                DateTime lastAltFTime = DateTime.MinValue;
                DateTime lastAltCTime = DateTime.MinValue;
                DateTime lastAltPTime = DateTime.MinValue;
                DateTime lastAltLTime = DateTime.MinValue;
                
                try
                {
                    while (!_pollingCts.Token.IsCancellationRequested)
                    {
                        // Check for Alt key
                        bool isAltPressed = IsKeyPressed(VK_MENU);
                        
                        // Check for Alt+G key combination
                        bool isGPressed = IsKeyPressed(VK_G);
                        
                        if (isAltPressed && isGPressed && !altGWasPressed)
                        {
                            DateTime now = DateTime.Now;
                            if ((now - lastAltGTime).TotalMilliseconds > 500)
                            {
                                Console.WriteLine("Polling detected: Alt+G");
                                
                                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                                {
                                    StartStopRequested?.Invoke(null, EventArgs.Empty);
                                });
                                lastAltGTime = now;
                            }
                        }
                        altGWasPressed = isAltPressed && isGPressed;
                        
                        // Check for Alt+H
                        bool isHPressed = IsKeyPressed(VK_H);
                        
                        if (isAltPressed && isHPressed && !altHWasPressed)
                        {
                            DateTime now = DateTime.Now;
                            if ((now - lastAltHTime).TotalMilliseconds > 500)
                            {
                                Console.WriteLine("Polling detected: Alt+H");
                                
                                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                                {
                                    MainWindowVisibilityToggleRequested?.Invoke(null, EventArgs.Empty);
                                });
                                lastAltHTime = now;
                            }
                        }
                        altHWasPressed = isAltPressed && isHPressed;
                        
                        // Check for Alt+F
                        bool isFPressed = IsKeyPressed(VK_F);
                        
                        if (isAltPressed && isFPressed && !altFWasPressed)
                        {
                            DateTime now = DateTime.Now;
                            if ((now - lastAltFTime).TotalMilliseconds > 500)
                            {
                                Console.WriteLine("Polling detected: Alt+F");
                                
                                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                                {
                                    MonitorToggleRequested?.Invoke(null, EventArgs.Empty);
                                });
                                lastAltFTime = now;
                            }
                        }
                        altFWasPressed = isAltPressed && isFPressed;
                        
                        // Check for Alt+C
                        bool isCPressed = IsKeyPressed(VK_C);
                        
                        if (isAltPressed && isCPressed && !altCWasPressed)
                        {
                            DateTime now = DateTime.Now;
                            if ((now - lastAltCTime).TotalMilliseconds > 500)
                            {
                                Console.WriteLine("Polling detected: Alt+C");
                                
                                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                                {
                                    ChatBoxToggleRequested?.Invoke(null, EventArgs.Empty);
                                });
                                lastAltCTime = now;
                            }
                        }
                        altCWasPressed = isAltPressed && isCPressed;
                        
                        // Check for Alt+P
                        bool isPPressed = IsKeyPressed(VK_P);
                        
                        if (isAltPressed && isPPressed && !altPWasPressed)
                        {
                            DateTime now = DateTime.Now;
                            if ((now - lastAltPTime).TotalMilliseconds > 500)
                            {
                                Console.WriteLine("Polling detected: Alt+P");
                                
                                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                                {
                                    SettingsToggleRequested?.Invoke(null, EventArgs.Empty);
                                });
                                lastAltPTime = now;
                            }
                        }
                        altPWasPressed = isAltPressed && isPPressed;
                        
                        // Check for Alt+L
                        bool isLPressed = IsKeyPressed(VK_L);
                        
                        if (isAltPressed && isLPressed && !altLWasPressed)
                        {
                            DateTime now = DateTime.Now;
                            if ((now - lastAltLTime).TotalMilliseconds > 500)
                            {
                                Console.WriteLine("Polling detected: Alt+L");
                                
                                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                                {
                                    LogToggleRequested?.Invoke(null, EventArgs.Empty);
                                });
                                lastAltLTime = now;
                            }
                        }
                        altLWasPressed = isAltPressed && isLPressed;
                        
                        // Sleep to reduce CPU usage
                        await Task.Delay(30, _pollingCts.Token); 
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in key polling: {ex.Message}");
                }
                finally
                {
                    _isPolling = false;
                    Console.WriteLine("Key polling stopped");
                }
            }, _pollingCts.Token);
        }
        
        // Stop the key polling
        private static void StopKeyPolling()
        {
            if (_isPolling && _pollingCts != null)
            {
                _pollingCts.Cancel();
                _pollingCts.Dispose();
                _pollingCts = null;
            }
        }
        
        // Remove the hook and unregister hotkeys
        public static void CleanupGlobalHook()
        {
            // Stop key polling
            StopKeyPolling();
            
            // Unregister hotkeys
            if (_mainWindowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_mainWindowHandle, HOTKEY_ID_ALT_G);
                UnregisterHotKey(_mainWindowHandle, HOTKEY_ID_ALT_H);
                UnregisterHotKey(_mainWindowHandle, HOTKEY_ID_ALT_F);
                UnregisterHotKey(_mainWindowHandle, HOTKEY_ID_ALT_C);
                UnregisterHotKey(_mainWindowHandle, HOTKEY_ID_ALT_P);
                UnregisterHotKey(_mainWindowHandle, HOTKEY_ID_ALT_L);
            }
            
            // Remove low-level keyboard hook
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
        
        // Direct key state check for more reliable detection
        private static bool IsKeyPressed(int vkCode)
        {
            return (GetAsyncKeyState(vkCode) & 0x8000) != 0;
        }
        
        // Check if we should process this key press (prevents double triggers)
        private static bool ShouldProcessKeyPress(int vkCode)
        {
            DateTime now = DateTime.Now;
            
            // If it's the same key and within threshold time, ignore it
            if (vkCode == _lastKeyCode && (now - _lastKeyPressTime) < _keyPressThreshold)
            {
                return false;
            }
            
            // Update last key press info
            _lastKeyCode = vkCode;
            _lastKeyPressTime = now;
            return true;
        }
        
        // Keyboard hook callback
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    
                    // Only process key down events (both regular and system keys)
                    if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                    {
                        // Global shortcuts - always process regardless of focus
                        bool isAltPressed = IsKeyPressed(VK_MENU);
                        
                        // Check for Alt+G key (Start/Stop) - Always global
                        if (vkCode == VK_G && isAltPressed && ShouldProcessKeyPress(VK_G | 0x1000))
                        {
                            Console.WriteLine("Global shortcut detected: Alt+G");
                            try
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    StartStopRequested?.Invoke(null, EventArgs.Empty);
                                });
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error invoking Alt+G action: {ex.Message}");
                            }
                            return (IntPtr)1; // Prevent further processing
                        }
                        
                        // Check for Alt+H (Toggle Main Window) - Always global
                        if (vkCode == VK_H && isAltPressed && !IsKeyPressed(VK_SHIFT) && !IsKeyPressed(VK_CONTROL) && 
                            ShouldProcessKeyPress(VK_H | 0x1000))
                        {
                            Console.WriteLine("Global shortcut detected: Alt+H");
                            MainWindowVisibilityToggleRequested?.Invoke(null, EventArgs.Empty);
                            return (IntPtr)1; // Prevent further processing
                        }
                        
                        // Check for Alt+F (Toggle Monitor Window) - Always global
                        if (vkCode == VK_F && isAltPressed && !IsKeyPressed(VK_SHIFT) && !IsKeyPressed(VK_CONTROL) && 
                            ShouldProcessKeyPress(VK_F | 0x1000))
                        {
                            Console.WriteLine("Global shortcut detected: Alt+F");
                            try
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    MonitorToggleRequested?.Invoke(null, EventArgs.Empty);
                                });
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error invoking Alt+F action: {ex.Message}");
                            }
                            return (IntPtr)1; // Prevent further processing
                        }
                        
                        // Check for Alt+C (Toggle ChatBox) - Global
                        if (vkCode == VK_C && isAltPressed && !IsKeyPressed(VK_SHIFT) && !IsKeyPressed(VK_CONTROL) && 
                            ShouldProcessKeyPress(VK_C | 0x1000))
                        {
                            Console.WriteLine("Global shortcut detected: Alt+C");
                            try
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    ChatBoxToggleRequested?.Invoke(null, EventArgs.Empty);
                                });
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error invoking Alt+C action: {ex.Message}");
                            }
                            return (IntPtr)1; // Prevent further processing
                        }
                        
                        // Check for Alt+P (Toggle Settings) - Global
                        if (vkCode == VK_P && isAltPressed && !IsKeyPressed(VK_SHIFT) && !IsKeyPressed(VK_CONTROL) && 
                            ShouldProcessKeyPress(VK_P | 0x1000))
                        {
                            Console.WriteLine("Global shortcut detected: Alt+P");
                            try
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    SettingsToggleRequested?.Invoke(null, EventArgs.Empty);
                                });
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error invoking Alt+P action: {ex.Message}");
                            }
                            return (IntPtr)1; // Prevent further processing
                        }
                        
                        // Check for Alt+L (Toggle Log) - Global
                        if (vkCode == VK_L && isAltPressed && !IsKeyPressed(VK_SHIFT) && !IsKeyPressed(VK_CONTROL) && 
                            ShouldProcessKeyPress(VK_L | 0x1000))
                        {
                            Console.WriteLine("Global shortcut detected: Alt+L");
                            try
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    LogToggleRequested?.Invoke(null, EventArgs.Empty);
                                });
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error invoking Alt+L action: {ex.Message}");
                            }
                            return (IntPtr)1; // Prevent further processing
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
                // Alt+G key: Start/Stop OCR (global shortcut)
                if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Alt)
                {
                    StartStopRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
                // Alt+H: Toggle Main Window Visibility (global shortcut)
                else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Alt)
                {
                    MainWindowVisibilityToggleRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
                // Alt+F: Toggle Monitor Window (global shortcut)
                else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Alt)
                {
                    MonitorToggleRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
                
                // Alt+C: Toggle ChatBox (global shortcut)
                else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Alt)
                {
                    ChatBoxToggleRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
                // Alt+P: Toggle Settings (global shortcut)
                else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Alt)
                {
                    SettingsToggleRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
                // Alt+L: Toggle Log (global shortcut)
                else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Alt)
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
            return false;
        }
        
        // Handle raw key input for global hook
        public static bool HandleRawKeyDown(Key key, ModifierKeys modifiers)
        {
            return false;
        }
        
        #endregion
    }
}