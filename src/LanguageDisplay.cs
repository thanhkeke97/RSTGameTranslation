using System;
using System.Globalization;
using System.Windows.Data;

namespace RSTGameTranslation
{
    // Readable names for the app's translation language codes (Google Translate codes, see LanguageCatalog)
    public static class LanguageDisplay
    {
        // "Japanese — 日本語 (ja)"
        public static string GetDisplayName(string code)
        {
            if (LanguageCatalog.IsAuto(code))
                return GetShortName(code);

            string englishName = GetShortName(code);
            string? nativeName = GetCulture(code)?.NativeName;
            return string.IsNullOrEmpty(nativeName) || string.Equals(nativeName, englishName, StringComparison.OrdinalIgnoreCase)
                ? $"{englishName} ({code})"
                : $"{englishName} — {nativeName} ({code})";
        }

        // "Japanese"
        public static string GetShortName(string code)
        {
            if (LanguageCatalog.IsAuto(code))
                return LocalizationManager.Instance["Lang_AutoDetect"];
            return LanguageCatalog.Find(code)?.EnglishName ?? GetCulture(code)?.EnglishName ?? code;
        }

        private static CultureInfo? GetCulture(string code)
        {
            // Culture names differ from Google's codes for Chinese and a few legacy codes (iw, jw)
            string cultureName = LanguageCatalog.Find(code)?.Code switch
            {
                "zh-CN" => "zh-Hans",
                "zh-TW" => "zh-Hant",
                _ => LanguageCatalog.ToIsoCode(code)
            };

            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
                // Show "Bulgarian" rather than "Bulgarian (Bulgaria)" for region-specific codes like bg-BG
                if (!culture.IsNeutralCulture && !Equals(culture.Parent, CultureInfo.InvariantCulture))
                {
                    culture = culture.Parent;
                }
                return culture;
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }
    }

    // Shows a language code as its display name; the bound value stays the code
    public class LanguageCodeToNameConverter : IValueConverter
    {
        public bool ShortName { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? code = value?.ToString();
            if (string.IsNullOrEmpty(code))
                return "";
            return ShortName ? LanguageDisplay.GetShortName(code) : LanguageDisplay.GetDisplayName(code);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
