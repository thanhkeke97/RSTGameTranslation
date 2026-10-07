namespace RSTGameTranslation
{
    /// <summary>
    /// Contextual hints shown when a user is likely stuck: started without choosing an area,
    /// OCR finds no text, or translation keeps failing. Also shows a one-time tip after the first success.
    /// </summary>
    public static class OnboardingTips
    {
        private const string StartWithoutAreaTipId = "start_without_area";
        private const string FirstTranslationTipId = "first_translation";

        // With auto OCR, frames come in continuously, so wait for several empty frames before suggesting a fix
        private const int EmptyFramesBeforeNoTextTip = 10;
        // A single failure is often a transient network error
        private const int FailuresBeforeTranslationTip = 2;

        private const int TipDurationMs = 12000;

        private static readonly object _lock = new object();
        private static int _emptyFramesThisSession;
        private static bool _textSeenThisSession;
        private static bool _noTextTipShownThisSession;
        private static int _consecutiveTranslationFailures;
        private static bool _translationFailureTipShown;

        /// <summary>
        /// Call when the user presses Start.
        /// </summary>
        public static void OnTranslationStarted(bool hasSelectedArea)
        {
            lock (_lock)
            {
                _emptyFramesThisSession = 0;
                _textSeenThisSession = false;
                _noTextTipShownThisSession = false;
            }

            // Selecting the game window alone is not enough; a translation area is still required
            if (!hasSelectedArea)
            {
                ShowOnce(StartWithoutAreaTipId, "Tip_NoArea_Title", Format("Tip_NoArea_Message", S("Btn_SelectArea")));
            }
        }

        /// <summary>
        /// Call for every processed OCR frame.
        /// </summary>
        public static void OnOcrResult(bool hasText)
        {
            lock (_lock)
            {
                if (hasText)
                {
                    _textSeenThisSession = true;
                    return;
                }

                // Empty frames are normal once text has been found (e.g. between dialogue lines)
                if (_textSeenThisSession || _noTextTipShownThisSession)
                    return;

                _emptyFramesThisSession++;
                int threshold = ConfigManager.Instance.IsAutoOCREnabled() ? EmptyFramesBeforeNoTextTip : 1;
                if (_emptyFramesThisSession < threshold)
                    return;

                _noTextTipShownThisSession = true;
            }

            Show("Tip_NoText_Title", Format("Tip_NoText_Message", S("Btn_Settings")));
        }

        /// <summary>
        /// Call after each OCR translation attempt.
        /// </summary>
        public static void OnTranslationResult(string service, bool succeeded)
        {
            bool showFailureTip = false;
            lock (_lock)
            {
                if (succeeded)
                {
                    _consecutiveTranslationFailures = 0;
                    _translationFailureTipShown = false;
                }
                else
                {
                    _consecutiveTranslationFailures++;
                    if (_consecutiveTranslationFailures >= FailuresBeforeTranslationTip && !_translationFailureTipShown)
                    {
                        _translationFailureTipShown = true;
                        showFailureTip = true;
                    }
                }
            }

            if (showFailureTip)
            {
                Show("Tip_TranslationFailed_Title", Format("Tip_TranslationFailed_Message", service, S("Btn_Settings")));
            }

            if (succeeded)
            {
                var config = ConfigManager.Instance;
                ShowOnce(FirstTranslationTipId, "Tip_FirstTranslation_Title",
                    Format("Tip_FirstTranslation_Message",
                        config.GetHotKey("Start/Stop"), config.GetHotKey("Overlay"), config.GetHotKey("ChatBox"),
                        S("Btn_Settings"), S("Tab_HotKeys")));
            }
        }

        private static void ShowOnce(string tipId, string titleKey, string message)
        {
            if (ConfigManager.Instance.IsOnboardingTipShown(tipId))
                return;

            ConfigManager.Instance.MarkOnboardingTipShown(tipId);
            Show(titleKey, message);
        }

        private static void Show(string titleKey, string message)
        {
            // Callers run on OCR/translation threads; the notification must be created on the UI thread
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                MainWindow.Instance.ShowFastNotification(S(titleKey), message, TipDurationMs));
        }

        private static string S(string key) => LocalizationManager.Instance.Strings[key];

        private static string Format(string key, params object[] args) => string.Format(S(key), args);
    }
}
