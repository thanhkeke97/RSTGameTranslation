using System.Collections.Generic;

namespace RSTGameTranslation
{
    /// <summary>
    /// Maps the app's source-language setting (Google codes such as "ja", "zh-CN", "iw") to the ISO codes
    /// the speech-recognition engines expect. Shared by Whisper and FunASR/SenseVoice, which
    /// previously each kept their own copy of this table.
    /// </summary>
    internal static class SpeechLanguageCodes
    {
        // Languages the SenseVoice model can recognize
        private static readonly HashSet<string> SenseVoiceLanguages = new HashSet<string> { "zh", "en", "ja", "ko", "yue" };

        /// <summary>ISO code for a language setting ("auto" stays "auto"); unknown values are returned unchanged.</summary>
        public static string ToIsoCode(string language)
        {
            if (language.Equals("cantonese", System.StringComparison.OrdinalIgnoreCase))
                return "yue";
            return LanguageCatalog.ToIsoCode(language);
        }

        /// <summary>SenseVoice code for a language setting; unsupported languages map to "auto".</summary>
        public static string ToSenseVoiceCode(string language)
        {
            string code = ToIsoCode(language);
            return SenseVoiceLanguages.Contains(code) ? code : "auto";
        }
    }
}
