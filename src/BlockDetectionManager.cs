using System.Text.Json;
using System.IO;

namespace UGTLive
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
            public double BaseCharacterVerticalGap = 8.0;     // Vertical alignment tolerance for characters
            
            // Word grouping thresholds
            public double BaseWordHorizontalGap = 3.0;       // Horizontal gap for word-to-word
            public double BaseWordVerticalGap = 10.0;         // Vertical alignment for word-to-word
            
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
            if (value < 0) {
                Console.WriteLine("Character horizontal gap must be positive");
                return;
            }
            _config.BaseCharacterHorizontalGap = value;
        }
        
        public void SetBaseCharacterVerticalGap(double value)
        {
            if (value < 0) {
                Console.WriteLine("Character vertical gap must be positive");
                return;
            }
            _config.BaseCharacterVerticalGap = value;
        }
        
        public void SetBaseLineVerticalGap(double value)
        {
            if (value < 0) {
                Console.WriteLine("Line vertical gap must be positive");
                return;
            }
            _config.BaseLineVerticalGap = value;
        }
        
        #endregion
        
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
                //Console.WriteLine($"Extracted {characters.Count} character objects");
                
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
                var words = GroupCharactersIntoWords(characters, blockPower);
                //Console.WriteLine($"Grouped characters into {words.Count} words");
                
                // PHASE 3: Group words into lines based on vertical position
                var lines = GroupWordsIntoLines(words, blockPower);
                //Console.WriteLine($"Grouped words into {lines.Count} lines");
                
                // Filter out low confidence lines
                double minLineConfidence = ConfigManager.Instance.GetMinLineConfidence();
                int lowConfidenceLineCount = lines.Count(l => l.Confidence < minLineConfidence);
                lines = lines.Where(l => l.Confidence >= minLineConfidence).ToList();
                Console.WriteLine($"Filtered out {lowConfidenceLineCount} lines with confidence < {minLineConfidence}");
                
                // PHASE 4: Group lines into paragraphs
                var paragraphs = GroupLinesIntoParagraphs(lines, blockPower);
                //Console.WriteLine($"Grouped lines into {paragraphs.Count} paragraphs");
                
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
                    ElementType = isCharacter ? ElementType.Character : ElementType.Other
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
        private List<TextElement> GroupCharactersIntoWords(List<TextElement> characters, double blockPower)
        {
            if (characters.Count == 0)
                return new List<TextElement>();
                
            // Get threshold values with scaling applied
            double horizontalGapThreshold = _config.GetScaledValue(_config.BaseCharacterHorizontalGap, blockPower);
            double verticalGapThreshold = _config.GetScaledValue(_config.BaseCharacterVerticalGap, blockPower);
            
            // Adjust thresholds based on source language
            string sourceLangForChars = ConfigManager.Instance.GetSourceLanguage();
            bool isEastAsianLangForChars = sourceLangForChars == "ja" || 
                                          sourceLangForChars == "ch_sim" || 
                                          sourceLangForChars == "ch_tra" || 
                                          sourceLangForChars == "ko";
                                      
            // For Western languages, use smaller character gap to avoid splitting words
            if (!isEastAsianLangForChars)
            {
                // For languages like English, we need a smaller gap as letters should be closer together
                horizontalGapThreshold = Math.Max(5, horizontalGapThreshold * 0.5);
            }
            
            // First, sort characters by vertical position to identify lines
            var charactersWithCenters = characters.Select(c => {
                c.CenterY = c.Bounds.Y + (c.Bounds.Height / 2);
                return c;
            }).ToList();
            
            // Group characters into lines based on vertical position
            var lines = charactersWithCenters
                .GroupBy(c => Math.Round(c.CenterY / verticalGapThreshold))
                .OrderBy(g => g.Key)
                .ToList();
                
            // Process each line to form words
            var words = new List<TextElement>();
            int lineIndex = 0;
            
            foreach (var line in lines)
            {
                // Sort by X position within the line
                var lineCharacters = line.OrderBy(c => c.Bounds.X).ToList();
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
                        // Calculate horizontal gap
                        double horizontalGap = character.Bounds.X - (currentWord.Bounds.X + currentWord.Bounds.Width);
                        
                        // Check if this character should be part of the current word
                        if (horizontalGap <= horizontalGapThreshold)
                        {
                            // Add to current word
                            currentWord.Text += character.Text;
                            
                            // Update word bounds
                            double right = Math.Max(currentWord.Bounds.X + currentWord.Bounds.Width, 
                                               character.Bounds.X + character.Bounds.Width);
                            double bottom = Math.Max(currentWord.Bounds.Y + currentWord.Bounds.Height, 
                                               character.Bounds.Y + character.Bounds.Height);
                                               
                            currentWord.Bounds.Width = right - currentWord.Bounds.X;
                            currentWord.Bounds.Height = bottom - currentWord.Bounds.Y;
                            
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
        private List<TextElement> GroupWordsIntoLines(List<TextElement> words, double blockPower)
        {
            if (words.Count == 0)
                return new List<TextElement>();
                
            // Get threshold values with scaling applied
            double wordHorizontalGapThreshold = _config.GetScaledValue(_config.BaseWordHorizontalGap, blockPower);
            double wordVerticalGapThreshold = _config.GetScaledValue(_config.BaseWordVerticalGap, blockPower);
            
            // Adjust thresholds based on source language
            string sourceLangForWords = ConfigManager.Instance.GetSourceLanguage();
            bool isEastAsianLangForWords = sourceLangForWords == "ja" || 
                                          sourceLangForWords == "ch_sim" || 
                                          sourceLangForWords == "ch_tra" || 
                                          sourceLangForWords == "ko";
                                      
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
                // Sort words by X position within the line
                var lineWords = lineGroup.OrderBy(w => w.Bounds.X).ToList();
                
                // Check for large horizontal gaps and split the line if needed
                List<List<TextElement>> splitLines = new List<List<TextElement>>();
                List<TextElement> currentSegment = new List<TextElement>();
                TextElement? previousWord = null;
                
                // Split the line if there are large horizontal gaps
                foreach (var word in lineWords)
                {
                    if (previousWord != null)
                    {
                        // Calculate the horizontal gap between words
                        double gap = word.Bounds.X - (previousWord.Bounds.X + previousWord.Bounds.Width);
                        double averageCharWidth = (word.Bounds.Width / Math.Max(1, word.Text.Length) + 
                                              previousWord.Bounds.Width / Math.Max(1, previousWord.Text.Length)) / 2.0;
                        
                        // Log for debugging
                        Console.WriteLine($"Horizontal gap between words: {gap:F1}px, Average char width: {averageCharWidth:F1}px, Threshold: {largeHorizontalGapThreshold:F1}px");
                        
                        // Check if the gap exceeds the threshold or is unusually large compared to character width
                        if (gap > largeHorizontalGapThreshold || gap > (averageCharWidth * 10))
                        {
                            // Gap is large enough to split the line
                            Console.WriteLine($"Large horizontal gap ({gap:F1}px) detected - splitting line");
                            
                            // Add the current segment to splitLines if it has words
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
                    double minX = segment.Min(w => w.Bounds.X);
                    double minY = segment.Min(w => w.Bounds.Y);
                    double maxX = segment.Max(w => w.Bounds.X + w.Bounds.Width);
                    double maxY = segment.Max(w => w.Bounds.Y + w.Bounds.Height);
                    
                    line.Bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
                    line.CenterY = minY + (maxY - minY) / 2;
                    
                    // Combine all text with appropriate separators based on language
                    string sourceLang = ConfigManager.Instance.GetSourceLanguage();
                    bool isEastAsian = sourceLang == "ja" || 
                                      sourceLang == "ch_sim" || 
                                      sourceLang == "ch_tra" || 
                                      sourceLang == "ko";
                                              
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
        private List<TextElement> GroupLinesIntoParagraphs(List<TextElement> lines, double blockPower)
        {
            if (lines.Count == 0)
                return new List<TextElement>();
                
            // Get threshold values with scaling applied
            double lineVerticalGapThreshold = _config.GetScaledValue(_config.BaseLineVerticalGap, blockPower);
            double fontSizeTolerance = _config.GetScaledValue(_config.BaseLineFontSizeTolerance, blockPower);
            double indentationThreshold = _config.GetScaledValue(_config.BaseIndentation, blockPower);
            double paragraphBreakThreshold = _config.GetScaledValue(_config.BaseParagraphBreakThreshold, blockPower);
            
            // Sort lines by Y position
            var sortedLines = lines.OrderBy(l => l.Bounds.Y).ToList();
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
                    
                    // Calculate vertical distance between line centers instead of using bounding boxes
                    // This handles overlapping character descenders/ascenders better
                    double lastLineCenterY = lastLine.Bounds.Y + (lastLine.Bounds.Height * 0.5);
                    double currentLineCenterY = line.Bounds.Y + (line.Bounds.Height * 0.5);
                    double centerDistance = currentLineCenterY - lastLineCenterY;
                    
                    // Calculate expected line height - use a % of the average height to account for overlapping
                    double averageHeight = (lastLine.Bounds.Height + line.Bounds.Height) * 0.5;
                    double normalLineSpacing = averageHeight * 0.63;
                    
                    // Calculate adjusted vertical gap that accounts for overlapping text
                    double verticalGap = centerDistance - normalLineSpacing;
                    
                    // Log for debugging
                    Console.WriteLine($"Line vertical gap (adjusted): {verticalGap:F1}px, Center distance: {centerDistance:F1}px, Normal spacing: {normalLineSpacing:F1}px, Threshold: {lineVerticalGapThreshold:F1}px");
                    
                    // Large center distance indicates paragraph break
                    if (centerDistance > (averageHeight * 1.5) + paragraphBreakThreshold)
                    {
                        startNewParagraph = true;
                        Console.WriteLine("New paragraph: Large gap detected");
                    }
                    // Moderate gap more than normal line spacing threshold indicates line break
                    else if (verticalGap > lineVerticalGapThreshold || centerDistance > (averageHeight * 1.2))
                    {
                        startNewParagraph = true;
                        Console.WriteLine("New paragraph: Line spacing exceeded threshold");
                    }
                    
                    // Check indentation - significant indent may indicate new paragraph
                    // We already have the lastLine from above
                    double indentation = line.Bounds.X - lastLine.Bounds.X;
                    
                    if (Math.Abs(indentation) > indentationThreshold)
                    {
                        // Significant indentation change suggests new paragraph
                        startNewParagraph = true;
                    }
                    
                    // Check font size consistency
                    double fontSizeDiff = Math.Abs(line.Bounds.Height - lastLine.Bounds.Height);
                    if (fontSizeDiff > fontSizeTolerance)
                    {
                        // Different font sizes suggest different paragraphs
                        startNewParagraph = true;
                    }
                    
                    // Font size too small might indicate a caption, header, or other special text
                    double fontSizeRatio = line.Bounds.Height / lastLine.Bounds.Height;
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

                        // Get current source language from config
                        string sourceLangForParagraphs = ConfigManager.Instance.GetSourceLanguage();
                        
                        // Add appropriate separator based on language
                        // For East Asian languages (Japanese, Chinese, Korean), don't add space
                        bool isEastAsianLangForParagraphs = sourceLangForParagraphs == "ja" || 
                                                          sourceLangForParagraphs == "ch_sim" || 
                                                          sourceLangForParagraphs == "ch_tra" || 
                                                          sourceLangForParagraphs == "ko";
                                                  
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
                        double right = Math.Max(currentParagraph.Bounds.X + currentParagraph.Bounds.Width, 
                                          line.Bounds.X + line.Bounds.Width);
                        double bottom = Math.Max(currentParagraph.Bounds.Y + currentParagraph.Bounds.Height, 
                                          line.Bounds.Y + line.Bounds.Height);
                                          
                        currentParagraph.Bounds.Width = right - currentParagraph.Bounds.X;
                        currentParagraph.Bounds.Height = bottom - currentParagraph.Bounds.Y;
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
        private double _settleTime; // Time in seconds to wait for text to settle before capturing
        
        // Constructor - load values from config
        private BlockDetectionManager()
        {
            // Load values from config - these will use defaults if not found in config
            _scaleModToApplyToAllBlockDetectionParameters = ConfigManager.Instance.GetBlockDetectionScale();
            _settleTime = ConfigManager.Instance.GetBlockDetectionSettleTime();
            
            Console.WriteLine($"Loaded block detection scale from config: {_scaleModToApplyToAllBlockDetectionParameters}");
            Console.WriteLine($"Loaded block detection settle time from config: {_settleTime} seconds");
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
        /// Set the settle time (time to wait for text to settle before capturing)
        /// </summary>
        /// <param name="seconds">Time in seconds</param>
        public void SetSettleTime(double seconds)
        {
            if (seconds < 0)
            {
                Console.WriteLine($"Invalid settle time: {seconds}. Must be non-negative. Using 0.");
                _settleTime = 0;
                ConfigManager.Instance.SetBlockDetectionSettleTime(0);
            }
            else
            {
                _settleTime = seconds;
                // Save to config to persist between sessions
                ConfigManager.Instance.SetBlockDetectionSettleTime(seconds);
                Console.WriteLine($"Settle time set to {seconds} seconds");
            }
        }
        
        /// <summary>
        /// Get the current settle time in seconds
        /// </summary>
        public double GetSettleTime()
        {
            return _settleTime;
        }
        
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
        public void SetBlockDetectionScale(double scale)
        {
            if (scale <= 0)
            {
                Console.WriteLine($"Invalid block detection scale: {scale}. Must be positive. Using default.");
                _scaleModToApplyToAllBlockDetectionParameters = 0.1f;
                ConfigManager.Instance.SetBlockDetectionScale(0.1f);
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
                    textBlocks.Add(new TextBlockInfo {
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
            
            // Create an empty JsonElement to return (dummy implementation)
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartArray();
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
        /// Determine if a new paragraph should be started based on the relationship between two text blocks
        /// </summary>
        private bool ShouldStartNewParagraph(TextBlockInfo previous, TextBlockInfo current)
        {
            // Calculate the current scaled thresholds
            double verticalProximityThreshold = _baseVerticalProximityThreshold * _scaleModToApplyToAllBlockDetectionParameters;
            double paragraphBreakThreshold = _baseParagraphBreakThreshold * _scaleModToApplyToAllBlockDetectionParameters;
            double indentationThreshold = _baseIndentationThreshold * _scaleModToApplyToAllBlockDetectionParameters;
            double horizontalAlignmentThreshold = _baseHorizontalAlignmentThreshold * _scaleModToApplyToAllBlockDetectionParameters;
            double horizontalGapThreshold = _baseHorizontalGapThreshold * _scaleModToApplyToAllBlockDetectionParameters;
            double xPositionDiffThreshold = _baseHorizontalXPositionThreshold * _scaleModToApplyToAllBlockDetectionParameters;
            
            // Check vertical distance
            double verticalGap = current.Y - (previous.Y + previous.Height);
            
            // If there's a large vertical gap, it's likely a new paragraph
            if (verticalGap > paragraphBreakThreshold)
            {
                return true;
            }
            
            // Check X position difference - if starting points differ significantly, consider as different paragraphs
            double xPositionDiff = Math.Abs(current.X - previous.X);
            
            // If starting X positions differ significantly, consider them different paragraphs
            // This is critical for handling character names, columns, and other horizontal layout patterns
            if (xPositionDiff > xPositionDiffThreshold)
            {
                return true;
            }
            
            // Check if blocks are on different lines but close enough vertically to be in the same paragraph
            double centerYPrev = previous.Y + previous.Height / 2;
            double centerYCurr = current.Y + current.Height / 2;
            
            // If vertical centers differ significantly, these are different lines
            if (Math.Abs(centerYCurr - centerYPrev) > verticalProximityThreshold)
            {
                // If on different lines, check horizontal gap
                // If horizontally far apart, start a new paragraph 
                double horizontalGap = Math.Min(
                    Math.Abs(current.X - (previous.X + previous.Width)), // gap if current is to the right
                    Math.Abs(previous.X - (current.X + current.Width))   // gap if current is to the left
                );
                
                // Large horizontal gap indicates these blocks don't belong together
                if (horizontalGap > horizontalGapThreshold)
                {
                    return true;
                }
            }
            
            // Check horizontal alignment
            // If the current line starts significantly to the right, it might be indented (first line of a paragraph)
            if (current.X > previous.X + indentationThreshold)
            {
                // Only consider as a new paragraph if it's also on a new line
                // (to avoid breaking on spaces within a line)
                if (verticalGap > 0)
                {
                    return true;
                }
            }
            
            // If the current line starts significantly to the left, it might be a new paragraph or element
            if (previous.X > current.X + horizontalAlignmentThreshold)
            {
                // Only consider as a new paragraph if it's also on a new line
                if (verticalGap > 0)
                {
                    return true;
                }
            }
            
            // If we've gotten here, the blocks are probably part of the same paragraph
            return false;
        }
        
        /// <summary>
        /// Determine if a paragraph contains isolated elements like buttons or headers
        /// </summary>
        private bool IsProbablyIsolatedText(List<TextBlockInfo> paragraph)
        {
            // Calculate the current scaled thresholds
            double isolatedTextThreshold = _baseIsolatedTextThreshold * _scaleModToApplyToAllBlockDetectionParameters;
            
            // If it's a single, short text block, it might be a button or header
            if (paragraph.Count == 1)
            {
                var block = paragraph[0];
                
                // Short text and short width might indicate a button or isolated element
                if (block.Text.Length < 20 && block.Width < isolatedTextThreshold)
                {
                    return true;
                }
                
                // Potential character name or label - often no spaces and positioned left
                if (block.Text.Length < 15 && !block.Text.Contains(" "))
                {
                    return true;
                }
            }
            
            return false;
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