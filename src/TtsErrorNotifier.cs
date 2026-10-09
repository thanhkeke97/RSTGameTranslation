using System;
using System.Collections.Generic;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace RSTGameTranslation
{
    /// <summary>
    /// Shows TTS error popups at most once per service per cooldown window. TTS runs
    /// automatically for every translated line, so a network hiccup or a bad API key used to
    /// raise a MessageBox per line — each one pulling a fullscreen game out of focus.
    /// Errors are always logged; only the popup is throttled.
    /// </summary>
    public static class TtsErrorNotifier
    {
        private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);
        private static readonly Dictionary<string, DateTime> lastShown = new Dictionary<string, DateTime>();
        private static readonly object gate = new object();

        public static void ShowError(string service, string message, string title = "TTS Error")
        {
            Console.WriteLine($"[TTS:{service}] {message}");

            lock (gate)
            {
                if (lastShown.TryGetValue(service, out var last) && DateTime.UtcNow - last < Cooldown) return;
                lastShown[service] = DateTime.UtcNow;
            }

            try
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning)));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TTS:{service}] Failed to show error popup: {ex.Message}");
            }
        }
    }
}
