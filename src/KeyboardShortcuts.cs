using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace RSTGameTranslation
{
    public static class KeyboardShortcuts
    {
        #region Events
        
        public static event EventHandler? StartStopRequested;
        public static event EventHandler? AudioServiceToggleRequested;
        public static event EventHandler? MonitorToggleRequested;
        public static event EventHandler? ChatBoxToggleRequested;
        public static event EventHandler? SettingsToggleRequested;
        public static event EventHandler? LogToggleRequested;
        public static event EventHandler? ClearSelectedAreaRequested;
        public static event EventHandler? ShowAreaRequested;
        // public static event EventHandler? MainWindowVisibilityToggleRequested;
        public static event EventHandler? SelectTranslationRegion;
        public static event EventHandler? ClearAreasRequested;
        public static event EventHandler? RetryTranslationRequested;
        public static event EventHandler? ToggleExcludeRegionsRequested;
        
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
        
        // For getting key state
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        
        // For registering hotkeys
        [DllImport("user32.dll", SetLastError = true)]
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
        
        // Virtual key codes
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // ALT key
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        // Every hotkey-bound function: its config name (as used by ConfigManager.GetHotKey) and the event it raises.
        // The RegisterHotKey id of each entry is its index + 1.
        private static readonly (string Name, Action Raise)[] _hotkeyFunctions =
        {
            ("Start/Stop", () => StartStopRequested?.Invoke(null, EventArgs.Empty)),
            ("Overlay", () => MonitorToggleRequested?.Invoke(null, EventArgs.Empty)),
            ("ChatBox", () => ChatBoxToggleRequested?.Invoke(null, EventArgs.Empty)),
            ("Setting", () => SettingsToggleRequested?.Invoke(null, EventArgs.Empty)),
            ("Log", () => LogToggleRequested?.Invoke(null, EventArgs.Empty)),
            ("Select Area", () => SelectTranslationRegion?.Invoke(null, EventArgs.Empty)),
            ("Clear Areas", () => ClearAreasRequested?.Invoke(null, EventArgs.Empty)),
            ("Clear Selected Area", () => ClearSelectedAreaRequested?.Invoke(null, EventArgs.Empty)),
            ("Show Area", () => ShowAreaRequested?.Invoke(null, EventArgs.Empty)),
            ("Audio Service", () => AudioServiceToggleRequested?.Invoke(null, EventArgs.Empty)),
            ("Swap Languages", () => SwapLanguagesRequested?.Invoke(null, EventArgs.Empty)),
            ("Retry Translation", () => RetryTranslationRequested?.Invoke(null, EventArgs.Empty)),
            ("Select Exclude Region", () => ToggleExcludeRegionsRequested?.Invoke(null, EventArgs.Empty)),
        };

        private static readonly Dictionary<string, int> _keyCodeMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> _keyNameMap = new Dictionary<int, string>();

        // Set while the Settings window is recording a new hotkey, so the keys reach it instead of firing actions
        private static volatile bool _suspended;
        // Replaced as a whole on refresh (never mutated in place) so the polling thread can enumerate it safely
        private static volatile Dictionary<string, (uint modifiers, int vkCode)> _parsedHotkeys = new Dictionary<string, (uint, int)>();

        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        // Window that owns the RegisterHotKey registrations
        private static IntPtr _mainWindowHandle = IntPtr.Zero;

        // Hotkey keys whose key-down was swallowed by the hook (vk -> tick of last key-down); used to ignore
        // auto-repeat and swallow the matching key-up. Entries expire in case a key-up is never delivered
        // (e.g. secure desktop switch), longer than the max keyboard repeat delay of 1s.
        private static readonly Dictionary<int, long> _heldHotkeyKeys = new Dictionary<int, long>();
        private const long HeldKeyExpiryMs = 1500;
        private static readonly object _hotkeyDispatchLock = new object();
        private static readonly Dictionary<string, DateTime> _lastHotkeyDispatchTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan _hotkeyDispatchThreshold = TimeSpan.FromMilliseconds(250);
        
        // Key polling system for global hotkeys only
        private static CancellationTokenSource? _pollingCts;
        private static bool _isPolling = false;

        public static event EventHandler? SwapLanguagesRequested;
        
        static KeyboardShortcuts()
        {
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

            foreach (var kvp in _keyCodeMap)
            {
                _keyNameMap[kvp.Value] = kvp.Key;
            }
        }

        public static IEnumerable<string> FunctionNames => _hotkeyFunctions.Select(f => f.Name);

        // Parse a config string such as "CTRL+SHIFT+G" or "F9". A modifier is required except for F1-F12.
        public static bool TryParseHotkey(string? hotkeyString, out uint modifiers, out int vkCode)
        {
            modifiers = 0;
            vkCode = 0;
            if (string.IsNullOrWhiteSpace(hotkeyString))
                return false;

            string[] parts = hotkeyString.Split('+');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToUpperInvariant())
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
                    default:
                        return false;
                }
            }

            if (!_keyCodeMap.TryGetValue(parts[parts.Length - 1].Trim(), out vkCode))
                return false;

            return modifiers != 0 || IsFunctionKey(vkCode);
        }

        private static bool IsFunctionKey(int vkCode) => vkCode >= 0x70 && vkCode <= 0x7B; // F1-F12

        public static bool AreSameHotkey(string? a, string? b)
        {
            return TryParseHotkey(a, out uint modsA, out int vkA) &&
                   TryParseHotkey(b, out uint modsB, out int vkB) &&
                   modsA == modsB && vkA == vkB;
        }

        public enum HotkeyCaptureResult
        {
            Ok,
            ModifierOnly,   // only Ctrl/Alt/Shift held so far; keep waiting for the main key
            NeedsModifier,  // a plain key that isn't F1-F12
            Unsupported,    // a key we can't map, or the Windows key is held
        }

        // Turn a key press captured in WPF into a config string such as "CTRL+ALT+G"
        public static HotkeyCaptureResult TryBuildHotkey(ModifierKeys modifiers, Key key, out string hotkey)
        {
            hotkey = "";

            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)
                return HotkeyCaptureResult.ModifierOnly;

            if ((modifiers & ModifierKeys.Windows) != 0 || key is Key.LWin or Key.RWin)
                return HotkeyCaptureResult.Unsupported;

            int vkCode = KeyInterop.VirtualKeyFromKey(key);
            if (!_keyNameMap.TryGetValue(vkCode, out string? keyName))
                return HotkeyCaptureResult.Unsupported;

            if (modifiers == ModifierKeys.None && !IsFunctionKey(vkCode))
                return HotkeyCaptureResult.NeedsModifier;

            hotkey = FormatModifiers(modifiers, "+") + keyName;
            return HotkeyCaptureResult.Ok;
        }

        // Modifier prefix in a fixed order, e.g. "CTRL+ALT+" (or "Ctrl + Alt + " for display)
        public static string FormatModifiers(ModifierKeys modifiers, string separator, bool forDisplay = false)
        {
            var sb = new System.Text.StringBuilder();
            if ((modifiers & ModifierKeys.Control) != 0)
                sb.Append(forDisplay ? "Ctrl" : "CTRL").Append(separator);
            if ((modifiers & ModifierKeys.Alt) != 0)
                sb.Append(forDisplay ? "Alt" : "ALT").Append(separator);
            if ((modifiers & ModifierKeys.Shift) != 0)
                sb.Append(forDisplay ? "Shift" : "SHIFT").Append(separator);
            return sb.ToString();
        }

        // Human-friendly form of a config string: "CTRL+ALT+G" -> "Ctrl + Alt + G"
        public static string FormatForDisplay(string? hotkeyString)
        {
            if (!TryParseHotkey(hotkeyString, out uint mods, out int vkCode))
                return hotkeyString ?? "";

            ModifierKeys modifierKeys = ModifierKeys.None;
            if ((mods & MOD_CONTROL) != 0) modifierKeys |= ModifierKeys.Control;
            if ((mods & MOD_ALT) != 0) modifierKeys |= ModifierKeys.Alt;
            if ((mods & MOD_SHIFT) != 0) modifierKeys |= ModifierKeys.Shift;

            string keyName = _keyNameMap[vkCode];
            if (keyName.Length > 1 && !IsFunctionKey(vkCode))
                keyName = char.ToUpperInvariant(keyName[0]) + keyName.Substring(1).ToLowerInvariant();

            return FormatModifiers(modifierKeys, " + ", forDisplay: true) + keyName;
        }

        // Stop handling hotkeys (and release the RegisterHotKey combos) while a new hotkey is being recorded
        public static void SuspendHotkeys()
        {
            if (_suspended)
                return;
            _suspended = true;
            if (_mainWindowHandle != IntPtr.Zero)
            {
                UnregisterAllHotkeys();
            }
        }

        public static void ResumeHotkeys()
        {
            if (!_suspended)
                return;
            _suspended = false;
            RegisterConfiguredHotkeys();
        }

        // Re-read hotkeys from config and re-register them
        public static void ReloadHotkeys()
        {
            RefreshHotkeys();
            RegisterConfiguredHotkeys();
        }

        // Re-read all hotkeys from config
        public static void RefreshHotkeys()
        {
            var hotkeys = new Dictionary<string, (uint, int)>();

            foreach (var (name, _) in _hotkeyFunctions)
            {
                ParseHotkey(hotkeys, name);
            }

            _parsedHotkeys = hotkeys;
        }

        private static void ParseHotkey(Dictionary<string, (uint, int)> hotkeys, string functionName)
        {
            string hotkeyString = ConfigManager.Instance.GetHotKey(functionName);
            if (string.IsNullOrEmpty(hotkeyString))
                return;

            if (TryParseHotkey(hotkeyString, out uint modifiers, out int vkCode))
            {
                hotkeys[functionName] = (modifiers, vkCode);
                Console.WriteLine($"Parsed hotkey for {functionName}: {hotkeyString} -> Modifiers: {modifiers}, VK: {vkCode}");
            }
            else
            {
                Console.WriteLine($"Could not parse hotkey '{hotkeyString}' for function {functionName}");
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

            // Without the hook, fall back to polling key states. When the hook is active polling is not
            // needed: the hook sees every key first, and RegisterHotKey covers windows the hook can't see
            // (e.g. an elevated game in the foreground).
            if (_hookID == IntPtr.Zero)
            {
                StartKeyPolling();
            }
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
            if (_mainWindowHandle == IntPtr.Zero || _suspended)
                return;

            UnregisterAllHotkeys();

            for (int i = 0; i < _hotkeyFunctions.Length; i++)
            {
                RegisterFunctionHotkey(_hotkeyFunctions[i].Name, i + 1);
            }
        }

        private static void UnregisterAllHotkeys()
        {
            for (int i = 0; i < _hotkeyFunctions.Length; i++)
            {
                UnregisterHotKey(_mainWindowHandle, i + 1);
            }
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
        
        private static bool TryReserveHotkeyDispatch(string function)
        {
            DateTime now = DateTime.UtcNow;

            lock (_hotkeyDispatchLock)
            {
                if (_lastHotkeyDispatchTimes.TryGetValue(function, out DateTime lastDispatch) &&
                    (now - lastDispatch) < _hotkeyDispatchThreshold)
                {
                    Console.WriteLine($"Hotkey duplicate suppressed: {function}");
                    return false;
                }

                _lastHotkeyDispatchTimes[function] = now;
                return true;
            }
        }

        private static bool InvokeHotkeyAction(string function)
        {
            if (_suspended || !ConfigManager.Instance.IsHotKeyEnabled())
            {
                return false;
            }

            int index = Array.FindIndex(_hotkeyFunctions, f => f.Name == function);
            if (index < 0)
            {
                return false;
            }

            if (TryReserveHotkeyDispatch(function))
            {
                Console.WriteLine($"Hotkey detected: {function}");
                _hotkeyFunctions[index].Raise();
            }
            return true;
        }

        // Process WM_HOTKEY messages in the main window
        public static bool ProcessHotKey(IntPtr wParam)
        {
            int index = wParam.ToInt32() - 1;
            return index >= 0 && index < _hotkeyFunctions.Length && InvokeHotkeyAction(_hotkeyFunctions[index].Name);
        }

        // Run the action bound to a hotkey function name
        public static bool ProcessHandleHotKey(string function)
        {
            return InvokeHotkeyAction(function);
        }
        
        // Start polling for key states as a backup method - ONLY for global shortcuts
        private static void StartKeyPolling()
        {
            if (_isPolling)
                return;

            _isPolling = true;
            _pollingCts = new CancellationTokenSource();
            CancellationToken token = _pollingCts.Token;

            Task.Run(async () =>
            {
                Console.WriteLine("Key polling started as backup method for global shortcuts");

                // Tracks whether each hotkey was held on the previous tick, so we only fire on the press edge
                Dictionary<string, bool> keyStates = new Dictionary<string, bool>();

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        // Polling keeps running while hotkeys are disabled so re-enabling them works without a restart
                        if (!_suspended && ConfigManager.Instance.IsHotKeyEnabled())
                        {
                            uint? currentModifiers = GetCurrentModifiers();

                            foreach (var kvp in _parsedHotkeys)
                            {
                                string function = kvp.Key;
                                var (modifiers, vkCode) = kvp.Value;

                                bool isHotkeyPressed = currentModifiers == modifiers && IsKeyPressed(vkCode);
                                keyStates.TryGetValue(function, out bool wasPressed);

                                // If the hotkey state changed from not pressed to pressed
                                if (isHotkeyPressed && !wasPressed)
                                {
                                    Console.WriteLine($"Polling detected: {function} hotkey");
                                    DispatchHotkey(function);
                                }

                                keyStates[function] = isHotkeyPressed;
                            }
                        }

                        // Sleep to reduce CPU usage
                        await Task.Delay(30, token);
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
            }, token);
        }

        // Run the hotkey action asynchronously on the UI thread. Never block the caller: the low-level
        // hook is silently removed by Windows if its callback exceeds LowLevelHooksTimeout.
        private static void DispatchHotkey(string function)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        ProcessHandleHotKey(function);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error invoking {function} action: {ex.Message}");
                    }
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error dispatching {function} action: {ex.Message}");
            }
        }

        // Currently held Alt/Ctrl/Shift as MOD_* flags, or null when a Windows key is held
        // (Win combos are never configured, so they must not trigger a hotkey)
        private static uint? GetCurrentModifiers()
        {
            if (IsKeyPressed(VK_LWIN) || IsKeyPressed(VK_RWIN))
                return null;

            uint modifiers = 0;
            if (IsKeyPressed(VK_MENU))
                modifiers |= MOD_ALT;
            if (IsKeyPressed(VK_CONTROL))
                modifiers |= MOD_CONTROL;
            if (IsKeyPressed(VK_SHIFT))
                modifiers |= MOD_SHIFT;
            return modifiers;
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
                UnregisterAllHotkeys();
            }
            
            // Remove low-level keyboard hook
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
                Console.WriteLine("Global keyboard hook removed");
            }
        }
        
        // Direct key state check for more reliable detection
        private static bool IsKeyPressed(int vkCode)
        {
            return (GetAsyncKeyState(vkCode) & 0x8000) != 0;
        }
        
        // Keyboard hook callback
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    bool isKeyDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
                    bool isKeyUp = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;

                    if (isKeyUp && _heldHotkeyKeys.Remove(vkCode))
                    {
                        // The key-down was swallowed, so swallow the key-up too to avoid an orphan key-up in the game
                        return (IntPtr)1;
                    }

                    if (isKeyDown)
                    {
                        // Auto-repeat of a hotkey we already handled: keep swallowing, but don't fire again
                        long now = Environment.TickCount64;
                        if (_heldHotkeyKeys.TryGetValue(vkCode, out long lastDown))
                        {
                            if (now - lastDown < HeldKeyExpiryMs)
                            {
                                _heldHotkeyKeys[vkCode] = now;
                                return (IntPtr)1;
                            }
                            _heldHotkeyKeys.Remove(vkCode);
                        }

                        if (!_suspended && ConfigManager.Instance.IsHotKeyEnabled())
                        {
                            uint? currentModifiers = GetCurrentModifiers();

                            foreach (var kvp in _parsedHotkeys)
                            {
                                var (modifiers, keyCode) = kvp.Value;

                                // Modifiers must match exactly, so e.g. Ctrl+Alt+G does not trigger Alt+G
                                if (vkCode == keyCode && currentModifiers == modifiers)
                                {
                                    Console.WriteLine($"Global shortcut detected: {kvp.Key}");
                                    _heldHotkeyKeys[vkCode] = now;
                                    DispatchHotkey(kvp.Key);
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

        #endregion
        
        #region Standard Shortcut Handling
        
        // Handle shortcut keys - for regular window event handling
        public static bool HandleKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (!ConfigManager.Instance.IsHotKeyEnabled())
            {
                return false;
            }
            try
            {
                // Convert WPF key to virtual key code
                // With Alt held, WPF reports Key.System and puts the real key in SystemKey
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                int vkCode = KeyInterop.VirtualKeyFromKey(key);
                
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
                        if (ProcessHandleHotKey(function))
                        {
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