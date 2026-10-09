using System.Collections.Generic;

namespace RSTGameTranslation
{
    /// <summary>
    /// Maps the app's source-language setting ("japanese", "ch_sim", "ja", ...) to the ISO codes
    /// the speech-recognition engines expect. Shared by Whisper and FunASR/SenseVoice, which
    /// previously each kept their own copy of this table.
    /// </summary>
    internal static class SpeechLanguageCodes
    {
        // Languages the SenseVoice model can recognize
        private static readonly HashSet<string> SenseVoiceLanguages = new HashSet<string> { "zh", "en", "ja", "ko", "yue" };

        /// <summary>ISO code for a language setting; unknown values are returned unchanged.</summary>
        public static string ToIsoCode(string language)
        {
            return language.ToLowerInvariant() switch
            {
                "japanese" or "japan" or "ja" => "ja",
                "english" or "en" => "en",
                "chinese" or "zh" or "ch_sim" => "zh",
                "traditional chinese" or "ch_tra" => "zh",
                "cantonese" or "yue" => "yue",
                "korean" or "ko" => "ko",
                "vietnamese" or "vi" => "vi",
                "french" or "fr" => "fr",
                "german" or "de" => "de",
                "spanish" or "es" => "es",
                "italian" or "it" => "it",
                "portuguese" or "pt" => "pt",
                "russian" or "ru" => "ru",
                "hindi" or "hi" => "hi",
                "indonesian" or "id" => "id",
                "polish" or "pl" => "pl",
                "arabic" or "ar" => "ar",
                "dutch" or "nl" => "nl",
                "romanian" or "ro" => "ro",
                "persian" or "farsi" or "fa" => "fa",
                "czech" or "cs" => "cs",
                "bulgarian" or "bg" => "bg",
                "thai" or "th" or "thailand" => "th",
                "croatian" or "hr" => "hr",
                "hungarian" or "hu" => "hu",
                "turkish" or "tr" => "tr",
                "sinhala" or "si" => "si",
                "danish" or "da" => "da",
                "ukrainian" or "uk" => "uk",
                "finnish" or "fi" => "fi",
                "central kurdish" or "ckb" => "ckb",
                "bengali" or "bn" => "bn",
                "greek" or "el" => "el",
                "hebrew" or "he" or "iw" => "he",
                _ => language
            };
        }

        /// <summary>SenseVoice code for a language setting; unsupported languages map to "auto".</summary>
        public static string ToSenseVoiceCode(string language)
        {
            string code = ToIsoCode(language);
            return SenseVoiceLanguages.Contains(code) ? code : "auto";
        }
    }
}
