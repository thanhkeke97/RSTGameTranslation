using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RSTGameTranslation
{
    // Gamepad shortcuts read through XInput (Xbox controllers, and controllers mapped to XInput by Steam Input/DS4Windows).
    // A button press can't be hidden from the game, so a shortcut is always a combination of at least two buttons
    // (e.g. "BACK+A"). Each combo runs the same hotkey function as its keyboard counterpart in KeyboardShortcuts.
    public static class GamepadShortcuts
    {
        #region XInput

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

        private const uint ERROR_SUCCESS = 0;
        private const int MaxControllers = 4;

        // Triggers are analog; count them as pressed past half travel so resting a finger on them doesn't count
        private const byte TriggerThreshold = 128;

        #endregion

        // Button bits: the XInput wButtons flags, plus two extra bits for the triggers
        private static readonly (string Name, string Display, uint Mask)[] _buttons =
        {
            ("LB", "LB", 0x0100),
            ("RB", "RB", 0x0200),
            ("LT", "LT", 0x10000),
            ("RT", "RT", 0x20000),
            ("BACK", "Back", 0x0020),
            ("START", "Start", 0x0010),
            ("LS", "LS", 0x0040),
            ("RS", "RS", 0x0080),
            ("DPAD_UP", "D-Pad ↑", 0x0001),
            ("DPAD_DOWN", "D-Pad ↓", 0x0002),
            ("DPAD_LEFT", "D-Pad ←", 0x0004),
            ("DPAD_RIGHT", "D-Pad →", 0x0008),
            ("A", "A", 0x1000),
            ("B", "B", 0x2000),
            ("X", "X", 0x4000),
            ("Y", "Y", 0x8000),
        };

        private const uint LT_MASK = 0x10000;
        private const uint RT_MASK = 0x20000;

        public const int MinComboButtons = 2;

        // Hotkey functions that can get a gamepad combo. Functions that need the mouse afterwards (selecting areas)
        // or open windows meant for mouse/keyboard (Settings, Log) are left out, as is destructive Clear Areas.
        private static readonly HashSet<string> _supportedFunctions = new HashSet<string>
        {
            "Start/Stop",
            "Overlay",
            "ChatBox",
            "Show Area",
            "Audio Service",
            "Swap Languages",
            "Retry Translation",
        };

        public static bool IsSupportedFunction(string function) => _supportedFunctions.Contains(function);

        private const int PollIntervalMs = 20;
        private const int IdlePollIntervalMs = 500;
        // XInputGetState on an empty slot is slow, so empty slots are only re-checked this often
        private const long DisconnectedRecheckMs = 2000;

        // Replaced as a whole on refresh (never mutated in place) so the polling thread can enumerate it safely
        private static volatile Dictionary<string, uint> _parsedCombos = new Dictionary<string, uint>();

        private static readonly uint[] _previousButtons = new uint[MaxControllers];
        private static readonly bool[] _connected = new bool[MaxControllers];
        private static readonly long[] _nextConnectCheck = new long[MaxControllers];

        private static CancellationTokenSource? _pollingCts;

        // Recording state for the Settings window: buttons pressed so far, and whether we're waiting for buttons
        // that were already held when recording started to be released
        private static volatile bool _capturing;
        private static uint _captureMask;
        private static bool _captureWaitForRelease;

        // Raised on the UI thread while recording: the buttons held so far, then the final combination once all
        // buttons are released
        public static event Action<uint>? CaptureProgress;
        public static event Action<uint>? CaptureCompleted;

        // Raised on the UI thread when the number of connected controllers changes
        public static event EventHandler? ConnectionChanged;

        public static bool IsAnyControllerConnected
        {
            get
            {
                lock (_connected)
                {
                    return Array.IndexOf(_connected, true) >= 0;
                }
            }
        }

        #region Combo strings

        public static int CountButtons(uint mask) => System.Numerics.BitOperations.PopCount(mask);

        // Parse a config string such as "BACK+A" into a button mask. At least two buttons are required.
        public static bool TryParseCombo(string? comboString, out uint mask)
        {
            mask = 0;
            if (string.IsNullOrWhiteSpace(comboString))
                return false;

            foreach (string part in comboString.Split('+'))
            {
                string name = part.Trim();
                int index = Array.FindIndex(_buttons, b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                    return false;
                mask |= _buttons[index].Mask;
            }

            return CountButtons(mask) >= MinComboButtons;
        }

        // Config form of a button mask in a fixed order, e.g. "BACK+A"
        public static string FormatCombo(uint mask) => JoinButtons(mask, b => b.Name, "+");

        // Human-friendly form of a button mask, e.g. "Back + A"
        public static string FormatMaskForDisplay(uint mask) => JoinButtons(mask, b => b.Display, " + ");

        // Human-friendly form of a config string: "BACK+A" -> "Back + A"
        public static string FormatForDisplay(string? comboString)
        {
            return TryParseCombo(comboString, out uint mask) ? FormatMaskForDisplay(mask) : comboString ?? "";
        }

        public static bool AreSameCombo(string? a, string? b)
        {
            return TryParseCombo(a, out uint maskA) && TryParseCombo(b, out uint maskB) && maskA == maskB;
        }

        private static string JoinButtons(uint mask, Func<(string Name, string Display, uint Mask), string> selector, string separator)
        {
            return string.Join(separator, _buttons.Where(b => (mask & b.Mask) != 0).Select(selector));
        }

        #endregion

        // Re-read all gamepad combos from config
        public static void RefreshCombos()
        {
            var combos = new Dictionary<string, uint>();

            foreach (string function in _supportedFunctions)
            {
                string comboString = ConfigManager.Instance.GetGamepadHotKey(function);
                if (string.IsNullOrEmpty(comboString))
                    continue;

                if (TryParseCombo(comboString, out uint mask))
                {
                    combos[function] = mask;
                }
                else
                {
                    Console.WriteLine($"Could not parse gamepad hotkey '{comboString}' for function {function}");
                }
            }

            _parsedCombos = combos;
        }

        // Read combos from config and start the polling thread
        public static void Initialize()
        {
            RefreshCombos();

            if (_pollingCts != null)
                return;

            _pollingCts = new CancellationTokenSource();
            CancellationToken token = _pollingCts.Token;

            Task.Run(async () =>
            {
                Console.WriteLine("Gamepad polling started");
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        bool active = _capturing ||
                                      (ConfigManager.Instance.IsGamepadHotKeyEnabled() && ConfigManager.Instance.IsHotKeyEnabled());
                        if (active)
                        {
                            Poll();
                        }
                        else
                        {
                            ResetControllerState();
                        }

                        await Task.Delay(active ? PollIntervalMs : IdlePollIntervalMs, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation
                }
                catch (DllNotFoundException ex)
                {
                    Console.WriteLine($"XInput is not available, gamepad shortcuts disabled: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in gamepad polling: {ex.Message}");
                }
                finally
                {
                    Console.WriteLine("Gamepad polling stopped");
                }
            }, token);
        }

        public static void Cleanup()
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;
        }

        // Start recording a combination for the Settings window; gamepad shortcuts don't fire meanwhile
        public static void BeginCapture()
        {
            lock (_previousButtons)
            {
                _captureMask = 0;
                _captureWaitForRelease = true;
                _capturing = true;
            }
        }

        public static void EndCapture()
        {
            lock (_previousButtons)
            {
                _capturing = false;
                _captureMask = 0;
            }
        }

        private static void Poll()
        {
            long now = Environment.TickCount64;
            uint heldOnAny = 0;
            bool connectionChanged = false;
            var combos = _parsedCombos;

            lock (_previousButtons)
            {
                for (int i = 0; i < MaxControllers; i++)
                {
                    if (!_connected[i] && now < _nextConnectCheck[i])
                        continue;

                    bool connected = XInputGetState((uint)i, out XINPUT_STATE state) == ERROR_SUCCESS;
                    if (connected != _connected[i])
                    {
                        lock (_connected)
                        {
                            _connected[i] = connected;
                        }
                        connectionChanged = true;
                        Console.WriteLine($"Gamepad {i + 1} {(connected ? "connected" : "disconnected")}");
                    }

                    if (!connected)
                    {
                        _previousButtons[i] = 0;
                        _nextConnectCheck[i] = now + DisconnectedRecheckMs;
                        continue;
                    }

                    uint held = state.Gamepad.wButtons;
                    if (state.Gamepad.bLeftTrigger >= TriggerThreshold) held |= LT_MASK;
                    if (state.Gamepad.bRightTrigger >= TriggerThreshold) held |= RT_MASK;

                    if (!_capturing)
                    {
                        DetectCombos(combos, _previousButtons[i], held);
                    }

                    _previousButtons[i] = held;
                    heldOnAny |= held;
                }

                if (_capturing)
                {
                    UpdateCapture(heldOnAny);
                }
            }

            if (connectionChanged)
            {
                RaiseOnUiThread(() => ConnectionChanged?.Invoke(null, EventArgs.Empty));
            }
        }

        // Fire each combo on the tick it becomes complete. When a larger combo containing it is complete too
        // (e.g. "BACK+LB+A" over "BACK+A"), only the larger one fires.
        private static void DetectCombos(Dictionary<string, uint> combos, uint previous, uint held)
        {
            foreach (var kvp in combos)
            {
                uint combo = kvp.Value;
                if ((held & combo) != combo || (previous & combo) == combo)
                    continue;

                bool coveredByLargerCombo = combos.Values.Any(other =>
                    other != combo && (other & combo) == combo && (held & other) == other);
                if (coveredByLargerCombo)
                    continue;

                Console.WriteLine($"Gamepad shortcut detected: {kvp.Key}");
                string function = kvp.Key;
                RaiseOnUiThread(() => KeyboardShortcuts.ProcessHandleHotKey(function));
            }
        }

        private static void UpdateCapture(uint held)
        {
            if (_captureWaitForRelease)
            {
                if (held == 0)
                    _captureWaitForRelease = false;
                return;
            }

            if (held != 0)
            {
                uint mask = _captureMask | held;
                if (mask != _captureMask)
                {
                    _captureMask = mask;
                    RaiseOnUiThread(() => CaptureProgress?.Invoke(mask));
                }
            }
            else if (_captureMask != 0)
            {
                // Everything released: the combination is every button that was held during the press
                uint mask = _captureMask;
                _captureMask = 0;
                RaiseOnUiThread(() => CaptureCompleted?.Invoke(mask));
            }
        }

        private static void ResetControllerState()
        {
            bool hadController;
            lock (_previousButtons)
            {
                lock (_connected)
                {
                    hadController = Array.IndexOf(_connected, true) >= 0;
                    Array.Clear(_connected);
                }
                Array.Clear(_previousButtons);
                Array.Clear(_nextConnectCheck);
            }

            if (hadController)
            {
                RaiseOnUiThread(() => ConnectionChanged?.Invoke(null, EventArgs.Empty));
            }
        }

        // Never block the polling thread on the UI
        private static void RaiseOnUiThread(Action action)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error handling gamepad event: {ex.Message}");
                    }
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error dispatching gamepad event: {ex.Message}");
            }
        }
    }
}
