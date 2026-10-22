using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

using FontFamily = System.Windows.Media.FontFamily;
using Panel = System.Windows.Controls.Panel;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace RSTGameTranslation
{
    /// <summary>
    /// Release notes of the running version, read from its GitHub release (tag "V{version}", the same
    /// tag the updater downloads from). The last copy is cached on disk so the notes still show offline.
    /// </summary>
    public static class Changelog
    {
        public const string ReleasesPageUrl = "https://github.com/thanhkeke97/RSTGameTranslation/releases";

        public static string Tag => "V" + SplashManager.FormatVersion(SplashManager.CurrentVersion);
        public static string ReleasePageUrl => $"{ReleasesPageUrl}/tag/{Tag}";

        private static string CachePath => Path.Combine(AppContext.BaseDirectory, "changelog_cache.json");

        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Add("User-Agent", "RSTGameTranslation");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            return client;
        }

        private class CacheEntry
        {
            public string Tag { get; set; } = "";
            public string Body { get; set; } = "";
        }

        /// <summary>The release notes (markdown), or null when neither GitHub nor the cache has them.</summary>
        public static async Task<string?> GetReleaseNotesAsync()
        {
            try
            {
                string url = $"https://api.github.com/repos/thanhkeke97/RSTGameTranslation/releases/tags/{Tag}";
                using var response = await _http.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("body", out JsonElement body) && body.ValueKind == JsonValueKind.String)
                    {
                        string notes = body.GetString() ?? "";
                        SaveCache(notes);
                        return notes;
                    }
                }
                else
                {
                    Console.WriteLine($"Changelog: GitHub returned {(int)response.StatusCode} for {Tag}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Changelog: could not fetch release notes: {ex.Message}");
            }

            return LoadCache();
        }

        private static void SaveCache(string body)
        {
            try
            {
                File.WriteAllText(CachePath, JsonSerializer.Serialize(new CacheEntry { Tag = Tag, Body = body }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Changelog: could not save cache: {ex.Message}");
            }
        }

        private static string? LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath))
                    return null;
                var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(CachePath));
                // Notes of another version would be misleading
                return entry != null && entry.Tag == Tag ? entry.Body : null;
            }
            catch
            {
                return null;
            }
        }

        // ---------- Markdown to WPF ----------
        // Shown as GitHub shows release notes: no guessing, so notes that look right on GitHub look
        // right here. Covers what release notes use: headings, lists, quotes, code, tables, rules and
        // inline bold / italic / strikethrough / code / links. Line breaks are kept, as on GitHub.

        private static readonly Regex InlineToken = new Regex(
            @"!\[([^\]]*)\]\([^)]*\)" +                       // 1 image (alt text only)
            @"|\[([^\]]+)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)" +   // 2,3 link
            @"|`([^`]+)`" +                                    // 4 code
            @"|\*\*(.+?)\*\*|__(.+?)__" +                      // 5,6 bold
            @"|~~(.+?)~~" +                                    // 7 strikethrough
            @"|(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?!\*)|(?<!\w)_(?!\s)(.+?)(?<!\s)_(?!\w)" + // 8,9 italic
            @"|<(https?://[^>\s]+)>|(https?://[^\s<>()]+[^\s<>().,;:!?'""])"); // 10,11 bare links

        private enum BlockKind { None, Paragraph, ListItem, Quote }

        /// <summary>Shows the notes in <paramref name="target"/>; plain text if they cannot be rendered.</summary>
        public static void Show(Panel target, string markdown)
        {
            try
            {
                Render(target, markdown);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Changelog: showing plain text, markdown failed: {ex.Message}");
                target.Children.Clear();
                var plain = new TextBlock { Text = markdown, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
                plain.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                target.Children.Add(plain);
            }
        }

        private static void Render(Panel target, string markdown)
        {
            target.Children.Clear();

            // HTML that GitHub would render invisibly or as a line break
            string text = Regex.Replace(markdown.Replace("\r\n", "\n"), @"<!--.*?-->", "", RegexOptions.Singleline);
            text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</?(?:details|summary|p|div|span|sub|sup|b|i|em|strong|kbd|picture|source|img|a)\b[^>]*>", "", RegexOptions.IgnoreCase);

            string[] lines = text.Split('\n');
            TextBlock? open = null;          // block that a following line continues
            BlockKind openKind = BlockKind.None;
            bool gap = false;                // blank line before the next block

            double NextTop() => target.Children.Count == 0 ? 0 : (gap ? 10 : 3);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                string trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    open = null;
                    gap = true;
                    continue;
                }

                // Fenced code block
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                {
                    string fence = trimmed.Substring(0, 3);
                    var code = new List<string>();
                    while (++i < lines.Length && !lines[i].Trim().StartsWith(fence))
                        code.Add(lines[i].TrimEnd());
                    target.Children.Add(CodeBlock(string.Join("\n", code), NextTop()));
                    open = null; gap = false;
                    continue;
                }

                // "Title" + "===" / "---" underneath is a heading (setext)
                if (open != null && openKind == BlockKind.Paragraph && Regex.IsMatch(trimmed, @"^(=+|-+)$"))
                {
                    open.FontSize = trimmed[0] == '=' ? 17 : 15;
                    open.FontWeight = FontWeights.Bold;
                    open = null; gap = false;
                    continue;
                }

                // Horizontal rule
                if (Regex.IsMatch(trimmed, @"^([-*_])(\s*\1){2,}$"))
                {
                    var rule = new Border { Height = 1, Margin = new Thickness(0, 10, 0, 6) };
                    rule.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
                    target.Children.Add(rule);
                    open = null; gap = false;
                    continue;
                }

                var heading = Regex.Match(trimmed, @"^(#{1,6})\s+(.*?)\s*#*$");
                if (heading.Success)
                {
                    int level = heading.Groups[1].Value.Length;
                    double size = level switch { 1 => 17, 2 => 15, 3 => 14, _ => 13 };
                    var block = Inline(heading.Groups[2].Value, size, FontWeights.Bold);
                    block.Margin = new Thickness(0, target.Children.Count == 0 ? 0 : 12, 0, 2);
                    target.Children.Add(block);
                    open = null; gap = false;
                    continue;
                }

                // Table: header row, separator row, body rows
                if (trimmed.StartsWith("|") && i + 1 < lines.Length && Regex.IsMatch(lines[i + 1].Trim(), @"^\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?$"))
                {
                    var rows = new List<string> { trimmed };
                    i++; // separator
                    while (i + 1 < lines.Length && lines[i + 1].Trim().StartsWith("|"))
                        rows.Add(lines[++i].Trim());
                    target.Children.Add(Table(rows, NextTop()));
                    open = null; gap = false;
                    continue;
                }

                var item = Regex.Match(line, @"^(\s*)([-*+]|\d{1,3}[.)])\s+(.*)$");
                if (item.Success)
                {
                    int indent = item.Groups[1].Value.Replace("\t", "    ").Length / 2;
                    string marker = char.IsDigit(item.Groups[2].Value[0]) ? item.Groups[2].Value.TrimEnd(')').TrimEnd('.') + "." : "•";
                    open = Inline(item.Groups[3].Value, 12.5, FontWeights.Normal);
                    target.Children.Add(ListItem(marker, open, indent, NextTop()));
                    openKind = BlockKind.ListItem; gap = false;
                    continue;
                }

                if (trimmed.StartsWith(">"))
                {
                    string quoteText = trimmed.TrimStart('>').TrimStart();
                    if (open != null && openKind == BlockKind.Quote)
                    {
                        AppendLine(open, quoteText);
                    }
                    else
                    {
                        open = Inline(quoteText, 12.5, FontWeights.Normal);
                        open.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                        var bar = new Border { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 2, 0, 2), Margin = new Thickness(0, NextTop(), 0, 0), Child = open };
                        bar.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                        target.Children.Add(bar);
                        openKind = BlockKind.Quote;
                    }
                    gap = false;
                    continue;
                }

                // A line right under a paragraph or list item continues it on a new line, as on GitHub
                if (open != null && openKind != BlockKind.Quote)
                {
                    AppendLine(open, trimmed);
                    continue;
                }

                open = Inline(trimmed, 12.5, FontWeights.Normal);
                open.Margin = new Thickness(0, NextTop(), 0, 0);
                target.Children.Add(open);
                openKind = BlockKind.Paragraph; gap = false;
            }
        }

        private static void AppendLine(TextBlock block, string markdown)
        {
            block.Inlines.Add(new LineBreak());
            AddInlines(block.Inlines, markdown);
        }

        private static UIElement ListItem(string marker, TextBlock body, int indent, double top)
        {
            var grid = new Grid { Margin = new Thickness(indent * 16, top, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(marker == "•" ? 14 : 22) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var markerText = new TextBlock { Text = marker, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Left };
            markerText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            grid.Children.Add(markerText);

            Grid.SetColumn(body, 1);
            grid.Children.Add(body);
            return grid;
        }

        private static UIElement CodeBlock(string code, double top)
        {
            var text = new TextBlock { Text = code, FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var box = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, top, 0, 0), Child = text };
            box.SetResourceReference(Border.BackgroundProperty, "Surface3Brush");
            return box;
        }

        private static UIElement Table(List<string> rows, double top)
        {
            var grid = new Grid { Margin = new Thickness(0, top, 0, 0) };
            var cells = rows.Select(r => r.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray()).ToList();
            int columns = cells.Max(c => c.Length);
            for (int c = 0; c < columns; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            for (int r = 0; r < cells.Count; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < cells[r].Length; c++)
                {
                    var cell = Inline(cells[r][c], 12.5, r == 0 ? FontWeights.SemiBold : FontWeights.Normal);
                    cell.Margin = new Thickness(0, 2, 16, 2);
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c);
                    grid.Children.Add(cell);
                }
            }
            return grid;
        }

        private static TextBlock Inline(string markdown, double size, FontWeight weight)
        {
            var block = new TextBlock { FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap };
            block.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            AddInlines(block.Inlines, markdown);
            return block;
        }

        private static void AddInlines(InlineCollection inlines, string markdown)
        {
            int pos = 0;
            foreach (Match m in InlineToken.Matches(markdown))
            {
                if (m.Index > pos)
                    inlines.Add(PlainRun(markdown.Substring(pos, m.Index - pos)));

                if (m.Groups[1].Success)
                {
                    // Images cannot be shown here; their alt text keeps the sentence readable
                    if (m.Groups[1].Value.Length > 0)
                        inlines.Add(PlainRun(m.Groups[1].Value));
                }
                else if (m.Groups[2].Success)
                {
                    inlines.Add(Link(m.Groups[2].Value, m.Groups[3].Value));
                }
                else if (m.Groups[4].Success)
                {
                    inlines.Add(new Run(m.Groups[4].Value) { FontFamily = new FontFamily("Consolas") });
                }
                else if (m.Groups[5].Success || m.Groups[6].Success)
                {
                    var bold = new Bold();
                    AddInlines(bold.Inlines, m.Groups[5].Success ? m.Groups[5].Value : m.Groups[6].Value);
                    inlines.Add(bold);
                }
                else if (m.Groups[7].Success)
                {
                    var struck = new Span();
                    struck.TextDecorations = TextDecorations.Strikethrough;
                    AddInlines(struck.Inlines, m.Groups[7].Value);
                    inlines.Add(struck);
                }
                else if (m.Groups[8].Success || m.Groups[9].Success)
                {
                    var italic = new Italic();
                    AddInlines(italic.Inlines, m.Groups[8].Success ? m.Groups[8].Value : m.Groups[9].Value);
                    inlines.Add(italic);
                }
                else
                {
                    string url = m.Groups[10].Success ? m.Groups[10].Value : m.Groups[11].Value;
                    inlines.Add(Link(url, url));
                }
                pos = m.Index + m.Length;
            }
            if (pos < markdown.Length)
                inlines.Add(PlainRun(markdown.Substring(pos)));
        }

        // Backslash escapes ("\*") and the entities GitHub turns into characters
        private static Run PlainRun(string text) =>
            new Run(Regex.Replace(text, @"\\([\\`*_{}\[\]()#+\-.!|~>])", "$1")
                .Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\""));

        private static Inline Link(string label, string target)
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                // In-page anchors ("#tiếng-việt") and relative links lead nowhere here
                return PlainRun(label);
            }

            var link = new Hyperlink(PlainRun(label)) { NavigateUri = uri, ToolTip = uri.AbsoluteUri };
            link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
            link.RequestNavigate += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Changelog: could not open {e.Uri}: {ex.Message}");
                }
                e.Handled = true;
            };
            return link;
        }
    }
}
