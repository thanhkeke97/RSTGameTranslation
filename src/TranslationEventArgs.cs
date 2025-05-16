using System;

namespace RSTGameTranslation
{
    public class TranslationEventArgs : EventArgs
    {
        public string OriginalText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
    }
}