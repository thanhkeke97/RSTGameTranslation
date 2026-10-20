using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;
using ComboBox = System.Windows.Controls.ComboBox;

namespace RSTGameTranslation
{
    /// <summary>
    /// The languages offered for translation, keyed by Google Translate codes: these codes are the
    /// app's source/target language setting. Every service maps from here, so adding a language
    /// is one line. OneOCR reads any script, so the list follows what translation supports.
    /// </summary>
    public static class LanguageCatalog
    {
        public const string Auto = "auto";

        /// <param name="MicrosoftCode">Microsoft Translator code, or null when it does not support the language</param>
        /// <param name="YandexCode">Yandex code, or null when it does not support the language</param>
        public sealed record Language(string Code, string EnglishName, string? MicrosoftCode, string? YandexCode);

        // Same code as Google
        private const string S = "=";

        private static Language L(string code, string name, string? microsoft = S, string? yandex = S) =>
            new Language(code, name, microsoft == S ? code : microsoft, yandex == S ? code : yandex);

        // Google Translate's languages (without the 2024 additions, which are mostly rare regional
        // languages), sorted by English name
        private static readonly Language[] _languages =
        {
            L("af", "Afrikaans"),
            L("sq", "Albanian"),
            L("am", "Amharic"),
            L("ar", "Arabic"),
            L("hy", "Armenian"),
            L("as", "Assamese", yandex: null),
            L("ay", "Aymara", null, null),
            L("az", "Azerbaijani"),
            L("bm", "Bambara", null, null),
            L("eu", "Basque"),
            L("be", "Belarusian", microsoft: null),
            L("bn", "Bengali"),
            L("bho", "Bhojpuri", yandex: null),
            L("bs", "Bosnian"),
            L("bg", "Bulgarian"),
            L("ca", "Catalan"),
            L("ceb", "Cebuano", microsoft: null),
            L("ny", "Chichewa", "nya", null),
            // The Microsoft endpoint in use accepts zh-CN / zh-TW
            L("zh-CN", "Chinese (Simplified)", "zh-CN", "zh"),
            L("zh-TW", "Chinese (Traditional)", "zh-TW", "zh"),
            L("co", "Corsican", null, null),
            L("hr", "Croatian"),
            L("cs", "Czech"),
            L("da", "Danish"),
            L("dv", "Dhivehi", yandex: null),
            L("doi", "Dogri", yandex: null),
            L("nl", "Dutch"),
            L("en", "English"),
            L("eo", "Esperanto", microsoft: null),
            L("et", "Estonian"),
            L("ee", "Ewe", null, null),
            L("tl", "Filipino", "fil"),
            L("fi", "Finnish"),
            L("fr", "French"),
            L("fy", "Frisian", null, null),
            L("gl", "Galician"),
            L("ka", "Georgian"),
            L("de", "German"),
            L("el", "Greek"),
            L("gn", "Guarani", null, null),
            L("gu", "Gujarati"),
            L("ht", "Haitian Creole"),
            L("ha", "Hausa", yandex: null),
            L("haw", "Hawaiian", null, null),
            L("iw", "Hebrew", "he", "he"),
            L("hi", "Hindi"),
            L("hmn", "Hmong", "mww", null),
            L("hu", "Hungarian"),
            L("is", "Icelandic"),
            L("ig", "Igbo", yandex: null),
            L("ilo", "Ilocano", null, null),
            L("id", "Indonesian"),
            L("ga", "Irish"),
            L("it", "Italian"),
            L("ja", "Japanese"),
            L("jw", "Javanese", null, "jv"),
            L("kn", "Kannada"),
            L("kk", "Kazakh"),
            L("km", "Khmer"),
            L("rw", "Kinyarwanda", yandex: null),
            L("gom", "Konkani", yandex: null),
            L("ko", "Korean"),
            L("kri", "Krio", null, null),
            L("ku", "Kurdish (Kurmanji)", "kmr", null),
            L("ckb", "Kurdish (Sorani)", "ku", null),
            L("ky", "Kyrgyz"),
            L("lo", "Lao"),
            L("la", "Latin", microsoft: null),
            L("lv", "Latvian"),
            L("ln", "Lingala", yandex: null),
            L("lt", "Lithuanian"),
            L("lg", "Luganda", "lug", null),
            L("lb", "Luxembourgish", microsoft: null),
            L("mk", "Macedonian"),
            L("mai", "Maithili", yandex: null),
            L("mg", "Malagasy"),
            L("ms", "Malay"),
            L("ml", "Malayalam"),
            L("mt", "Maltese"),
            L("mi", "Maori"),
            L("mr", "Marathi"),
            L("mni-Mtei", "Meiteilon (Manipuri)", null, null),
            L("lus", "Mizo", null, null),
            L("mn", "Mongolian", "mn-Cyrl"),
            L("my", "Myanmar (Burmese)"),
            L("ne", "Nepali"),
            L("no", "Norwegian", "nb"),
            L("or", "Odia (Oriya)", yandex: null),
            L("om", "Oromo", null, null),
            L("ps", "Pashto", yandex: null),
            L("fa", "Persian"),
            L("pl", "Polish"),
            L("pt", "Portuguese"),
            L("pa", "Punjabi"),
            L("qu", "Quechua", null, null),
            L("ro", "Romanian"),
            L("ru", "Russian"),
            L("sm", "Samoan", yandex: null),
            L("sa", "Sanskrit", null, null),
            L("gd", "Scots Gaelic", microsoft: null),
            L("nso", "Sepedi", yandex: null),
            L("sr", "Serbian", "sr-Cyrl"),
            L("st", "Sesotho", yandex: null),
            L("sn", "Shona", yandex: null),
            L("sd", "Sindhi", yandex: null),
            L("si", "Sinhala"),
            L("sk", "Slovak"),
            L("sl", "Slovenian"),
            L("so", "Somali", yandex: null),
            L("es", "Spanish"),
            L("su", "Sundanese", microsoft: null),
            L("sw", "Swahili"),
            L("sv", "Swedish"),
            L("tg", "Tajik", microsoft: null),
            L("ta", "Tamil"),
            L("tt", "Tatar"),
            L("te", "Telugu"),
            L("th", "Thai"),
            L("ti", "Tigrinya", yandex: null),
            L("ts", "Tsonga", null, null),
            L("tr", "Turkish"),
            L("tk", "Turkmen", yandex: null),
            L("ak", "Twi", null, null),
            L("uk", "Ukrainian"),
            L("ur", "Urdu"),
            L("ug", "Uyghur", yandex: null),
            L("uz", "Uzbek"),
            L("vi", "Vietnamese"),
            L("cy", "Welsh"),
            L("xh", "Xhosa"),
            L("yi", "Yiddish", microsoft: null),
            L("yo", "Yoruba", yandex: null),
            L("zu", "Zulu"),
        };

        private static readonly Dictionary<string, Language> _byCode =
            _languages.ToDictionary(l => l.Code, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, Language> _byName =
            _languages.ToDictionary(l => l.EnglishName, StringComparer.OrdinalIgnoreCase);

        // Codes saved by older versions and other common spellings
        private static readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ch_sim"] = "zh-CN", ["ch_tra"] = "zh-TW", ["zh"] = "zh-CN", ["zh-Hans"] = "zh-CN", ["zh-Hant"] = "zh-TW",
            ["chinese"] = "zh-CN", ["simplified chinese"] = "zh-CN", ["traditional chinese"] = "zh-TW",
            ["he"] = "iw", ["jv"] = "jw", ["fil"] = "tl", ["nb"] = "no", ["bg-BG"] = "bg",
            ["japan"] = "ja", ["farsi"] = "fa", ["thailand"] = "th", ["central kurdish"] = "ckb",
        };

        // Written without spaces between words (and possibly vertically), which changes how OCR
        // lines are joined. Korean is included as the app always treated it this way.
        private static readonly HashSet<string> _eastAsian = new(StringComparer.OrdinalIgnoreCase) { "ja", "zh-CN", "zh-TW", "ko" };

        private static readonly HashSet<string> _rightToLeft = new(StringComparer.OrdinalIgnoreCase)
        {
            "ar", "fa", "iw", "ur", "ckb", "ps", "sd", "yi", "dv", "ug"
        };

        public static IReadOnlyList<Language> All => _languages;

        public static bool IsAuto(string? value) =>
            value != null && (value.Equals(Auto, StringComparison.OrdinalIgnoreCase)
                              || value.Equals("auto-detect", StringComparison.OrdinalIgnoreCase)
                              || value.Equals("auto_detect", StringComparison.OrdinalIgnoreCase));

        /// <summary>The language for a code, an older code (ch_sim, he, ...) or an English name; null if unknown.</summary>
        public static Language? Find(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            value = value.Trim();
            if (_aliases.TryGetValue(value, out string? code))
                value = code;
            if (_byCode.TryGetValue(value, out Language? language) || _byName.TryGetValue(value, out language))
                return language;
            return null;
        }

        /// <summary>The catalog code for a saved setting, or <paramref name="fallback"/> when it is not a known language.</summary>
        public static string Normalize(string? value, string fallback, bool allowAuto)
        {
            if (IsAuto(value))
                return allowAuto ? Auto : fallback;
            return Find(value)?.Code ?? fallback;
        }

        /// <summary>English name, as used in AI prompts ("auto-detect" for automatic detection).</summary>
        public static string GetEnglishName(string? code)
        {
            if (IsAuto(code))
                return "auto-detect";
            return Find(code)?.EnglishName ?? code ?? "";
        }

        /// <summary>Google code; unknown values are returned unchanged.</summary>
        public static string ToGoogleCode(string? value) =>
            IsAuto(value) ? Auto : Find(value)?.Code ?? value ?? "";

        /// <summary>Microsoft code, null when Microsoft does not support the language; unknown values are returned unchanged.</summary>
        public static string? ToMicrosoftCode(string? value) =>
            IsAuto(value) ? Auto : Find(value) is Language language ? language.MicrosoftCode : value;

        /// <summary>Yandex code, null when Yandex does not support the language; unknown values are returned unchanged.</summary>
        public static string? ToYandexCode(string? value) =>
            IsAuto(value) ? Auto : Find(value) is Language language ? language.YandexCode : value;

        /// <summary>Plain ISO 639-1 style code (zh-CN → zh, iw → he) for speech and TTS engines.</summary>
        public static string ToIsoCode(string? value)
        {
            if (IsAuto(value))
                return Auto;
            string code = Find(value)?.Code ?? value ?? "";
            return code switch
            {
                "zh-CN" or "zh-TW" => "zh",
                "iw" => "he",
                "jw" => "jv",
                "mni-Mtei" => "mni",
                _ => code
            };
        }

        public static bool IsEastAsian(string? code) => code != null && _eastAsian.Contains(Find(code)?.Code ?? code);

        public static bool IsRightToLeft(string? code) => code != null && _rightToLeft.Contains(Find(code)?.Code ?? code);

        /// <summary>
        /// The source language for OCR text: the configured one, or, when it is auto-detect, a guess
        /// from the script (kana → Japanese, hangul → Korean, han → Chinese) so East Asian text is
        /// still joined without spaces. Stays "auto" for other scripts.
        /// </summary>
        public static string ResolveForText(string? code, IEnumerable<string?> texts)
        {
            if (!IsAuto(code))
                return Find(code)?.Code ?? code ?? "";

            int kana = 0, han = 0, hangul = 0, other = 0;
            foreach (string? text in texts)
            {
                if (text == null)
                    continue;
                foreach (char c in text)
                {
                    if (c >= '぀' && c <= 'ヿ') kana++;
                    else if ((c >= '一' && c <= '鿿') || (c >= '㐀' && c <= '䶿')) han++;
                    else if ((c >= '가' && c <= '힯') || (c >= 'ᄀ' && c <= 'ᇿ') || (c >= '㄰' && c <= '㆏')) hangul++;
                    else if (char.IsLetter(c)) other++;
                }
            }

            if (kana > 0 && kana + han >= other)
                return "ja";
            if (hangul > han && hangul >= other)
                return "ko";
            if (han > 0 && han >= other)
                return "zh-CN";
            return Auto;
        }

        /// <summary>
        /// Fill a language picker. Items hold the code (read back through Content as before) and
        /// show the name; typing a name's first letters jumps to it.
        /// </summary>
        public static void FillComboBox(ComboBox comboBox, bool includeAuto)
        {
            var template = (DataTemplate)Application.Current.FindResource("LanguageCodeTemplate");
            comboBox.Items.Clear();
            if (includeAuto)
            {
                comboBox.Items.Add(CreateItem(Auto, LanguageDisplay.GetShortName(Auto), template));
            }
            foreach (Language language in _languages)
            {
                comboBox.Items.Add(CreateItem(language.Code, language.EnglishName, template));
            }
        }

        private static ComboBoxItem CreateItem(string code, string searchText, DataTemplate template)
        {
            var item = new ComboBoxItem { Content = code, ContentTemplate = template };
            TextSearch.SetText(item, searchText);
            return item;
        }

        /// <summary>
        /// Tell the user that a service cannot translate the selected languages (shown once until it
        /// translates successfully again).
        /// </summary>
        public static void WarnUnsupported(string service, params string?[] values)
        {
            string names = string.Join(", ", values.Where(v => !string.IsNullOrEmpty(v)).Select(GetEnglishName));
            Console.WriteLine($"{service} does not support: {names}");
            TranslationErrorNotifier.ShowOnce(service,
                string.Format(LocalizationManager.Instance.Strings["Msg_ServiceLanguageNotSupported"], service, names),
                LocalizationManager.Instance.Strings["Title_ServiceLanguageNotSupported"]);
        }
    }
}
