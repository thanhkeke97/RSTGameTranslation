using System;
using System.Windows.Input;

namespace UGTLive
{
    public static class KeyboardShortcuts
    {
        public static event EventHandler? StartStopRequested;
        public static event EventHandler? MonitorToggleRequested;
        public static event EventHandler? ChatBoxToggleRequested;
        public static event EventHandler? SettingsToggleRequested;
        public static event EventHandler? LogToggleRequested;
        public static event EventHandler? MainWindowVisibilityToggleRequested;
        public static event EventHandler? OcrRefreshRequested;

        // Handle shortcut keys
        public static bool HandleKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                // Shift+S: Start/Stop OCR
                if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    StartStopRequested?.Invoke(null, EventArgs.Empty);
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
                // Shift+H: Toggle Main Window Visibility
                else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    MainWindowVisibilityToggleRequested?.Invoke(null, EventArgs.Empty);
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
    }
}