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
        public static event EventHandler? ClearSelectedAreaRequested;
        public static event EventHandler? ShowAreaRequested;
        // public static event EventHandler? MainWindowVisibilityToggleRequested;
        public static event EventHandler? SelectTranslationRegion;
        public static event EventHandler? ClearAreasRequested;
        
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
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        
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
        private const int VK_Q = 0x51;    // Q key
        private const int VK_1 = 0x31;    // 1 key
        private const int VK_2 = 0x32;    // 2 key
        private const int VK_3 = 0x33;    // 3 key
        private const int VK_4 = 0x34;    // 4 key
        private const int VK_5 = 0x35;    // 5 key
        
        // Hotkey IDs
        private const int HOTKEY_ID_START_STOP = 1;
        private const int HOTKEY_ID_OVERLAY = 2;
        private const int HOTKEY_ID_CHATBOX = 3;
        private const int HOTKEY_ID_SETTING = 4;
        private const int HOTKEY_ID_LOG = 5;
        private const int HOTKEY_ID_SELECT_AREA = 6;
        private const int HOTKEY_ID_CLEAR_AREAS = 7;
        private const int HOTKEY_ID_AREA_1 = 8;
        private const int HOTKEY_ID_AREA_2 = 9;
        private const int HOTKEY_ID_AREA_3 = 10;
        private const int HOTKEY_ID_AREA_4 = 11;
        private const int HOTKEY_ID_AREA_5 = 12;
        private const int HOTKEY_ID_CLEAR_SELECTED_AREA = 13;
        private const int HOTKEY_ID_SHOW_AREA = 14;

        private static readonly Dictionary<string, EventHandler?> _functionHandlers = new Dictionary<string, EventHandler?>();
        private static readonly Dictionary<string, int> _keyCodeMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, (uint modifiers, int vkCode)> _parsedHotkeys = new Dictionary<string, (uint, int)>();
        
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

        // Event for Multi selection area
        public static event EventHandler? SelectArea1Requested;
        public static event EventHandler? SelectArea2Requested;
        public static event EventHandler? SelectArea3Requested;
        public static event EventHandler? SelectArea4Requested;
        public static event EventHandler? SelectArea5Requested;
        
        static KeyboardShortcuts()
        {
            // Initialize function handlers
            _functionHandlers["Start/Stop"] = StartStopRequested;
            _functionHandlers["Overlay"] = MonitorToggleRequested;
            _functionHandlers["ChatBox"] = ChatBoxToggleRequested;
            _functionHandlers["Setting"] = SettingsToggleRequested;
            _functionHandlers["Log"] = LogToggleRequested;
            _functionHandlers["Select Area"] = SelectTranslationRegion;
            _functionHandlers["Clear Areas"] = ClearAreasRequested;
            _functionHandlers["Clear Selected Area"] = ClearSelectedAreaRequested;
            _functionHandlers["Show Area"] = ShowAreaRequested;
            _functionHandlers["Area 1"] = SelectArea1Requested;
            _functionHandlers["Area 2"] = SelectArea2Requested;
            _functionHandlers["Area 3"] = SelectArea3Requested;
            _functionHandlers["Area 4"] = SelectArea4Requested;
            _functionHandlers["Area 5"] = SelectArea5Requested;
            
            // Initialize key code map
            for (int i = 0; i < 26; i++) // A-Z
            {
                char key = (char)('A' + i);
                _keyCodeMap[key.ToString()] = 0x41 + i;
            }
            
            for (int i = 0; i <= 9; i++) // 0-9
            {
                _keyCodeMap[i.ToString()] = 0x30 + i;
            }
            
            // Function keys
            for (int i = 1; i <= 12; i++)
            {
                _keyCodeMap[$"F{i}"] = 0x70 + i - 1;
            }
            
            // Other common keys
            _keyCodeMap["SPACE"] = 0x20;
            _keyCodeMap["TAB"] = 0x09;
            _keyCodeMap["ENTER"] = 0x0D;
            _keyCodeMap["ESCAPE"] = 0x1B;
            _keyCodeMap["BACKSPACE"] = 0x08;
            _keyCodeMap["INSERT"] = 0x2D;
            _keyCodeMap["DELETE"] = 0x2E;
            _keyCodeMap["HOME"] = 0x24;
            _keyCodeMap["END"] = 0x23;
            _keyCodeMap["PAGEUP"] = 0x21;
            _keyCodeMap["PAGEDOWN"] = 0x22;
            _keyCodeMap["LEFT"] = 0x25;
            _keyCodeMap["UP"] = 0x26;
            _keyCodeMap["RIGHT"] = 0x27;
            _keyCodeMap["DOWN"] = 0x28;
        }

        // Thêm phương thức RefreshHotkeys
        public static void RefreshHotkeys()
        {
            _parsedHotkeys.Clear();
            
            // Parse all hotkeys from config
            ParseHotkey("Start/Stop");
            ParseHotkey("Overlay");
            ParseHotkey("ChatBox");
            ParseHotkey("Setting");
            ParseHotkey("Log");
            ParseHotkey("Select Area");
            ParseHotkey("Clear Areas");
            ParseHotkey("Clear Selected Area");
            ParseHotkey("Show Area");
            ParseHotkey("Area 1");
            ParseHotkey("Area 2");
            ParseHotkey("Area 3");
            ParseHotkey("Area 4");
            ParseHotkey("Area 5");
        }

        private static void ParseHotkey(string functionName)
        {
            string hotkeyString = ConfigManager.Instance.GetHotKey(functionName);
            if (string.IsNullOrEmpty(hotkeyString))
                return;
                
            string[] parts = hotkeyString.Split('+');
            if (parts.Length < 2)
                return;
                
            uint modifiers = 0;
            string keyName = parts[parts.Length - 1].Trim().ToUpper();
            
            // Parse modifiers
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string mod = parts[i].Trim().ToUpper();
                switch (mod)
                {
                    case "ALT":
                        modifiers |= MOD_ALT;
                        break;
                    case "CTRL":
                        modifiers |= MOD_CONTROL;
                        break;
                    case "SHIFT":
                        modifiers |= MOD_SHIFT;
                        break;
                }
            }
            
            // Get key code
            if (_keyCodeMap.TryGetValue(keyName, out int vkCode))
            {
                _parsedHotkeys[functionName] = (modifiers, vkCode);
                Console.WriteLine($"Parsed hotkey for {functionName}: {hotkeyString} -> Modifiers: {modifiers}, VK: {vkCode}");
            }
            else
            {
                Console.WriteLine($"Could not parse key name: {keyName} for function {functionName}");
            }
        }

        // Set up global keyboard hook and hotkeys
        public static void InitializeGlobalHook()
        {
            // Parse all hotkeys from config
            RefreshHotkeys();
            
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
                RegisterConfiguredHotkeys();
            }
        }

        private static void RegisterConfiguredHotkeys()
        {
            if (_mainWindowHandle == IntPtr.Zero)
                return;
                
            // Unregister any existing hotkeys first
            for (int i = 1; i <= 12; i++)
            {
                UnregisterHotKey(_mainWindowHandle, i);
            }
            
            // Register hotkeys from config
            RegisterFunctionHotkey("Start/Stop", HOTKEY_ID_START_STOP);
            RegisterFunctionHotkey("Overlay", HOTKEY_ID_OVERLAY);
            RegisterFunctionHotkey("ChatBox", HOTKEY_ID_CHATBOX);
            RegisterFunctionHotkey("Setting", HOTKEY_ID_SETTING);
            RegisterFunctionHotkey("Log", HOTKEY_ID_LOG);
            RegisterFunctionHotkey("Select Area", HOTKEY_ID_SELECT_AREA);
            RegisterFunctionHotkey("Clear Areas", HOTKEY_ID_CLEAR_AREAS);
            RegisterFunctionHotkey("Clear Selected Area", HOTKEY_ID_CLEAR_SELECTED_AREA);
            RegisterFunctionHotkey("Show Area", HOTKEY_ID_SHOW_AREA);
            RegisterFunctionHotkey("Area 1", HOTKEY_ID_AREA_1);
            RegisterFunctionHotkey("Area 2", HOTKEY_ID_AREA_2);
            RegisterFunctionHotkey("Area 3", HOTKEY_ID_AREA_3);
            RegisterFunctionHotkey("Area 4", HOTKEY_ID_AREA_4);
            RegisterFunctionHotkey("Area 5", HOTKEY_ID_AREA_5);
        }

        private static void RegisterFunctionHotkey(string functionName, int hotkeyId)
        {
            if (_parsedHotkeys.TryGetValue(functionName, out var hotkeyInfo))
            {
                uint modifiers = hotkeyInfo.modifiers | MOD_NOREPEAT;
                uint vkCode = (uint)hotkeyInfo.vkCode;
                
                if (RegisterHotKey(_mainWindowHandle, hotkeyId, modifiers, vkCode))
                {
                    Console.WriteLine($"Registered {functionName} hotkey successfully");
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"Failed to register {functionName} hotkey. Error: {error}");
                }
            }
        }
        
        // Process WM_HOTKEY messages in the main window
        public static bool ProcessHotKey(IntPtr wParam)
        {
            int id = wParam.ToInt32();

            switch (id)
            {
                case HOTKEY_ID_START_STOP:
                    Console.WriteLine("Hotkey detected: Start/Stop");
                    StartStopRequested?.Invoke(null, EventArgs.Empty);
                    return true;

                case HOTKEY_ID_OVERLAY:
                    Console.WriteLine("Hotkey detected: Overlay");
                    MonitorToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;

                case HOTKEY_ID_CHATBOX:
                    Console.WriteLine("Hotkey detected: ChatBox");
                    ChatBoxToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;

                case HOTKEY_ID_SETTING:
                    Console.WriteLine("Hotkey detected: Setting");
                    SettingsToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;

                case HOTKEY_ID_LOG:
                    Console.WriteLine("Hotkey detected: Log");
                    LogToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;

                case HOTKEY_ID_SELECT_AREA:
                    Console.WriteLine("Hotkey detected: Select Area");
                    SelectTranslationRegion?.Invoke(null, EventArgs.Empty);
                    return true;
                    
                case HOTKEY_ID_CLEAR_AREAS:
                    Console.WriteLine("Hotkey detected: Clear Areas");
                    ClearAreasRequested?.Invoke(null, EventArgs.Empty);
                    return true;

                case HOTKEY_ID_CLEAR_SELECTED_AREA:
                    Console.WriteLine("Hotkey detected: Clear Selected Areas");
                    ClearSelectedAreaRequested?.Invoke(null, EventArgs.Empty);
                    return true;

                case HOTKEY_ID_SHOW_AREA:
                    Console.WriteLine("Hotkey detected: Show Area");
                    ShowAreaRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                    
                case HOTKEY_ID_AREA_1:
                    Console.WriteLine("Hotkey detected: Area 1");
                    SelectArea1Requested?.Invoke(null, EventArgs.Empty);
                    return true;
                    
                case HOTKEY_ID_AREA_2:
                    Console.WriteLine("Hotkey detected: Area 2");
                    SelectArea2Requested?.Invoke(null, EventArgs.Empty);
                    return true;
                    
                case HOTKEY_ID_AREA_3:
                    Console.WriteLine("Hotkey detected: Area 3");
                    SelectArea3Requested?.Invoke(null, EventArgs.Empty);
                    return true;
                    
                case HOTKEY_ID_AREA_4:
                    Console.WriteLine("Hotkey detected: Area 4");
                    SelectArea4Requested?.Invoke(null, EventArgs.Empty);
                    return true;
                    
                case HOTKEY_ID_AREA_5:
                    Console.WriteLine("Hotkey detected: Area 5");
                    SelectArea5Requested?.Invoke(null, EventArgs.Empty);
                    return true;
            }
            
            return false;
        }
        
        // Process WM_HOTKEY messages in the main window
        public static void ProcessHandleHotKey(string function)
        {
            if (function == "Start/Stop")
            {
                Console.WriteLine("Hotkey detected: Start/Stop");
                StartStopRequested?.Invoke(null, EventArgs.Empty);
            }
            else if (function == "Overlay")
            {
                Console.WriteLine("Hotkey detected: Overlay");
                MonitorToggleRequested?.Invoke(null, EventArgs.Empty);
            }
            else if (function == "ChatBox")
            {
                Console.WriteLine("Hotkey detected: ChatBox");
                ChatBoxToggleRequested?.Invoke(null, EventArgs.Empty);
            }
            else if (function == "Setting")
            {
                Console.WriteLine("Hotkey detected: Setting");
                SettingsToggleRequested?.Invoke(null, EventArgs.Empty);
            }
            else if (function == "Log")
            {
                Console.WriteLine("Hotkey detected: Log");
                LogToggleRequested?.Invoke(null, EventArgs.Empty);
            }
            else if (function == "Select Area")
            {
                Console.WriteLine("Hotkey detected: Select Area");
                SelectTranslationRegion?.Invoke(null, EventArgs.Empty);
            }
            else if (function == "Clear Areas")
            {
                Console.WriteLine("Hotkey detected: Clear Areas");
                ClearAreasRequested?.Invoke(null, EventArgs.Empty);
            }
            else if (function == "Clear Selected Area")
            {
                Console.WriteLine("Hotkey detected: Clear Selected Area");
                ClearSelectedAreaRequested?.Invoke(null, EventArgs.Empty);
            }
            else if (function == "Show Area")
            {
                Console.WriteLine("Hotkey detected: Show Area");
                ShowAreaRequested?.Invoke(null, EventArgs.Empty);
            }
            else if (function == "Area 1")
            {
                Console.WriteLine("Hotkey detected: Area 1");
                SelectArea1Requested?.Invoke(null, EventArgs.Empty);
            }
            else if (function == "Area 2")
            {
                Console.WriteLine("Hotkey detected: Area 2");
                SelectArea2Requested?.Invoke(null, EventArgs.Empty);
            }
            else if (function == "Area 3")
            {
                Console.WriteLine("Hotkey detected: Area 3");
                SelectArea3Requested?.Invoke(null, EventArgs.Empty);
            }
            else if (function == "Area 4")
            {
                Console.WriteLine("Hotkey detected: Area 4");
                SelectArea4Requested?.Invoke(null, EventArgs.Empty);
            }
            else if (function == "Area 5")
            {
                Console.WriteLine("Hotkey detected: Area 5");
                SelectArea5Requested?.Invoke(null, EventArgs.Empty);
            }

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

                // Dictionary to track key states
                Dictionary<string, bool> keyStates = new Dictionary<string, bool>();
                Dictionary<string, DateTime> lastTriggerTimes = new Dictionary<string, DateTime>();

                // Initialize dictionaries for all functions
                foreach (string function in _parsedHotkeys.Keys)
                {
                    keyStates[function] = false;
                    lastTriggerTimes[function] = DateTime.MinValue;
                }

                try
                {
                    while (!_pollingCts.Token.IsCancellationRequested)
                    {
                        foreach (var kvp in _parsedHotkeys)
                        {
                            string function = kvp.Key;
                            var (modifiers, vkCode) = kvp.Value;

                            // Check if all required modifiers are pressed
                            bool modifiersPressed = true;
                            if ((modifiers & MOD_ALT) != 0 && !IsKeyPressed(VK_MENU))
                                modifiersPressed = false;
                            if ((modifiers & MOD_CONTROL) != 0 && !IsKeyPressed(VK_CONTROL))
                                modifiersPressed = false;
                            if ((modifiers & MOD_SHIFT) != 0 && !IsKeyPressed(VK_SHIFT))
                                modifiersPressed = false;

                            // Check if the key is pressed
                            bool isKeyPressed = IsKeyPressed(vkCode);

                            // Check if the hotkey is pressed
                            bool isHotkeyPressed = modifiersPressed && isKeyPressed;

                            // If the hotkey state changed from not pressed to pressed
                            if (isHotkeyPressed && !keyStates[function])
                            {
                                DateTime now = DateTime.Now;
                                if ((now - lastTriggerTimes[function]).TotalMilliseconds > 500)
                                {
                                    Console.WriteLine($"Polling detected: {function} hotkey");

                                    // Trigger the event on the UI thread
                                    if (_functionHandlers.TryGetValue(function, out var handler) && handler != null)
                                    {
                                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            handler.Invoke(null, EventArgs.Empty);
                                        });
                                    }

                                    lastTriggerTimes[function] = now;
                                }
                            }

                            // Update key state
                            keyStates[function] = isHotkeyPressed;
                        }

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
                for (int i = 1; i <= 12; i++)
                {
                    UnregisterHotKey(_mainWindowHandle, i);
                }
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
                        // Check for all configured hotkeys
                        foreach (var kvp in _parsedHotkeys)
                        {
                            string function = kvp.Key;
                            var (modifiers, keyCode) = kvp.Value;
                            
                            if (vkCode == keyCode && ShouldProcessKeyPress(keyCode | 0x1000))
                            {
                                // Check if all required modifiers are pressed
                                bool modifiersMatch = true;
                                if ((modifiers & MOD_ALT) != 0 && !IsKeyPressed(VK_MENU))
                                    modifiersMatch = false;
                                if ((modifiers & MOD_CONTROL) != 0 && !IsKeyPressed(VK_CONTROL))
                                    modifiersMatch = false;
                                if ((modifiers & MOD_SHIFT) != 0 && !IsKeyPressed(VK_SHIFT))
                                    modifiersMatch = false;
                                    
                                if (modifiersMatch)
                                {
                                    Console.WriteLine($"Global shortcut detected: {function}");
                                    ProcessHandleHotKey(function);
                                    
                                    // Invoke the associated event handler
                                    if (_functionHandlers.TryGetValue(function, out var handler) && handler != null)
                                    {
                                        try
                                        {
                                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                            {
                                                handler.Invoke(null, EventArgs.Empty);
                                            });
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Error invoking {function} action: {ex.Message}");
                                        }
                                    }
                                    
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
                // Convert WPF key to virtual key code
                int vkCode = KeyInterop.VirtualKeyFromKey(e.Key);
                
                // Check modifiers
                uint modifiers = 0;
                if ((e.KeyboardDevice.Modifiers & ModifierKeys.Alt) != 0)
                    modifiers |= MOD_ALT;
                if ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0)
                    modifiers |= MOD_CONTROL;
                if ((e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0)
                    modifiers |= MOD_SHIFT;
                    
                // Check if it matches any of our configured hotkeys
                foreach (var kvp in _parsedHotkeys)
                {
                    string function = kvp.Key;
                    var (configModifiers, configVkCode) = kvp.Value;
                    
                    if (vkCode == configVkCode && modifiers == configModifiers)
                    {
                        // Invoke the associated event handler
                        if (_functionHandlers.TryGetValue(function, out var handler) && handler != null)
                        {
                            handler.Invoke(null, EventArgs.Empty);
                            e.Handled = true;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling keyboard shortcut: {ex.Message}");
            }
            
            return false;
        }
        
        #endregion
    }
}