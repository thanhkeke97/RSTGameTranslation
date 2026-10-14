using System;
using System.Globalization;
using System.Windows.Data;

namespace RSTGameTranslation
{
    // Readable names for the app's OCR/translation language codes (ja, en, ch_sim, ...)
    public static class LanguageDisplay
    {
        // "Japanese — 日本語 (ja)"
        public static string GetDisplayName(string code)
        {
            CultureInfo? culture = GetCulture(code);
            if (culture == null)
                return code;

            return culture.NativeName == culture.EnglishName
                ? $"{culture.EnglishName} ({code})"
                : $"{culture.EnglishName} — {culture.NativeName} ({code})";
        }

        // "Japanese"
        public static string GetShortName(string code) => GetCulture(code)?.EnglishName ?? code;

        private static CultureInfo? GetCulture(string code)
        {
            // The app uses its own codes for Chinese
            string cultureName = code.ToLowerInvariant() switch
            {
                "ch_sim" => "zh-Hans",
                "ch_tra" => "zh-Hant",
                _ => code
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
