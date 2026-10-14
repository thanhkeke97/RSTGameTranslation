using System;
using System.Text.RegularExpressions;

namespace RSTGameTranslation
{
    /// <summary>
    /// Helpers for text that types out a few characters at a time (typewriter dialogue). Each OCR
    /// frame then shows the previous text plus a little more, which a plain similarity check takes
    /// for "the same text": the sentence would be translated half-typed and the rest skipped.
    /// </summary>
    public static class TextGrowth
    {
        // Above this many character comparisons only the cheap prefix check runs (a full manga page)
        private const long MaxEditDistanceWork = 4_000_000;

        /// <summary>
        /// True when current is previous with characters added (anywhere, so a static label after the
        /// dialogue does not hide it), allowing a misread character or two from OCR.
        /// </summary>
        public static bool IsGrowth(string previous, string current)
        {
            string p = Normalize(previous);
            string c = Normalize(current);
            if (p.Length == 0)
                return false;

            int added = c.Length - p.Length;
            // One CJK character is a real step; for other scripts a single extra character is too
            // often OCR noise (a stray dot or letter)
            if (added < (ContainsCjk(c) ? 1 : 2))
                return false;

            if (c.StartsWith(p, StringComparison.Ordinal))
                return true;
            if ((long)p.Length * c.Length > MaxEditDistanceWork)
                return false;

            // Pure insertions turn previous into current with exactly `added` edits
            int tolerance = Math.Max(1, p.Length / 20);
            return EditDistance(p, c) - added <= tolerance;
        }

        /// <summary>
        /// True when the text ends like a finished sentence. A typewriter often pauses on a comma or an
        /// ellipsis, so those do not count.
        /// </summary>
        public static bool EndsSentence(string text)
        {
            string t = text.TrimEnd();
            if (t.Length == 0)
                return false;
            if (t.EndsWith("…") || t.EndsWith("‥") || t.EndsWith(".."))
                return false;
            return ".!?。！？」』\"”’)）】♪～~▼▽".IndexOf(t[t.Length - 1]) >= 0;
        }

        private static string Normalize(string text) =>
            string.IsNullOrEmpty(text) ? "" : Regex.Replace(text.ToLowerInvariant().Trim(), @"\s+", " ");

        private static bool ContainsCjk(string text)
        {
            foreach (char ch in text)
            {
                // CJK punctuation, kana, ideographs, Hangul, full-width forms
                if (ch >= '⺀' && ch <= '￯')
                    return true;
            }
            return false;
        }

        private static int EditDistance(string a, string b)
        {
            int[] prev = new int[b.Length + 1];
            int[] curr = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++)
                prev[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }
            return prev[b.Length];
        }
    }
}
