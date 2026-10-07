using System.Collections.Generic;

namespace RSTGameTranslation
{
    /// <summary>
    /// Shows translation service error dialogs at most once per failure streak.
    /// With auto-OCR a failing service is called every capture cycle, so showing a modal
    /// dialog on every failure would flood the user with popups.
    /// </summary>
    public static class TranslationErrorNotifier
    {
        private static readonly HashSet<string> _notifiedServices = new HashSet<string>();
        private static readonly object _lock = new object();

        /// <summary>
        /// Show the error dialog unless one was already shown for this service since its last success.
        /// </summary>
        public static void ShowOnce(string service, string message, string title)
        {
            lock (_lock)
            {
                if (!_notifiedServices.Add(service)) return;
            }

            // Application.Current is null during shutdown
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                System.Windows.MessageBox.Show(message, title,
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            });
        }

        /// <summary>
        /// Re-arm the dialog for this service after a successful translation.
        /// </summary>
        public static void Reset(string service)
        {
            lock (_lock)
            {
                _notifiedServices.Remove(service);
            }
        }
    }
}
