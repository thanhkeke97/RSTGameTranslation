using System.IO;
using System.Text.Json;

namespace RSTGameTranslation
{
    /// <summary>
    /// Advanced intelligent character and text block detection system
    /// for grouping text elements into natural reading units.
    /// </summary>
    public class CharacterBlockDetectionManager
    {
        #region Singleton and Configuration

        private static CharacterBlockDetectionManager? _instance;

        // Singleton pattern
        public static CharacterBlockDetectionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CharacterBlockDetectionManager();
                }
                return _instance;
            }
        }

        // Block detection power is obtained from BlockDetectionManager
        private double GetBlockPower() => BlockDetectionManager.Instance.GetBlockDetectionScale();

        // Configuration values
        private readonly Config _config = new Config();

        // Configuration class to keep all thresholds together
        private class Config
        {
            // Character grouping thresholds (base values before scaling)
            public double BaseCharacterHorizontalGap = 2.0;  // Horizontal gap for letter-to-letter
            public double BaseCharacterVerticalGap = 4.0;     // Vertical alignment tolerance for characters

            // Word grouping thresholds
            public double BaseWordHorizontalGap = 3.0;       // Horizontal gap for word-to-word
            public double BaseWordVerticalGap = 6.0;         // Vertical alignment for word-to-word

            // Large gap detection
            public double BaseLargeHorizontalGapThreshold = 40.0; // Large horizontal gap that should split text into separate blocks

            // Line grouping thresholds
            public double BaseLineVerticalGap = 5.0;         // Vertical gap between lines to consider as paragraph
            public double BaseLineFontSizeTolerance = 5.0;    // Max font height difference for lines in same paragraph

            // Paragraph detection
            public double BaseIndentation = 20.0;             // Indentation that suggests a new paragraph
            public double BaseParagraphBreakThreshold = 20.0; // Vertical gap suggesting paragraph break

            // Get scaled values with current block power
            public double GetScaledValue(double baseValue, double blockPower) => baseValue * blockPower;
        }

        // Public methods to adjust configuration
        public void SetBaseCharacterHorizontalGap(double value)
        {
            if (value < 0)
            {
                Console.WriteLine("Character horizontal gap must be positive");
                return;
            }
            _config.BaseCharacterHorizontalGap = value;
        }

        public void SetBaseCharacterVerticalGap(double value)
        {
            if (value < 0)
            {
                Console.WriteLine("Character vertical gap must be positive");
                return;
            }
            _config.BaseCharacterVerticalGap = value;
        }

        public void SetBaseLineVerticalGap(double value)
        {
            if (value < 0)
            {
                //    Console.WriteLine("Line vertical gap must be positive");
                return;
            }
            _config.BaseLineVerticalGap = value;
        }

        #endregion

        // Source language of the OCR result being processed: the configured one, or guessed
        // from its script when auto-detect is selected (see LanguageCatalog.ResolveForText)
        private string _sourceLang = "";

        #region Main Processing Method

        /// <summary>
        /// Process OCR results to identify and group text into natural reading blocks
        /// </summary>
        public JsonElement ProcessCharacterResults(JsonElement resultsElement)
        {
            // Early validation
            if (resultsElement.ValueKind != JsonValueKind.Array || resultsElement.GetArrayLength() == 0)
                return resultsElement;

            try
            {
                // Get current block power for scaling thresholds
                double blockPower = GetBlockPower();
                //Console.WriteLine($"Processing with block power: {blockPower:F2}");

                // PHASE 1: Extract character information from JSON
                var characters = ExtractCharacters(resultsElement);
                _sourceLang = LanguageCatalog.ResolveForText(ConfigManager.Instance.GetSourceLanguage(), characters.Select(c => c.Text));
                //Console.WriteLine($"Extracted {characters.Count} character objects");

                // Detect vertical (tategaki) text orientation for East Asian languages.
                // Vertical text is read top-to-bottom within a column, and columns are read
                // right-to-left. We detect it when the source language is East Asian AND the
                // majority of character/element boxes are taller than they are wide.
                // Japanese manga dialogue is almost always vertical; re-detecting every frame could
                // flip to horizontal on some pages and then the columns would not be grouped at all.
                bool isVertical = (ConfigManager.Instance.IsMangaModeEnabled() && _sourceLang == "ja")
                    || IsVerticalText(characters);
                if (isVertical)
                {
                    Console.WriteLine("Vertical (tategaki) text detected - using right-to-left column ordering");
                }

                // Get minimum letter confidence threshold
                double minLetterConfidence = ConfigManager.Instance.GetMinLetterConfidence();

                // Count how many will be filtered due to low confidence
                int lowConfidenceCount = characters.Count(c => c.Confidence < minLetterConfidence);

                // Remove ALL elements that don't meet the confidence threshold
                characters.RemoveAll(c => c.Confidence < minLetterConfidence);

                // Now separate the remaining high-confidence elements into character and non-character lists
                var nonCharacters = characters.Where(c => !c.IsCharacter || c.IsProcessed).ToList();
                characters = characters.Where(c => c.IsCharacter && !c.IsProcessed).ToList();

                Console.WriteLine($"Filtered out {lowConfidenceCount} elements with confidence < {minLetterConfidence}");

                // PHASE 2: Group characters into words based on proximity
                var words = GroupCharactersIntoWords(characters, blockPower, isVertical);
                //Console.WriteLine($"Grouped characters into {words.Count} words");

                // PHASE 3: Group words into lines based on vertical position
                var lines = GroupWordsIntoLines(words, blockPower, isVertical);
                //Console.WriteLine($"Grouped words into {lines.Count} lines");

                // Filter out low confidence lines
                double minLineConfidence = ConfigManager.Instance.GetMinLineConfidence();
                int lowConfidenceLineCount = lines.Count(l => l.Confidence < minLineConfidence);
                lines = lines.Where(l => l.Confidence >= minLineConfidence).ToList();
                Console.WriteLine($"Filtered out {lowConfidenceLineCount} lines with confidence < {minLineConfidence}");

                // Furigana only repeats the reading of the kanji beside it; left in, it garbles the
                // translation and breaks paragraph grouping (it looks like a much smaller font)
                if (_sourceLang == "ja")
                {
                    lines = RemoveFurigana(lines, isVertical);
                }

                // PHASE 4: Group lines into paragraphs
                var paragraphs = GroupLinesIntoParagraphs(lines, blockPower, isVertical);
                //Console.WriteLine($"Grouped lines into {paragraphs.Count} paragraphs");
                // PHASE 5: Apply manga-specific processing if manga mode is enabled.
                // Skip manga processing for vertical (tategaki) text because GroupLinesIntoParagraphs
                // already orders columns right-to-left correctly, and ProcessMangaSpecificBlocks
                // re-sorts paragraphs by Y which would break the vertical column order.
                if (ConfigManager.Instance.IsMangaModeEnabled() && !isVertical)
                {
                    paragraphs = ProcessMangaSpecificBlocks(paragraphs, blockPower);
                    Console.WriteLine($"After manga processing: {paragraphs.Count} paragraphs");
                }
                // Create JSON output
                return CreateJsonOutput(paragraphs, nonCharacters);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in character block detection: {ex.Message}");
                return resultsElement; // Return original if processing fails
            }
        }

        #endregion

        #region Character Extraction

        /// <summary>
        /// Extract character information from JSON results
        /// </summary>
        private List<TextElement> ExtractCharacters(JsonElement resultsElement)
        {
            var characters = new List<TextElement>();

            for (int i = 0; i < resultsElement.GetArrayLength(); i++)
            {
                JsonElement item = resultsElement[i];

                // Skip if missing required properties
                if (!item.TryGetProperty("text", out JsonElement textElement) ||
                    !item.TryGetProperty("confidence", out JsonElement confElement) ||
                    !item.TryGetProperty("rect", out JsonElement boxElement) ||
                    boxElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                string text = textElement.GetString() ?? "";
                double confidence = confElement.GetDouble();
                bool isCharacter = true;

                // Check if this item has an is_character property
                if (item.TryGetProperty("is_character", out JsonElement isCharElement))
                {
                    isCharacter = isCharElement.GetBoolean();
                }

                // Skip empty text
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                // Calculate bounding box from polygon points
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                var points = new List<Point>();

                for (int p = 0; p < boxElement.GetArrayLength(); p++)
                {
                    if (boxElement[p].ValueKind == JsonValueKind.Array && boxElement[p].GetArrayLength() >= 2)
                    {
                        double pointX = boxElement[p][0].GetDouble();
                        double pointY = boxElement[p][1].GetDouble();

                        points.Add(new Point(pointX, pointY));

                        minX = Math.Min(minX, pointX);
                        minY = Math.Min(minY, pointY);
                        maxX = Math.Max(maxX, pointX);
                        maxY = Math.Max(maxY, pointY);
                    }
                }

                // Create the text element
                var element = new TextElement
                {
                    Text = text,
                    Confidence = confidence,
                    Bounds = new Rect(minX, minY, maxX - minX, maxY - minY),
                    Points = points,
                    IsCharacter = isCharacter,
                    IsProcessed = !isCharacter, // Mark non-characters as already processed
                    OriginalItem = item,
                    ElementType = isCharacter ? ElementType.Character : ElementType.Other,
                    BubbleId = item.TryGetProperty("bubble_id", out JsonElement bubbleElement) && bubbleElement.ValueKind == JsonValueKind.Number
                        ? bubbleElement.GetInt32()
                        : null
                };

                characters.Add(element);
            }

            return characters;
        }

        #endregion

        #region Character to Word Grouping

        /// <summary>
        /// Group characters into words based on horizontal proximity
        /// </summary>
        /// <summary>
        /// Detects whether the text is vertical (tategaki) for East Asian languages.
        /// Returns true when the source language is East Asian (ja/zh/ko) AND the majority
        /// of element boxes are taller than they are wide.
        /// </summary>
        private bool IsVerticalText(List<TextElement> elements)
        {
            if (elements == null || elements.Count == 0)
                return false;

            bool isEastAsian = LanguageCatalog.IsEastAsian(_sourceLang);
            if (!isEastAsian)
                return false;

            // Use multiple signals to detect vertical (tategaki) text robustly.
            // Single-signal detection (Height > Width per char) misses often because:
            //  - Many CJK chars are nearly square (Height ~= Width)
            //  - Punctuation/small kana can be wider than tall
            //  - OCR jitter can flip a char's aspect ratio
            //
            // Signals:
            //   1. Median character aspect ratio (Height / Width) — vertical CJK tends to be >= 1.0
            //   2. Overall bounding region aspect ratio — vertical text regions are usually taller than wide
            //   3. Count of "tall" chars (Height > Width * 0.9) — tolerant of near-square chars
            //   4. Columnar layout: chars stack vertically (large Y span) within narrow X bands

            var valid = elements.Where(e => e.Bounds.Width > 0 && e.Bounds.Height > 0).ToList();
            if (valid.Count == 0)
                return false;

            // Signal 1: median aspect ratio (Height / Width)
            var aspectRatios = valid.Select(e => e.Bounds.Height / e.Bounds.Width).OrderBy(r => r).ToList();
            double medianAspect = aspectRatios[aspectRatios.Count / 2];

            // Signal 2: overall region aspect ratio
            double minX = valid.Min(e => e.Bounds.X);
            double maxX = valid.Max(e => e.Bounds.X + e.Bounds.Width);
            double minY = valid.Min(e => e.Bounds.Y);
            double maxY = valid.Max(e => e.Bounds.Y + e.Bounds.Height);
            double regionWidth = maxX - minX;
            double regionHeight = maxY - minY;
            double regionAspect = regionWidth > 0 ? regionHeight / regionWidth : 0;

            // Signal 3: count of tall chars (tolerant of near-square)
            int tallCount = valid.Count(e => e.Bounds.Height > e.Bounds.Width * 0.9);
            double tallRatio = (double)tallCount / valid.Count;

            // Signal 4: columnar layout — group chars by X proximity and check if columns
            // are tall (Y span >> X span). This is the strongest layout signal.
            bool isColumnar = IsColumnarLayout(valid);

            // Decision: vertical if multiple signals agree.
            // - Strong signal: columnar layout alone is enough (it directly reflects tategaki)
            // - Otherwise: medianAspect >= 1.0 AND (regionAspect > 0.8 OR tallRatio >= 0.4)
            if (isColumnar)
                return true;

            if (medianAspect >= 1.0 && (regionAspect > 0.8 || tallRatio >= 0.4))
                return true;

            // Japanese specifically uses vertical text very commonly — lower the bar.
            if (_sourceLang == "ja" && medianAspect >= 0.9 && tallRatio >= 0.4)
                return true;

            return false;
        }

        /// <summary>
        /// Detects whether characters form vertical columns (tategaki layout).
        /// Groups characters by X proximity and checks if the resulting columns are
        /// tall (Y span significantly larger than X width). Returns true when the layout
        /// is columnar rather than row-based.
        /// </summary>
        private bool IsColumnarLayout(List<TextElement> elements)
        {
            if (elements.Count < 4)
                return false;

            // Sort by X center
            var sorted = elements
                .Select(e => new { Element = e, CenterX = e.Bounds.X + e.Bounds.Width / 2.0 })
                .OrderBy(x => x.CenterX)
                .ToList();

            // Group into columns by X proximity. Use median char width as the tolerance.
            var widths = elements.Select(e => e.Bounds.Width).OrderBy(w => w).ToList();
            double medianWidth = widths[widths.Count / 2];
            double columnTolerance = Math.Max(medianWidth * 1.5, 10.0);

            var columns = new List<List<TextElement>>();
            var currentCol = new List<TextElement> { sorted[0].Element };
            double currentColCenterX = sorted[0].CenterX;

            for (int i = 1; i < sorted.Count; i++)
            {
                if (Math.Abs(sorted[i].CenterX - currentColCenterX) <= columnTolerance)
                {
                    currentCol.Add(sorted[i].Element);
                }
                else
                {
                    columns.Add(currentCol);
                    currentCol = new List<TextElement> { sorted[i].Element };
                    currentColCenterX = sorted[i].CenterX;
                }
            }
            columns.Add(currentCol);

            // Filter to columns with at least 2 chars (real columns, not single outliers)
            var realColumns = columns.Where(c => c.Count >= 2).ToList();
            if (realColumns.Count == 0)
                return false;

            // A column is "tall" if its Y span is larger than its X width.
            int tallColumns = 0;
            foreach (var col in realColumns)
            {
                double colMinX = col.Min(e => e.Bounds.X);
                double colMaxX = col.Max(e => e.Bounds.X + e.Bounds.Width);
                double colMinY = col.Min(e => e.Bounds.Y);
                double colMaxY = col.Max(e => e.Bounds.Y + e.Bounds.Height);
                double colWidth = colMaxX - colMinX;
                double colHeight = colMaxY - colMinY;

                if (colHeight > colWidth * 1.5 && colHeight > 0)
                    tallColumns++;
            }

            // Vertical layout if majority of real columns are tall.
            return tallColumns * 2 >= realColumns.Count;
        }

        private List<TextElement> GroupCharactersIntoWords(List<TextElement> characters, double blockPower, bool isVertical)
        {
            if (characters.Count == 0)
                return new List<TextElement>();

            // Get threshold values with scaling applied
            double horizontalGapThreshold = _config.GetScaledValue(_config.BaseCharacterHorizontalGap, blockPower);
            double verticalGapThreshold = _config.GetScaledValue(_config.BaseCharacterVerticalGap, blockPower);

            // Adjust thresholds based on source language
            bool isEastAsianLangForChars = LanguageCatalog.IsEastAsian(_sourceLang);

            // For Western languages, use smaller character gap to avoid splitting words
            if (!isEastAsianLangForChars)
            {
                // For languages like English, we need a smaller gap as letters should be closer together
                horizontalGapThreshold = Math.Max(5, horizontalGapThreshold * 0.5);
            }

            // First, sort characters by vertical position to identify lines
            var charactersWithCenters = characters.Select(c =>
            {
                c.CenterY = c.Bounds.Y + (c.Bounds.Height / 2);
                return c;
            }).ToList();

            // Group characters into lines based on position.
            //  - Horizontal text: group by vertical proximity (Y) -> horizontal lines.
            //  - Vertical text: group by horizontal proximity (X) -> vertical columns.
            //    Within a column, characters are later ordered top-to-bottom by Y.
            var sortedChars = isVertical
                ? charactersWithCenters.OrderBy(c => c.Bounds.X).ToList()
                : charactersWithCenters.OrderBy(c => c.Bounds.Y).ToList();

            var lines = new List<List<TextElement>>();
            if (sortedChars.Count > 0)
            {
                var currentLine = new List<TextElement> { sortedChars[0] };
                lines.Add(currentLine);

                for (int i = 1; i < sortedChars.Count; i++)
                {
                    var currentChar = sortedChars[i];
                    var lastCharInLine = currentLine.Last();

                    if (isVertical)
                    {
                        // Vertical text: characters belong to the same column when their X
                        // centers are close. OCR engines that return whole column segments (OneOCR)
                        // jitter by more than a couple of pixels, so also accept centers within half
                        // the narrower element's width.
                        double diffX = Math.Abs((currentChar.Bounds.X + currentChar.Bounds.Width / 2.0) -
                                                (lastCharInLine.Bounds.X + lastCharInLine.Bounds.Width / 2.0));
                        double columnTolerance = Math.Max(horizontalGapThreshold,
                            Math.Min(currentChar.Bounds.Width, lastCharInLine.Bounds.Width) * 0.5);
                        if (diffX < columnTolerance)
                        {
                            currentLine.Add(currentChar);
                        }
                        else
                        {
                            currentLine = new List<TextElement> { currentChar };
                            lines.Add(currentLine);
                        }
                    }
                    else
                    {
                        double diffY = Math.Abs(currentChar.CenterY - lastCharInLine.CenterY);

                        bool isOverlapping = currentChar.Bounds.Y < (lastCharInLine.Bounds.Y + lastCharInLine.Bounds.Height * 0.5);

                        if (diffY < verticalGapThreshold || isOverlapping)
                        {
                            currentLine.Add(currentChar);
                        }
                        else
                        {
                            currentLine = new List<TextElement> { currentChar };
                            lines.Add(currentLine);
                        }
                    }
                }
            }

            // Process each line to form words
            var words = new List<TextElement>();
            int lineIndex = 0;

            foreach (var line in lines)
            {
                // Order characters within the line in reading order.
                //  - Horizontal text: left-to-right (by X).
                //  - Vertical text: top-to-bottom within a column (by Y).
                var lineCharacters = isVertical
                    ? line.OrderBy(c => c.Bounds.Y).ToList()
                    : line.OrderBy(c => c.Bounds.X).ToList();
                var lineHeight = lineCharacters.Average(c => c.Bounds.Height);
                TextElement? currentWord = null;

                foreach (var character in lineCharacters)
                {
                    character.LineIndex = lineIndex;

                    if (currentWord == null)
                    {
                        // Start a new word with this character
                        currentWord = new TextElement
                        {
                            Text = character.Text,
                            Confidence = character.Confidence,
                            Bounds = character.Bounds.Clone(),
                            Points = new List<Point>(character.Points),
                            LineIndex = lineIndex,
                            ElementType = ElementType.Word,
                            Children = new List<TextElement> { character },
                            CenterY = character.CenterY
                        };
                    }
                    else
                    {
                        // Calculate gap between current character and the current word.
                        //  - Horizontal text: horizontal gap (X).
                        //  - Vertical text: vertical gap (Y) between stacked characters.
                        double gap = isVertical
                            ? character.Bounds.Y - (currentWord.Bounds.Y + currentWord.Bounds.Height)
                            : character.Bounds.X - (currentWord.Bounds.X + currentWord.Bounds.Width);

                        // Check if this character should be part of the current word
                        if (gap <= (isVertical ? verticalGapThreshold : horizontalGapThreshold))
                        {
                            // Add to current word
                            currentWord.Text += character.Text;

                            // Update word bounds
                            double minX = Math.Min(currentWord.Bounds.X, character.Bounds.X);
                            double minY = Math.Min(currentWord.Bounds.Y, character.Bounds.Y);
                            double maxX = Math.Max(currentWord.Bounds.X + currentWord.Bounds.Width,
                                            character.Bounds.X + character.Bounds.Width);
                            double maxY = Math.Max(currentWord.Bounds.Y + currentWord.Bounds.Height,
                                            character.Bounds.Y + character.Bounds.Height);

                            // Cập nhật bounds của từ hiện tại
                            currentWord.Bounds.X = minX;
                            currentWord.Bounds.Y = minY;
                            currentWord.Bounds.Width = maxX - minX;
                            currentWord.Bounds.Height = maxY - minY;

                            // Add to children
                            currentWord.Children.Add(character);
                        }
                        else
                        {
                            // Finish current word and add to list
                            words.Add(currentWord);

                            // Start a new word
                            currentWord = new TextElement
                            {
                                Text = character.Text,
                                Confidence = character.Confidence,
                                Bounds = character.Bounds.Clone(),
                                Points = new List<Point>(character.Points),
                                LineIndex = lineIndex,
                                ElementType = ElementType.Word,
                                Children = new List<TextElement> { character },
                                CenterY = character.CenterY
                            };
                        }
                    }

                    // Mark as processed
                    character.IsProcessed = true;
                }

                // Add the last word of the line
                if (currentWord != null)
                {
                    words.Add(currentWord);
                }

                lineIndex++;
            }

            return words;
        }

        #endregion

        #region Word to Line Grouping

        /// <summary>
        /// Group words into lines based on vertical position and alignment
        /// </summary>
        private List<TextElement> GroupWordsIntoLines(List<TextElement> words, double blockPower, bool isVertical)
        {
            if (words.Count == 0)
                return new List<TextElement>();

            // Get threshold values with scaling applied
            double wordHorizontalGapThreshold = _config.GetScaledValue(_config.BaseWordHorizontalGap, blockPower);
            double wordVerticalGapThreshold = _config.GetScaledValue(_config.BaseWordVerticalGap, blockPower);

            // Adjust thresholds based on source language
            bool isEastAsianLangForWords = LanguageCatalog.IsEastAsian(_sourceLang);

            // For Western languages, use larger word gaps to ensure proper spacing
            if (!isEastAsianLangForWords)
            {
                // For languages like English, increase word gap to better identify word boundaries
                wordHorizontalGapThreshold = Math.Max(15, wordHorizontalGapThreshold * 0.8);
            }

            // Group words by their already assigned line index
            var lineGroups = words
                .GroupBy(w => w.LineIndex)
                .OrderBy(g => g.Key)
                .ToList();

            var lines = new List<TextElement>();

            // Get large gap threshold value
            double largeHorizontalGapThreshold = _config.GetScaledValue(_config.BaseLargeHorizontalGapThreshold, blockPower);

            foreach (var lineGroup in lineGroups)
            {
                // Sort words in reading order within the line.
                //  - Horizontal text: left-to-right (by X).
                //  - Vertical text: top-to-bottom within a column (by Y).
                var lineWords = isVertical
                    ? lineGroup.OrderBy(w => w.Bounds.Y).ToList()
                    : lineGroup.OrderBy(w => w.Bounds.X).ToList();

                // Check for large horizontal gaps and split the line if needed
                List<List<TextElement>> splitLines = new List<List<TextElement>>();
                List<TextElement> currentSegment = new List<TextElement>();
                TextElement? previousWord = null;

                // Split the line if there are large gaps along the reading axis.
                //  - Horizontal text: large horizontal (X) gap.
                //  - Vertical text: large vertical (Y) gap between stacked words.
                foreach (var word in lineWords)
                {
                    if (previousWord != null)
                    {
                        double gap;
                        double averageCharSize;
                        if (isVertical)
                        {
                            // Vertical: gap along Y between stacked words
                            gap = word.Bounds.Y - (previousWord.Bounds.Y + previousWord.Bounds.Height);
                            averageCharSize = (word.Bounds.Height / Math.Max(1, word.Text.Length) +
                                               previousWord.Bounds.Height / Math.Max(1, previousWord.Text.Length)) / 2.0;
                        }
                        else
                        {
                            // Horizontal: gap along X between adjacent words
                            gap = word.Bounds.X - (previousWord.Bounds.X + previousWord.Bounds.Width);
                            averageCharSize = (word.Bounds.Width / Math.Max(1, word.Text.Length) +
                                               previousWord.Bounds.Width / Math.Max(1, previousWord.Text.Length)) / 2.0;
                        }

                        // Check if the gap exceeds the threshold or is unusually large compared to character size
                        // Also split where the line crosses from one detected speech bubble into another
                        int? wordBubble = GetBubbleId(word);
                        int? previousBubble = GetBubbleId(previousWord);
                        bool crossesBubbles = wordBubble != null && previousBubble != null && wordBubble != previousBubble;

                        if (crossesBubbles || gap > largeHorizontalGapThreshold || gap > (averageCharSize * 10))
                        {
                            // Gap is large enough to split the line
                            if (currentSegment.Count > 0)
                            {
                                splitLines.Add(currentSegment);
                                currentSegment = new List<TextElement>();
                            }
                        }
                    }

                    // Add the word to the current segment
                    currentSegment.Add(word);
                    previousWord = word;
                }

                // Add the last segment if it exists
                if (currentSegment.Count > 0)
                {
                    splitLines.Add(currentSegment);
                }

                // If no large gaps were found, we'll have just one segment with all words
                // Otherwise, we'll have multiple segments to create separate lines
                foreach (var segment in splitLines)
                {
                    // Create a line element for each segment
                    var line = new TextElement
                    {
                        ElementType = ElementType.Line,
                        LineIndex = lineGroup.Key,
                        Children = segment.ToList()
                    };

                    // Set line bounds based on segment words
                    double minX = double.MaxValue;
                    double minY = double.MaxValue;
                    double maxX = double.MinValue;
                    double maxY = double.MinValue;

                    foreach (var word in segment)
                    {
                        minX = Math.Min(minX, word.Bounds.X);
                        minY = Math.Min(minY, word.Bounds.Y);
                        maxX = Math.Max(maxX, word.Bounds.X + word.Bounds.Width);
                        maxY = Math.Max(maxY, word.Bounds.Y + word.Bounds.Height);
                    }

                    line.Bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
                    line.CenterY = minY + (maxY - minY) / 2;

                    // Combine all text with appropriate separators based on language
                    bool isEastAsian = LanguageCatalog.IsEastAsian(_sourceLang);

                    if (isEastAsian)
                    {
                        // For East Asian languages, join without spaces
                        line.Text = string.Join("", segment.Select(w => w.Text));
                    }
                    else
                    {
                        // For Western languages, join with spaces
                        line.Text = string.Join(" ", segment.Select(w => w.Text));
                    }

                    // Average confidence
                    line.Confidence = segment.Average(w => w.Confidence);

                    lines.Add(line);
                }
            }

            return lines;
        }

        #endregion

        #region Line to Paragraph Grouping

        /// <summary>
        /// Group lines into paragraphs based on spacing, indentation, and font size
        /// </summary>
        private List<TextElement> GroupLinesIntoParagraphs(List<TextElement> lines, double blockPower, bool isVertical)
        {
            if (lines.Count == 0)
                return new List<TextElement>();

            // Vertical text has its own rules: the horizontal ones below use fixed pixel thresholds
            // and line-start indentation, which misfire on columns of varying width and centered bubbles
            if (isVertical)
            {
                // Pages with vertical dialogue still have horizontal headings, captions and labels; as
                // "columns" their large width would make every threshold huge and bridge whole bubbles
                var horizontalLines = lines.Where(IsHorizontalLine).ToList();
                var columns = lines.Where(l => !IsHorizontalLine(l)).ToList();
                return GroupColumnsIntoParagraphs(columns)
                    .Concat(GroupLinesIntoParagraphs(horizontalLines, blockPower, false))
                    .OrderBy(p => p.Bounds.Y).ThenByDescending(p => p.Bounds.X)
                    .ToList();
            }

            // Lines inside a detected speech bubble (manga mode) form one paragraph per bubble. The rules
            // below compare each line with the previous one in Y order, so on a full page the lines of
            // side-by-side bubbles interleave and get split or merged by distance alone.
            var bubbleLines = lines.Where(l => GetBubbleId(l) != null).ToList();
            if (bubbleLines.Count > 0)
            {
                var bubbleParagraphs = bubbleLines
                    .GroupBy(l => GetBubbleId(l))
                    .Select(g => BuildParagraph(g.OrderBy(l => l.Bounds.Y + l.Bounds.Height / 2).ThenBy(l => l.Bounds.X)))
                    .ToList();
                var looseLines = lines.Where(l => GetBubbleId(l) == null).ToList();
                return bubbleParagraphs
                    .Concat(GroupLinesIntoParagraphs(looseLines, blockPower, false))
                    .OrderBy(p => p.Bounds.Y).ThenBy(p => p.Bounds.X)
                    .ToList();
            }

            // Get threshold values with scaling applied
            double lineVerticalGapThreshold = _config.GetScaledValue(_config.BaseLineVerticalGap, blockPower);
            double fontSizeTolerance = _config.GetScaledValue(_config.BaseLineFontSizeTolerance, blockPower);
            double indentationThreshold = _config.GetScaledValue(_config.BaseIndentation, blockPower);
            double paragraphBreakThreshold = _config.GetScaledValue(_config.BaseParagraphBreakThreshold, blockPower);

            // Sort lines in reading order.
            //  - Horizontal text: top-to-bottom (by Y).
            //  - Vertical text: columns right-to-left (by X descending).
            var sortedLines = isVertical
                ? lines.OrderByDescending(l => l.Bounds.X).ToList()
                : lines.OrderBy(l => l.Bounds.Y).ToList();
            var paragraphs = new List<TextElement>();
            TextElement? currentParagraph = null;

            foreach (var line in sortedLines)
            {
                if (currentParagraph == null)
                {
                    // Start a new paragraph with this line
                    currentParagraph = new TextElement
                    {
                        ElementType = ElementType.Paragraph,
                        Bounds = line.Bounds.Clone(),
                        Children = new List<TextElement> { line },
                        Text = line.Text,
                        Confidence = line.Confidence
                    };
                }
                else
                {
                    bool startNewParagraph = false;

                    // Get the last line in the paragraph to properly calculate gaps
                    var lastLine = currentParagraph.Children.Last();

                    // Distance between consecutive lines along the reading axis.
                    //  - Horizontal text: vertical (Y) distance between stacked lines.
                    //  - Vertical text: horizontal (X) distance between adjacent columns.
                    double centerDistance;
                    double averageSize;
                    double normalSpacing;
                    double axisGap;
                    double indent;
                    double centerAlignedTextIndent;

                    if (isVertical)
                    {
                        // Vertical text: columns are read right-to-left, so the "next" column
                        // is to the left of the previous one. Use X distance (positive = leftward).
                        double lastLineCenterX = lastLine.Bounds.X + (lastLine.Bounds.Width * 0.5);
                        double currentLineCenterX = line.Bounds.X + (line.Bounds.Width * 0.5);
                        centerDistance = lastLineCenterX - currentLineCenterX; // distance to the left

                        averageSize = (lastLine.Bounds.Width + line.Bounds.Width) * 0.5;
                        normalSpacing = averageSize * ConfigManager.Instance.GetLineSpacingFactor();
                        axisGap = centerDistance - normalSpacing;

                        // "Indentation" for vertical text = vertical offset between column tops
                        indent = line.Bounds.Y - lastLine.Bounds.Y;

                        // For center-aligned text, also check X-center proximity with tighter threshold.
                        double prevLineCenterY = lastLine.Bounds.Y + (lastLine.Bounds.Height * 0.5);
                        double currLineCenterY = line.Bounds.Y + (line.Bounds.Height * 0.5);
                        centerAlignedTextIndent = currLineCenterY - prevLineCenterY;
                    }
                    else
                    {
                        // Horizontal text: lines stack top-to-bottom. Use Y distance.
                        double lastLineCenterY = lastLine.Bounds.Y + (lastLine.Bounds.Height * 0.5);
                        double currentLineCenterY = line.Bounds.Y + (line.Bounds.Height * 0.5);
                        centerDistance = currentLineCenterY - lastLineCenterY;

                        averageSize = (lastLine.Bounds.Height + line.Bounds.Height) * 0.5;
                        normalSpacing = averageSize * ConfigManager.Instance.GetLineSpacingFactor();
                        axisGap = centerDistance - normalSpacing;

                        // Indentation for horizontal text = horizontal offset between line starts
                        indent = line.Bounds.X - lastLine.Bounds.X;

                        // For center-aligned text, also check X-center proximity with tighter threshold.
                        double prevLineCenterX = lastLine.Bounds.X + (lastLine.Bounds.Width * 0.5);
                        double currLineCenterX = line.Bounds.X + (line.Bounds.Width * 0.5);
                        centerAlignedTextIndent = currLineCenterX - prevLineCenterX;
                    }

                    // Large center distance indicates paragraph break
                    if (centerDistance > (averageSize * ConfigManager.Instance.GetLineSpacingFactor() * 2.5) + paragraphBreakThreshold)
                    {
                        startNewParagraph = true;
                        Console.WriteLine("New paragraph: Large gap detected");
                    }
                    // Moderate gap more than normal line spacing threshold indicates line break
                    else if (axisGap > lineVerticalGapThreshold || centerDistance > (averageSize * ConfigManager.Instance.GetLineSpacingFactor() * 2.0))
                    {
                        startNewParagraph = true;
                        Console.WriteLine("New paragraph: Line spacing exceeded threshold");
                    }

                    // Check indentation - significant indent may indicate new paragraph.


                    if (Math.Abs(indent) > indentationThreshold)
                    {


                        // Left edges are far apart — but maybe it's center-aligned text?
                        if (Math.Abs(centerAlignedTextIndent) > indentationThreshold * 0.5)
                        {
                            startNewParagraph = true;
                        }
                    }

                    // Check font size consistency.
                    //  - Horizontal text: font size ~ line Height.
                    //  - Vertical text: font size ~ column Width.
                    double lastLineFontSize = isVertical ? lastLine.Bounds.Width : lastLine.Bounds.Height;
                    double currentLineFontSize = isVertical ? line.Bounds.Width : line.Bounds.Height;
                    double fontSizeDiff = Math.Abs(currentLineFontSize - lastLineFontSize);
                    if (fontSizeDiff > fontSizeTolerance)
                    {
                        // Different font sizes suggest different paragraphs
                        startNewParagraph = true;
                    }

                    // Font size too small might indicate a caption, header, or other special text
                    double fontSizeRatio = currentLineFontSize / Math.Max(1.0, lastLineFontSize);
                    if (fontSizeRatio < 0.7 || fontSizeRatio > 1.3)
                    {
                        startNewParagraph = true;
                    }

                    if (startNewParagraph)
                    {
                        // Add completed paragraph to the list
                        paragraphs.Add(currentParagraph);

                        // Start a new paragraph
                        currentParagraph = new TextElement
                        {
                            ElementType = ElementType.Paragraph,
                            Bounds = line.Bounds.Clone(),
                            Children = new List<TextElement> { line },
                            Text = line.Text,
                            Confidence = line.Confidence
                        };
                    }
                    else
                    {
                        // Add line to current paragraph
                        currentParagraph.Children.Add(line);

                        // Update paragraph text - add newlines between lines
                        currentParagraph.Text += "\n";

                        // Add appropriate separator based on language
                        // For East Asian languages (Japanese, Chinese, Korean), don't add space
                        bool isEastAsianLangForParagraphs = LanguageCatalog.IsEastAsian(_sourceLang);

                        if (!isEastAsianLangForParagraphs &&
                            !currentParagraph.Text.EndsWith(" ") &&
                            !currentParagraph.Text.EndsWith("\n"))
                        {
                            // For Western languages, add space between lines
                            currentParagraph.Text += " ";
                        }

                        // Add the line text
                        currentParagraph.Text += line.Text;


                        // Update paragraph bounds
                        double minX = Math.Min(currentParagraph.Bounds.X, line.Bounds.X);
                        double minY = Math.Min(currentParagraph.Bounds.Y, line.Bounds.Y);
                        double maxX = Math.Max(currentParagraph.Bounds.X + currentParagraph.Bounds.Width,
                                        line.Bounds.X + line.Bounds.Width);
                        double maxY = Math.Max(currentParagraph.Bounds.Y + currentParagraph.Bounds.Height,
                                        line.Bounds.Y + line.Bounds.Height);

                        currentParagraph.Bounds.X = minX;
                        currentParagraph.Bounds.Y = minY;
                        currentParagraph.Bounds.Width = maxX - minX;
                        currentParagraph.Bounds.Height = maxY - minY;
                    }
                }
            }

            // Add the last paragraph
            if (currentParagraph != null)
            {
                paragraphs.Add(currentParagraph);
            }

            return paragraphs;
        }

        // Vertical (tategaki) grouping thresholds, relative to the columns' glyph size (em)
        private const double ColumnMaxGapEm = 1.0;          // max horizontal gap between neighbouring columns of one paragraph
        private const double ColumnSegmentMaxGapEm = 1.5;   // max vertical gap between two OCR segments of one column
        private const double ColumnMinOverlapRatio = 0.5;   // neighbouring columns share at least this much of the shorter one's height
        private const double ColumnSizeMinRatio = 0.6;      // min width ratio (narrower / wider) between two columns of one paragraph

        /// <summary>
        /// Group vertical columns into paragraphs (speech bubbles). Columns are compared pairwise and joined
        /// transitively, so a paragraph's growing bounds can't swallow the next bubble, and bubbles that
        /// interleave on a full page don't split each other. Bubble ids found in the image win over geometry.
        /// </summary>
        private List<TextElement> GroupColumnsIntoParagraphs(List<TextElement> lines)
        {
            int count = lines.Count;
            int[] parent = Enumerable.Range(0, count).ToArray();
            int Find(int i)
            {
                while (parent[i] != i)
                    i = parent[i] = parent[parent[i]];
                return i;
            }

            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    if (Find(i) != Find(j) && CanJoinColumns(lines[i], lines[j]))
                        parent[Find(i)] = Find(j);

            var paragraphs = new List<TextElement>();
            foreach (var group in Enumerable.Range(0, count).GroupBy(Find))
            {
                // Read columns right-to-left; OCR may split one column into segments, keep those top-to-bottom
                var columns = new List<List<TextElement>>();
                foreach (var line in group.Select(i => lines[i]).OrderByDescending(l => l.Bounds.X + l.Bounds.Width / 2))
                {
                    var column = columns.LastOrDefault();
                    if (column != null && column.Any(c => IsSameColumn(c, line)))
                        column.Add(line);
                    else
                        columns.Add(new List<TextElement> { line });
                }

                paragraphs.Add(BuildParagraph(columns.SelectMany(c => c.OrderBy(l => l.Bounds.Y))));
            }

            int inBubbles = lines.Count(l => GetBubbleId(l) != null);
            Console.WriteLine($"Vertical grouping: {count} columns ({inBubbles} inside detected bubbles) -> {paragraphs.Count} paragraphs");

            // Manga page order: top to bottom, then right to left
            return paragraphs.OrderBy(p => p.Bounds.Y).ThenByDescending(p => p.Bounds.X).ToList();
        }

        // Whether two columns belong to the same paragraph
        private static bool CanJoinColumns(TextElement a, TextElement b)
        {
            // Different speech bubbles never join, however close; the same bubble always does
            int? bubbleA = GetBubbleId(a);
            int? bubbleB = GetBubbleId(b);
            if (bubbleA != null && bubbleB != null)
                return bubbleA == bubbleB;

            // Glyph size; 1-2 character columns (punctuation, small kana) have unreliable widths
            bool aReliable = a.Text.Length > 2;
            bool bReliable = b.Text.Length > 2;
            double em = aReliable && !bReliable ? a.Bounds.Width
                      : bReliable && !aReliable ? b.Bounds.Width
                      : Math.Max(a.Bounds.Width, b.Bounds.Width);
            if (em <= 0)
                return false;

            // Two OCR segments of one column: only the vertical gap matters
            if (IsSameColumn(a, b))
            {
                var (upper, lower) = a.Bounds.Y <= b.Bounds.Y ? (a, b) : (b, a);
                double gapY = lower.Bounds.Y - (upper.Bounds.Y + upper.Bounds.Height);
                return gapY <= em * ColumnSegmentMaxGapEm;
            }

            var (right, left) = a.Bounds.X + a.Bounds.Width / 2 >= b.Bounds.X + b.Bounds.Width / 2 ? (a, b) : (b, a);
            double gapX = right.Bounds.X - (left.Bounds.X + left.Bounds.Width);
            if (gapX > em * ColumnMaxGapEm)
                return false;

            // Columns in a bubble overlap vertically even when centered or of different lengths
            double overlapY = Math.Min(a.Bounds.Y + a.Bounds.Height, b.Bounds.Y + b.Bounds.Height) - Math.Max(a.Bounds.Y, b.Bounds.Y);
            if (overlapY < Math.Min(a.Bounds.Height, b.Bounds.Height) * ColumnMinOverlapRatio)
                return false;

            if (aReliable && bReliable &&
                Math.Min(a.Bounds.Width, b.Bounds.Width) < Math.Max(a.Bounds.Width, b.Bounds.Width) * ColumnSizeMinRatio)
                return false;

            return true;
        }

        // A multi-character line clearly wider than tall reads horizontally (1-character boxes are ambiguous)
        private static bool IsHorizontalLine(TextElement line) =>
            line.Text.Trim().Length >= 2 && line.Bounds.Width > line.Bounds.Height * 1.2;

        // Bubble id of an element or, for grouped elements, of the first OCR element inside it
        private static int? GetBubbleId(TextElement element) =>
            element.BubbleId ?? element.Children.Select(GetBubbleId).FirstOrDefault(id => id != null);

        // Two vertical segments belong to one column when they overlap horizontally by half the narrower one
        private static bool IsSameColumn(TextElement a, TextElement b)
        {
            double overlapX = Math.Min(a.Bounds.X + a.Bounds.Width, b.Bounds.X + b.Bounds.Width) - Math.Max(a.Bounds.X, b.Bounds.X);
            return overlapX >= Math.Min(a.Bounds.Width, b.Bounds.Width) * 0.5;
        }

        // One paragraph from lines already in reading order: texts joined by newlines, bounds unioned
        private static TextElement BuildParagraph(IEnumerable<TextElement> orderedLines)
        {
            var lines = orderedLines.ToList();
            double minX = lines.Min(l => l.Bounds.X);
            double minY = lines.Min(l => l.Bounds.Y);
            double maxX = lines.Max(l => l.Bounds.X + l.Bounds.Width);
            double maxY = lines.Max(l => l.Bounds.Y + l.Bounds.Height);

            return new TextElement
            {
                ElementType = ElementType.Paragraph,
                Bounds = new Rect(minX, minY, maxX - minX, maxY - minY),
                Children = lines,
                Text = string.Join("\n", lines.Select(l => l.Text)),
                Confidence = lines[0].Confidence
            };
        }

        private static double Median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
        }

        /// <summary>
        /// Drop furigana lines: kana-only text about half the size of the line beside it
        /// (right of a vertical column, above a horizontal line), spanning only part of that line.
        /// </summary>
        private static List<TextElement> RemoveFurigana(List<TextElement> lines, bool isVertical)
        {
            if (lines.Count < 2)
                return lines;

            var furigana = new HashSet<TextElement>();
            foreach (var ruby in lines)
            {
                if (!IsKanaOnly(ruby.Text))
                    continue;

                double rubySize = isVertical ? ruby.Bounds.Width : ruby.Bounds.Height;
                foreach (var main in lines)
                {
                    double mainSize = isVertical ? main.Bounds.Width : main.Bounds.Height;
                    if (main == ruby || rubySize > mainSize * 0.6)
                        continue;

                    // Distance from the main line to the side where furigana is placed
                    double gap = isVertical
                        ? ruby.Bounds.X - (main.Bounds.X + main.Bounds.Width)
                        : main.Bounds.Y - (ruby.Bounds.Y + ruby.Bounds.Height);
                    if (gap < -rubySize * 0.5 || gap > mainSize * 0.5)
                        continue;

                    // Furigana lies within the main line along the reading axis
                    double rubyStart = isVertical ? ruby.Bounds.Y : ruby.Bounds.X;
                    double rubyLength = isVertical ? ruby.Bounds.Height : ruby.Bounds.Width;
                    double mainStart = isVertical ? main.Bounds.Y : main.Bounds.X;
                    double mainLength = isVertical ? main.Bounds.Height : main.Bounds.Width;
                    double overlap = Math.Min(rubyStart + rubyLength, mainStart + mainLength) - Math.Max(rubyStart, mainStart);
                    if (overlap < rubyLength * 0.7)
                        continue;

                    furigana.Add(ruby);
                    Console.WriteLine($"Removed furigana '{ruby.Text}' beside '{main.Text}'");
                    break;
                }
            }

            return furigana.Count == 0 ? lines : lines.Where(l => !furigana.Contains(l)).ToList();
        }

        // Hiragana, katakana and the long vowel mark (U+3040-U+30FF)
        private static bool IsKanaOnly(string text)
        {
            string trimmed = text.Trim();
            return trimmed.Length > 0 && trimmed.All(c => c >= '぀' && c <= 'ヿ');
        }

        #endregion

        #region Json Output Creation

        /// <summary>
        /// Create JSON output from processed paragraphs
        /// </summary>
        private JsonElement CreateJsonOutput(List<TextElement> paragraphs, List<TextElement> nonCharacters)
        {
            // Get minimum text fragment size from config
            int minTextFragmentSize = ConfigManager.Instance.GetMinTextFragmentSize();

            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartArray();

                    // Add paragraphs to output, filtering out those that are too small
                    foreach (var paragraph in paragraphs)
                    {
                        // Skip paragraphs with text smaller than the minimum fragment size
                        if (paragraph.Text.Length < minTextFragmentSize)
                        {
                            continue;
                        }

                        writer.WriteStartObject();

                        // Write paragraph text and confidence
                        writer.WriteString("text", paragraph.Text);
                        writer.WriteNumber("confidence", paragraph.Confidence);

                        // Write bounding box rectangle as a polygon with 4 corners
                        writer.WriteStartArray("rect");

                        // Top-left
                        writer.WriteStartArray();
                        writer.WriteNumberValue(paragraph.Bounds.X);
                        writer.WriteNumberValue(paragraph.Bounds.Y);
                        writer.WriteEndArray();

                        // Top-right
                        writer.WriteStartArray();
                        writer.WriteNumberValue(paragraph.Bounds.X + paragraph.Bounds.Width);
                        writer.WriteNumberValue(paragraph.Bounds.Y);
                        writer.WriteEndArray();

                        // Bottom-right
                        writer.WriteStartArray();
                        writer.WriteNumberValue(paragraph.Bounds.X + paragraph.Bounds.Width);
                        writer.WriteNumberValue(paragraph.Bounds.Y + paragraph.Bounds.Height);
                        writer.WriteEndArray();

                        // Bottom-left
                        writer.WriteStartArray();
                        writer.WriteNumberValue(paragraph.Bounds.X);
                        writer.WriteNumberValue(paragraph.Bounds.Y + paragraph.Bounds.Height);
                        writer.WriteEndArray();

                        writer.WriteEndArray(); // End rect

                        // Add metadata
                        writer.WriteNumber("line_count", paragraph.Children.Count);
                        writer.WriteString("element_type", "paragraph");

                        // Lets later merging keep text from different speech bubbles apart
                        int? bubbleId = GetBubbleId(paragraph);
                        if (bubbleId != null)
                        {
                            writer.WriteNumber("bubble_id", bubbleId.Value);
                        }

                        writer.WriteEndObject();
                    }

                    // Add non-character elements - all low confidence elements were already removed earlier
                    foreach (var element in nonCharacters)
                    {
                        if (element.OriginalItem.ValueKind != JsonValueKind.Undefined)
                        {
                            element.OriginalItem.WriteTo(writer);
                        }
                    }

                    writer.WriteEndArray();
                    writer.Flush();

                    // Parse and return the JSON
                    stream.Position = 0;
                    using (JsonDocument doc = JsonDocument.Parse(stream))
                    {
                        return doc.RootElement.Clone();
                    }
                }
            }
        }

        #region Manga-specific Block Detection

        /// <summary>
        /// Detect and process special text blocks for manga
        /// </summary>
        private const int MAX_PARAGRAPHS_PER_BUBBLE = 10;
        private const double MAX_BUBBLE_WIDTH_RATIO = 0.8;
        private const double MAX_BUBBLE_HEIGHT_RATIO = 0.35;

        private List<TextElement> ProcessMangaSpecificBlocks(List<TextElement> paragraphs, double blockPower)
        {
            // Skip if there aren't enough paragraphs to process
            if (paragraphs.Count <= 1)
                return paragraphs;

            // Check if manga mode is enabled
            bool isMangaMode = ConfigManager.Instance.IsMangaModeEnabled();
            if (!isMangaMode)
                return paragraphs;

            Console.WriteLine("Applying manga-specific block detection");

            // Detect main reading direction (left-to-right or right-to-left)
            var readingDirection = DetectReadingDirection(paragraphs, out double directionConfidence);
            bool isRightToLeft = readingDirection == MangaReadingDirection.RightToLeft;

            // Adjust thresholds based on manga characteristics
            double medianHeight = paragraphs.Select(p => p.Bounds.Height).OrderBy(h => h).ElementAt(paragraphs.Count / 2);
            double medianWidth = paragraphs.Select(p => p.Bounds.Width).OrderBy(w => w).ElementAt(paragraphs.Count / 2);

            double mangaVerticalThreshold = Math.Min(_config.BaseLineVerticalGap * blockPower, medianHeight * 0.9);
            double mangaHorizontalThreshold = Math.Max(_config.BaseWordHorizontalGap * blockPower, medianWidth * 1.1);

            // Sort paragraphs by position according to reading direction
            var sortedParagraphs = isRightToLeft
                ? paragraphs.OrderBy(p => p.Bounds.Y).ThenByDescending(p => p.Bounds.X).ToList()
                : paragraphs.OrderBy(p => p.Bounds.Y).ThenBy(p => p.Bounds.X).ToList();

            // Find and group paragraphs belonging to the same speech bubble
            var speechBubbles = DetectSpeechBubbles(sortedParagraphs, mangaVerticalThreshold, mangaHorizontalThreshold, medianHeight, medianWidth);
            Console.WriteLine($"Manga detection: direction={(isRightToLeft ? "RTL" : "LTR")}, confidence={directionConfidence:F2}, paragraphs={paragraphs.Count}, bubbles={speechBubbles.Count}");

            // If speech bubbles were detected, use them
            if (speechBubbles.Count > 0)
            {
                var mergedParagraphs = new List<TextElement>();

                // Process each speech bubble
                foreach (var bubble in speechBubbles)
                {
                    if (bubble.Count == 1)
                    {
                        // If there's only one paragraph, add it directly
                        mergedParagraphs.Add(bubble[0]);
                    }
                    else
                    {
                        // Group multiple paragraphs into one
                        var mergedBubble = MergeParagraphs(bubble, isRightToLeft);
                        mergedParagraphs.Add(mergedBubble);
                    }
                }

                return mergedParagraphs;
            }

            return paragraphs;
        }

        /// <summary>
        /// Detect reading direction based on text block positions
        /// </summary>
        private enum MangaReadingDirection
        {
            LeftToRight,
            RightToLeft
        }

        private MangaReadingDirection DetectReadingDirection(List<TextElement> paragraphs, out double confidence)
        {
            // Count paragraphs on right and left sides
            int rightSideCount = 0;
            int leftSideCount = 0;

            // Calculate horizontal midpoint
            double totalWidth = paragraphs.Max(p => p.Bounds.X + p.Bounds.Width) -
                            paragraphs.Min(p => p.Bounds.X);
            double centerX = paragraphs.Min(p => p.Bounds.X) + (totalWidth / 2);

            double weightedSum = 0;
            double totalWeight = 0;

            foreach (var para in paragraphs)
            {
                double paraCenter = para.Bounds.X + (para.Bounds.Width / 2);
                double weight = Math.Max(para.Bounds.Width * para.Bounds.Height, 1);
                totalWeight += weight;

                if (paraCenter > centerX)
                {
                    rightSideCount++;
                    weightedSum += weight;
                }
                else
                {
                    leftSideCount++;
                }
            }

            if (totalWeight == 0)
            {
                confidence = 0;
                return MangaReadingDirection.LeftToRight;
            }

            double rightRatio = weightedSum / totalWeight;
            confidence = Math.Abs(rightRatio - 0.5) * 2; // normalize 0..1

            if (confidence < 0.15)
            {
                return MangaReadingDirection.LeftToRight;
            }

            // If there are more paragraphs on the right side, it might be right-to-left manga
            return rightSideCount > leftSideCount ? MangaReadingDirection.RightToLeft : MangaReadingDirection.LeftToRight;
        }

        /// <summary>
        /// Detect speech bubbles based on relative positions of paragraphs
        /// </summary>
        private List<List<TextElement>> DetectSpeechBubbles(List<TextElement> paragraphs,
                                                        double verticalThreshold,
                                                        double horizontalThreshold,
                                                        double medianHeight,
                                                        double medianWidth)
        {
            var bubbles = new List<List<TextElement>>();
            var processed = new HashSet<TextElement>();

            // Sort paragraphs by Y position to process from top to bottom
            var sortedParagraphs = paragraphs.OrderBy(p => p.Bounds.Y).ToList();

            // Calculate average distance between paragraphs
            double avgHeight = paragraphs.Average(p => p.Bounds.Height);

            // Adjust distance thresholds - use lower thresholds to avoid excessive grouping
            double safeVerticalThreshold = Math.Min(verticalThreshold, avgHeight * 0.6);
            safeVerticalThreshold = Math.Max(safeVerticalThreshold, medianHeight * 0.6);

            double safeHorizontalThreshold = Math.Max(horizontalThreshold * 0.6, medianWidth * 1.0);

            Console.WriteLine($"Speech bubble detection thresholds: V={safeVerticalThreshold:F1}, H={safeHorizontalThreshold:F1}");

            foreach (var para in sortedParagraphs)
            {
                if (processed.Contains(para))
                    continue;

                var bubble = new List<TextElement> { para };
                processed.Add(para);

                // Find the closest paragraphs
                foreach (var other in sortedParagraphs)
                {
                    if (processed.Contains(other) || other == para)
                        continue;

                    double deltaY = Math.Abs(other.Bounds.Y - para.Bounds.Y);
                    if (deltaY > safeVerticalThreshold * 2 && other.Bounds.Y > para.Bounds.Y)
                    {
                        // Remaining paragraphs are too far vertically since list is sorted
                        break;
                    }

                    // Calculate distance between paragraph centers
                    double centerY1 = para.Bounds.Y + (para.Bounds.Height / 2);
                    double centerX1 = para.Bounds.X + (para.Bounds.Width / 2);

                    double centerY2 = other.Bounds.Y + (other.Bounds.Height / 2);
                    double centerX2 = other.Bounds.X + (other.Bounds.Width / 2);

                    double verticalDistance = Math.Abs(centerY2 - centerY1);
                    double horizontalDistance = Math.Abs(centerX2 - centerX1);

                    // Check for horizontal overlap
                    bool overlapsX = (para.Bounds.X < other.Bounds.X + other.Bounds.Width) &&
                                    (para.Bounds.X + para.Bounds.Width > other.Bounds.X);

                    // Check for horizontal alignment
                    bool alignedHorizontally = Math.Abs(para.Bounds.X - other.Bounds.X) < safeHorizontalThreshold * 0.3 ||
                                            Math.Abs((para.Bounds.X + para.Bounds.Width) -
                                                    (other.Bounds.X + other.Bounds.Width)) < safeHorizontalThreshold * 0.3;

                    // Only group paragraphs that are very close to each other
                    bool shouldGroup = false;

                    // Condition 1: If horizontally overlapping and very close vertically
                    if (overlapsX && verticalDistance < safeVerticalThreshold)
                    {
                        shouldGroup = true;
                    }
                    // Condition 2: If well-aligned horizontally and close vertically
                    else if (alignedHorizontally && verticalDistance < safeVerticalThreshold * 1.2)
                    {
                        shouldGroup = true;
                    }
                    // Condition 3: If very close in both dimensions
                    else if (verticalDistance < safeVerticalThreshold * 0.8 &&
                            horizontalDistance < safeHorizontalThreshold * 0.8)
                    {
                        shouldGroup = true;
                    }

                    // Apply absolute distance limit to avoid grouping paragraphs that are too far apart
                    double absoluteMaxDistance = Math.Max(para.Bounds.Height * 1.3, Math.Max(avgHeight, medianHeight) * 2);
                    if (verticalDistance > absoluteMaxDistance)
                    {
                        shouldGroup = false;
                    }

                    // Paragraphs inside two different detected bubbles never group, however close
                    int? paraBubble = GetBubbleId(para);
                    int? otherBubble = GetBubbleId(other);
                    if (paraBubble != null && otherBubble != null && paraBubble != otherBubble)
                    {
                        shouldGroup = false;
                    }

                    if (shouldGroup)
                    {
                        bubble.Add(other);
                        processed.Add(other);
                        Console.WriteLine($"Grouped paragraph: \"{other.Text.Substring(0, Math.Min(20, other.Text.Length))}...\"");
                    }
                }

                // Add speech bubble to the list
                bubbles.Add(bubble);
            }

            // Check results to ensure no speech bubbles are too large
            var validatedBubbles = ValidateSpeechBubbles(bubbles, paragraphs);

            return validatedBubbles;
        }

        /// <summary>
        /// Validate speech bubbles to avoid creating oversized blocks
        /// </summary>
        private List<List<TextElement>> ValidateSpeechBubbles(List<List<TextElement>> bubbles, List<TextElement> originalParagraphs)
        {
            var result = new List<List<TextElement>>();

            // Calculate average page size
            double pageWidth = originalParagraphs.Max(p => p.Bounds.X + p.Bounds.Width) -
                            originalParagraphs.Min(p => p.Bounds.X);
            double pageHeight = originalParagraphs.Max(p => p.Bounds.Y + p.Bounds.Height) -
                            originalParagraphs.Min(p => p.Bounds.Y);

            // Maximum size limits for a speech bubble
            double maxBubbleWidth = pageWidth * MAX_BUBBLE_WIDTH_RATIO;  // Maximum 40% of page width
            double maxBubbleHeight = pageHeight * MAX_BUBBLE_HEIGHT_RATIO; // Maximum 35% of page height

            foreach (var bubble in bubbles)
            {
                // If only 1 paragraph, keep as is
                if (bubble.Count <= 1)
                {
                    result.Add(bubble);
                    continue;
                }

                // Calculate speech bubble size
                double minX = bubble.Min(p => p.Bounds.X);
                double minY = bubble.Min(p => p.Bounds.Y);
                double maxX = bubble.Max(p => p.Bounds.X + p.Bounds.Width);
                double maxY = bubble.Max(p => p.Bounds.Y + p.Bounds.Height);

                double bubbleWidth = maxX - minX;
                double bubbleHeight = maxY - minY;

                // Check if the speech bubble is too large
                if (bubbleWidth > maxBubbleWidth || bubbleHeight > maxBubbleHeight || bubble.Count > MAX_PARAGRAPHS_PER_BUBBLE)
                {
                    Console.WriteLine($"Breaking up large speech bubble: {bubbleWidth:F0}x{bubbleHeight:F0} exceeds limits");
                    if (bubble.Count > MAX_PARAGRAPHS_PER_BUBBLE)
                    {
                        Console.WriteLine($"Bubble exceeded paragraph limit ({bubble.Count}/{MAX_PARAGRAPHS_PER_BUBBLE})");
                    }

                    // Break up oversized speech bubbles
                    foreach (var para in bubble)
                    {
                        result.Add(new List<TextElement> { para });
                    }
                }
                else
                {
                    // Speech bubble has reasonable size
                    result.Add(bubble);
                }
            }

            return result;
        }

        /// <summary>
        /// Merge multiple paragraphs into a single paragraph
        /// </summary>
        private TextElement MergeParagraphs(List<TextElement> paragraphs, bool isRightToLeft)
        {
            // Sort by reading position
            var sortedParagraphs = isRightToLeft
                ? paragraphs.OrderByDescending(p => p.Bounds.X).ThenBy(p => p.Bounds.Y).ToList()
                : paragraphs.OrderBy(p => p.Bounds.Y).ThenBy(p => p.Bounds.X).ToList();

            // Create new paragraph from sorted paragraphs
            var merged = new TextElement
            {
                ElementType = ElementType.Paragraph,
                Children = new List<TextElement>(),
                Text = ""
            };

            // Calculate new bounds
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            // Combine text and calculate average confidence
            double totalConfidence = 0;

            foreach (var para in sortedParagraphs)
            {
                // Update bounds
                minX = Math.Min(minX, para.Bounds.X);
                minY = Math.Min(minY, para.Bounds.Y);
                maxX = Math.Max(maxX, para.Bounds.X + para.Bounds.Width);
                maxY = Math.Max(maxY, para.Bounds.Y + para.Bounds.Height);

                // Add text
                if (!string.IsNullOrEmpty(merged.Text))
                {
                    merged.Text += isRightToLeft ? "\n" : " ";
                }
                merged.Text += para.Text.Trim();

                // Add to children list
                merged.Children.AddRange(para.Children);

                // Update confidence
                totalConfidence += para.Confidence;
            }

            // Update bounds and confidence
            merged.Bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
            merged.Confidence = totalConfidence / sortedParagraphs.Count;

            return merged;
        }

        #endregion
        #endregion

        #region Helper Classes

        // Element type enum for clear identification
        private enum ElementType
        {
            Character,
            Word,
            Line,
            Paragraph,
            Other
        }

        // Point class for polygon coordinates
        private class Point
        {
            public double X { get; set; }
            public double Y { get; set; }

            public Point(double x, double y)
            {
                X = x;
                Y = y;
            }
        }

        // Rectangle class for element bounds
        private class Rect
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }

            public Rect(double x, double y, double width, double height)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }

            public Rect Clone()
            {
                return new Rect(X, Y, Width, Height);
            }
        }

        // Text element class used throughout the pipeline
        private class TextElement
        {
            // Basic properties
            public string Text { get; set; } = "";
            public double Confidence { get; set; }
            public Rect Bounds { get; set; } = new Rect(0, 0, 0, 0);
            public List<Point> Points { get; set; } = new List<Point>();

            // Type and state
            public ElementType ElementType { get; set; } = ElementType.Other;
            public bool IsCharacter { get; set; }
            public bool IsProcessed { get; set; }

            // Hierarchy
            public int LineIndex { get; set; } = -1;
            public List<TextElement> Children { get; set; } = new List<TextElement>();

            // Position and measurement
            public double CenterY { get; set; }

            // Original JSON element
            public JsonElement OriginalItem { get; set; }

            // Speech bubble this OCR element sits in (manga mode), null when unknown
            public int? BubbleId { get; set; }
        }

        #endregion
    }

    public class BlockDetectionManager
    {
        private static BlockDetectionManager? _instance;

        // Singleton pattern
        public static BlockDetectionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new BlockDetectionManager();
                }
                return _instance;
            }
        }

        // Configuration parameters for block detection
        private double _scaleModToApplyToAllBlockDetectionParameters; // Global scale modifier for all parameters

        // Constructor - load values from config
        private BlockDetectionManager()
        {
            // Load values from config - these will use defaults if not found in config
            _scaleModToApplyToAllBlockDetectionParameters = ConfigManager.Instance.GetBlockDetectionScale();

            Console.WriteLine($"Loaded block detection scale from config: {_scaleModToApplyToAllBlockDetectionParameters}");
            Console.WriteLine($"Loaded block detection settle time from config: {ConfigManager.Instance.GetBlockDetectionSettleTime()} seconds");
        }

        // Base threshold values (before scaling)
        private readonly double _baseVerticalProximityThreshold = 6.0; // Maximum vertical distance to consider text in the same paragraph
        private readonly double _baseHorizontalAlignmentThreshold = 13.0; // Maximum difference in left edge position to consider horizontally aligned
        private readonly double _baseParagraphBreakThreshold = 7.0; // Vertical gap that indicates a paragraph break
        private readonly double _baseIndentationThreshold = 15.0; // Horizontal indentation that might indicate a paragraph's first line
        private readonly double _baseIsolatedTextThreshold = 30.0; // Width threshold to identify isolated text like buttons
        private readonly double _baseHorizontalGapThreshold = 30.0; // Maximum horizontal gap between text chunks to consider them part of the same line
        private double _baseHorizontalXPositionThreshold = 10.0; // Maximum difference in X starting positions to consider text in the same paragraph


        /// <summary>
        /// Set the horizontal X position threshold
        /// </summary>
        /// <param name="threshold">Maximum difference in X starting positions to consider text in the same paragraph</param>
        public void SetHorizontalXPositionThreshold(double threshold)
        {
            if (threshold < 0)
            {
                Console.WriteLine($"Invalid horizontal X position threshold: {threshold}. Must be non-negative. Using default.");
                return;
            }

            _baseHorizontalXPositionThreshold = threshold;
            double scaledThreshold = _baseHorizontalXPositionThreshold * _scaleModToApplyToAllBlockDetectionParameters;
            Console.WriteLine($"Horizontal X position threshold set to {threshold} (scaled: {scaledThreshold:F1})");
        }

        /// <summary>
        /// Set the global scale for all block detection parameters
        /// </summary>
        /// <param name="scale">Scale factor (1.0 is default, higher values for larger text/images)</param>
        private const double DEFAULT_BLOCK_SCALE = 1.0;

        public void SetBlockDetectionScale(double scale)
        {
            if (scale <= 0)
            {
                Console.WriteLine($"Invalid block detection scale: {scale}. Must be positive. Reverting to default ({DEFAULT_BLOCK_SCALE}).");
                _scaleModToApplyToAllBlockDetectionParameters = DEFAULT_BLOCK_SCALE;
                ConfigManager.Instance.SetBlockDetectionScale(DEFAULT_BLOCK_SCALE);
            }
            else
            {
                _scaleModToApplyToAllBlockDetectionParameters = scale;
                // Save to config to persist between sessions
                ConfigManager.Instance.SetBlockDetectionScale(scale);

                // Calculate and log the new threshold values
                double verticalProximityThreshold = _baseVerticalProximityThreshold * _scaleModToApplyToAllBlockDetectionParameters;
                double horizontalAlignmentThreshold = _baseHorizontalAlignmentThreshold * _scaleModToApplyToAllBlockDetectionParameters;
                double paragraphBreakThreshold = _baseParagraphBreakThreshold * _scaleModToApplyToAllBlockDetectionParameters;
                double indentationThreshold = _baseIndentationThreshold * _scaleModToApplyToAllBlockDetectionParameters;
                double isolatedTextThreshold = _baseIsolatedTextThreshold * _scaleModToApplyToAllBlockDetectionParameters;
                double horizontalGapThreshold = _baseHorizontalGapThreshold * _scaleModToApplyToAllBlockDetectionParameters;
                double xPositionDiffThreshold = _baseHorizontalXPositionThreshold * _scaleModToApplyToAllBlockDetectionParameters;

                /*
                 Console.WriteLine($"Block detection scale set to {scale}. " +
                     $"New thresholds: Vertical={verticalProximityThreshold}, " +
                     $"Horizontal={horizontalAlignmentThreshold}, " +
                     $"Break={paragraphBreakThreshold}, " +
                     $"Indentation={indentationThreshold}, " +
                     $"HorizontalGap={horizontalGapThreshold}, " +
                     $"HorizontalXPos={xPositionDiffThreshold}, " +
                     $"Isolated={isolatedTextThreshold}");
                */
            }
        }

        /// <summary>
        /// Get the current block detection scale factor
        /// </summary>
        public double GetBlockDetectionScale()
        {
            return _scaleModToApplyToAllBlockDetectionParameters;
        }

        /// <summary>
        /// Auto-adjust the block detection scale based on image size and content
        /// </summary>
        public void AutoAdjustBlockDetectionScale(JsonElement resultsElement)
        {

            float scaleFactorToApplyToAudoFinalAutoScale = 1.0f;
            try
            {
                if (resultsElement.ValueKind != JsonValueKind.Array || resultsElement.GetArrayLength() == 0)
                    return;

                // Calculate average text size in the document
                double avgHeight = 0;
                double avgWidth = 0;
                int textBlockCount = 0;

                for (int i = 0; i < resultsElement.GetArrayLength(); i++)
                {
                    JsonElement item = resultsElement[i];

                    if (item.TryGetProperty("rect", out JsonElement boxElement) &&
                        boxElement.ValueKind == JsonValueKind.Array)
                    {
                        // Calculate bounding box
                        double minX = double.MaxValue, minY = double.MaxValue;
                        double maxX = double.MinValue, maxY = double.MinValue;

                        for (int p = 0; p < boxElement.GetArrayLength(); p++)
                        {
                            if (boxElement[p].ValueKind == JsonValueKind.Array &&
                                boxElement[p].GetArrayLength() >= 2)
                            {
                                double pointX = boxElement[p][0].GetDouble();
                                double pointY = boxElement[p][1].GetDouble();

                                minX = Math.Min(minX, pointX);
                                minY = Math.Min(minY, pointY);
                                maxX = Math.Max(maxX, pointX);
                                maxY = Math.Max(maxY, pointY);
                            }
                        }

                        // Add to averages
                        double width = maxX - minX;
                        double height = maxY - minY;

                        if (width > 0 && height > 0)
                        {
                            avgWidth += width;
                            avgHeight += height;
                            textBlockCount++;
                        }
                    }
                }

                // Calculate averages
                if (textBlockCount > 0)
                {
                    avgWidth /= textBlockCount;
                    avgHeight /= textBlockCount;

                    // Base scale is calibrated for text around 20px high
                    // Adjust if the average differs significantly
                    double baseHeight = 20.0; // Base height the thresholds were calibrated for
                    double scaleFactor = avgHeight / baseHeight;

                    // Apply with some limits to avoid extreme values
                    scaleFactor = Math.Max(0.1, Math.Min(20.0, scaleFactor));

                    scaleFactor *= scaleFactorToApplyToAudoFinalAutoScale;
                    // Only update if the new scale is significantly different
                    if (Math.Abs(scaleFactor - _scaleModToApplyToAllBlockDetectionParameters) > 0.25)
                    {
                        Console.WriteLine($"Auto-adjusting block detection scale to {scaleFactor:F2} (avg text height: {avgHeight:F1}px)");
                        _scaleModToApplyToAllBlockDetectionParameters = scaleFactor;
                        ConfigManager.Instance.SetBlockDetectionScale(scaleFactor);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error auto-adjusting block detection scale: {ex.Message}");
                // Keep existing scale on failure
            }
        }

        /// <summary>
        /// Apply block detection algorithm to group related text lines and return new JSON
        /// </summary>
        public JsonElement ApplyBlockDetectionToJson(JsonElement resultsElement)
        {
            if (resultsElement.ValueKind != JsonValueKind.Array || resultsElement.GetArrayLength() == 0)
                return resultsElement; // Return original if no results

            // Get minimum text fragment size from config
            int minTextFragmentSize = ConfigManager.Instance.GetMinTextFragmentSize();

            // Step 1: Extract and sort text blocks by vertical position (y-coordinate)
            var textBlocks = new List<TextBlockInfo>();

            for (int i = 0; i < resultsElement.GetArrayLength(); i++)
            {
                JsonElement item = resultsElement[i];

                if (item.TryGetProperty("text", out JsonElement textElement) &&
                    item.TryGetProperty("confidence", out JsonElement confElement) &&
                    item.TryGetProperty("rect", out JsonElement boxElement) &&
                    boxElement.ValueKind == JsonValueKind.Array)
                {
                    string text = textElement.GetString() ?? "";
                    double confidence = confElement.GetDouble();


                    // Calculate bounding box from points
                    double minX = double.MaxValue, minY = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue;

                    // Store original polygon points for later
                    var originalPoints = new List<double[]>();

                    for (int p = 0; p < boxElement.GetArrayLength(); p++)
                    {
                        if (boxElement[p].ValueKind == JsonValueKind.Array &&
                            boxElement[p].GetArrayLength() >= 2)
                        {
                            double pointX = boxElement[p][0].GetDouble();
                            double pointY = boxElement[p][1].GetDouble();

                            originalPoints.Add(new double[] { pointX, pointY });

                            minX = Math.Min(minX, pointX);
                            minY = Math.Min(minY, pointY);
                            maxX = Math.Max(maxX, pointX);
                            maxY = Math.Max(maxY, pointY);
                        }
                    }

                    // Create a text block info object and add to list
                    textBlocks.Add(new TextBlockInfo
                    {
                        Index = i,
                        Text = text,
                        Confidence = confidence,
                        X = minX,
                        Y = minY,
                        Width = maxX - minX,
                        Height = maxY - minY,
                        IsProcessed = false,
                        OriginalItem = item,
                        OriginalPoints = originalPoints
                    });
                }
            }

            // First group blocks by their approximate Y position (lines)
            double verticalProximityThreshold = _baseVerticalProximityThreshold * _scaleModToApplyToAllBlockDetectionParameters;
            double xPositionDiffThreshold = _baseHorizontalXPositionThreshold * _scaleModToApplyToAllBlockDetectionParameters;

            // Group first by approximate Y position, creating initial line groups
            var initialLineGroups = textBlocks
                .GroupBy(b => Math.Round(b.Y / verticalProximityThreshold))
                .OrderBy(g => g.Key)
                .ToList();

            // Create a refined grouping that also considers X position differences
            var groupedByLine = new List<List<TextBlockInfo>>();

            // Process each initial line group
            foreach (var lineGroup in initialLineGroups)
            {
                var lineBlocks = lineGroup.OrderBy(b => b.X).ToList();
                var currentLineGroup = new List<TextBlockInfo>();
                TextBlockInfo? lastBlock = null;

                // Split the line into separate groups if X positions differ significantly
                foreach (var block in lineBlocks)
                {
                    if (lastBlock == null)
                    {
                        // Start a new line group
                        currentLineGroup.Add(block);
                    }
                    else
                    {
                        // Check X position difference
                        double xDiff = Math.Abs(block.X - lastBlock.X);

                        // Split blocks with significant X position differences
                        if (xDiff > xPositionDiffThreshold)
                        {
                            // X position differs too much, consider as separate line
                            groupedByLine.Add(currentLineGroup);
                            currentLineGroup = new List<TextBlockInfo> { block };
                        }
                        else
                        {
                            // Add to current line group
                            currentLineGroup.Add(block);
                        }
                    }

                    lastBlock = block;
                }

                // Add the last line group
                if (currentLineGroup.Count > 0)
                {
                    groupedByLine.Add(currentLineGroup);
                }
            }

            // Combine line groups into aggregated blocks
            var aggregatedBlocks = new List<TextBlockInfo>();
            string sourceLang = LanguageCatalog.ResolveForText(ConfigManager.Instance.GetSourceLanguage(), textBlocks.Select(b => b.Text));
            bool isEastAsianLang = LanguageCatalog.IsEastAsian(sourceLang);

            foreach (var lineGroup in groupedByLine)
            {
                var orderedBlocks = lineGroup.OrderBy(b => b.X).ToList();
                if (orderedBlocks.Count == 0)
                {
                    continue;
                }

                var cleanedTexts = orderedBlocks
                    .Select(b => b.Text?.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                if (cleanedTexts.Count == 0)
                {
                    continue;
                }

                string combinedText = isEastAsianLang
                    ? string.Concat(cleanedTexts)
                    : string.Join(" ", cleanedTexts);

                combinedText = combinedText.Trim();

                if (combinedText.Length < minTextFragmentSize)
                {
                    continue;
                }

                double minX = orderedBlocks.Min(b => b.X);
                double minY = orderedBlocks.Min(b => b.Y);
                double maxX = orderedBlocks.Max(b => b.X + b.Width);
                double maxY = orderedBlocks.Max(b => b.Y + b.Height);

                var aggregated = new TextBlockInfo
                {
                    Text = combinedText,
                    Confidence = orderedBlocks.Average(b => b.Confidence),
                    X = minX,
                    Y = minY,
                    Width = maxX - minX,
                    Height = maxY - minY,
                    OriginalBlocks = orderedBlocks
                };

                aggregatedBlocks.Add(aggregated);
            }

            if (aggregatedBlocks.Count == 0)
            {
                // Nothing aggregated; return original results
                return resultsElement;
            }

            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartArray();

                    foreach (var block in aggregatedBlocks)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("text", block.Text);
                        writer.WriteNumber("confidence", block.Confidence);

                        writer.WriteStartArray("rect");

                        // Top-left
                        writer.WriteStartArray();
                        writer.WriteNumberValue(block.X);
                        writer.WriteNumberValue(block.Y);
                        writer.WriteEndArray();

                        // Top-right
                        writer.WriteStartArray();
                        writer.WriteNumberValue(block.X + block.Width);
                        writer.WriteNumberValue(block.Y);
                        writer.WriteEndArray();

                        // Bottom-right
                        writer.WriteStartArray();
                        writer.WriteNumberValue(block.X + block.Width);
                        writer.WriteNumberValue(block.Y + block.Height);
                        writer.WriteEndArray();

                        // Bottom-left
                        writer.WriteStartArray();
                        writer.WriteNumberValue(block.X);
                        writer.WriteNumberValue(block.Y + block.Height);
                        writer.WriteEndArray();

                        writer.WriteEndArray(); // rect

                        writer.WriteNumber("block_count", block.OriginalBlocks?.Count ?? 1);
                        writer.WriteString("element_type", "block");

                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    writer.Flush();
                }

                stream.Position = 0;
                using (JsonDocument doc = JsonDocument.Parse(stream))
                {
                    return doc.RootElement.Clone();
                }
            }
        }



        /// <summary>
        /// Class to hold text block information for processing
        /// </summary>
        public class TextBlockInfo
        {
            public int Index { get; set; }
            public string Text { get; set; } = "";
            public double Confidence { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public bool IsProcessed { get; set; }
            public List<TextBlockInfo>? OriginalBlocks { get; set; } // For tracking merged blocks
            public JsonElement OriginalItem { get; set; } // Original JSON item
            public List<double[]> OriginalPoints { get; set; } = new List<double[]>(); // Original or new polygon points
        }
    }
}