// Token algorithm adapted from VictorZhang2014/free-google-translate (MIT).
// See THIRD-PARTY-NOTICES.md for the original copyright and license.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RSTGameTranslation
{
    internal static class GoogleTranslateWebToken
    {
        private static readonly Regex SeedPattern = new Regex(
            """(?:\btkk\b["']?|\[\s*["']tkk["']\s*\])\s*[:=]\s*["'](?<seed>[0-9]+\.[0-9]+)["']""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

        internal static string? ExtractSeed(string html)
        {
            Match match = SeedPattern.Match(html);
            string seed = match.Groups["seed"].Value;
            return match.Success && TryParseSeed(seed, out _, out _) ? seed : null;
        }

        private static bool TryParseSeed(string seed, out uint first, out uint second)
        {
            first = second = 0;
            string[] parts = seed.Split('.');
            return parts.Length == 2
                && uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out first)
                && uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out second);
        }

        internal static string Create(string text, string seed)
        {
            if (!TryParseSeed(seed, out uint first, out uint second))
                throw new ArgumentException("Invalid Google Translate TKK seed.", nameof(seed));

            // Unsigned arithmetic reproduces JavaScript's 32-bit shifts/overflow.
            // UTF-8 includes complete surrogate pairs (e.g. emoji) without filtering text.
            uint value = first;
            foreach (byte item in Encoding.UTF8.GetBytes(text))
            {
                value = Mix(unchecked(value + item), "+-a^+6");
            }
            value = (Mix(value, "+-3^+b+-f") ^ second) % 1_000_000;
            return value.ToString(CultureInfo.InvariantCulture) + "."
                + unchecked((int)(value ^ first)).ToString(CultureInfo.InvariantCulture);
        }

        private static uint Mix(uint value, string operations)
        {
            for (int i = 0; i < operations.Length - 2; i += 3)
            {
                int shift = operations[i + 2] >= 'a' ? operations[i + 2] - 87 : operations[i + 2] - '0';
                uint operand = operations[i + 1] == '+' ? value >> shift : value << shift;
                value = operations[i] == '+' ? unchecked(value + operand) : value ^ operand;
            }
            return value;
        }
    }
}
