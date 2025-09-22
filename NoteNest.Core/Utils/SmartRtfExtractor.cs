using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NoteNest.Core.Utils
{
    /// <summary>
    /// Smart RTF content extraction with enhanced text processing and preview generation.
    /// Optimized for production use with consistent, high-quality previews.
    /// </summary>
    public static class SmartRtfExtractor
    {
        // Compiled regex patterns for performance
        private static readonly Regex RtfStripper = new Regex(@"\\[a-z]{1,32}[0-9]*\s?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BraceRemover = new Regex(@"[\{\}]", RegexOptions.Compiled);
        private static readonly Regex WhitespaceNormalizer = new Regex(@"\s+", RegexOptions.Compiled);
        
        // RTF special character mappings
        private static readonly (string rtf, string plain)[] SpecialCharacters = {
            (@"\'92", "'"),     // Right single quotation mark
            (@"\'93", "\""),    // Left double quotation mark  
            (@"\'94", "\""),    // Right double quotation mark
            (@"\'96", "-"),     // En dash
            (@"\'97", "--"),    // Em dash
            (@"\'85", "..."),   // Horizontal ellipsis
            (@"\'a0", " "),     // Non-breaking space
            (@"\u8216", "'"),   // Unicode apostrophe
            (@"\u8220", "\""),  // Unicode left quote
            (@"\u8221", "\""),  // Unicode right quote
            (@"\u8211", "-"),   // Unicode en dash
            (@"\u8212", "--"),  // Unicode em dash
            (@"\tab", " "),     // Tab character
            (@"\par", " "),     // Paragraph break
            (@"\line", " "),    // Line break
        };

        /// <summary>
        /// Extract plain text from RTF content with enhanced error handling and character mapping
        /// </summary>
        /// <param name="rtfContent">Raw RTF content</param>
        /// <returns>Clean plain text or fallback message</returns>
        public static string ExtractPlainText(string rtfContent)
        {
            if (string.IsNullOrEmpty(rtfContent))
                return "Empty note";

            try
            {
                var text = rtfContent;

                // Step 1: Remove font table (major pollution source)
                text = RemoveFontTable(text);

                // Step 2: Extract actual content from ltrch blocks
                text = ExtractContentFromLtrchBlocks(text);

                // Step 3: Remove remaining RTF control codes
                text = RtfStripper.Replace(text, " ");

                // Step 4: Remove braces
                text = BraceRemover.Replace(text, "");

                // Step 5: Decode special characters
                foreach (var (rtf, plain) in SpecialCharacters)
                {
                    text = text.Replace(rtf, plain);
                }

                // Step 6: Clean up font names and orphaned semicolons
                text = CleanFontPollution(text);

                // Step 7: Normalize whitespace
                text = WhitespaceNormalizer.Replace(text, " ").Trim();

                // Step 8: Validate result
                return string.IsNullOrWhiteSpace(text) ? "No text content" : text;
            }
            catch (Exception ex)
            {
                // Log but don't throw - return graceful fallback
                System.Diagnostics.Debug.WriteLine($"RTF extraction failed: {ex.Message}");
                return "Could not extract content";
            }
        }

        /// <summary>
        /// Generate smart preview with boilerplate detection and intelligent truncation
        /// </summary>
        /// <param name="plainText">Extracted plain text</param>
        /// <param name="maxLength">Maximum preview length</param>
        /// <returns>High-quality preview text</returns>
        public static string GenerateSmartPreview(string plainText, int maxLength = 150)
        {
            if (string.IsNullOrWhiteSpace(plainText))
                return "Empty note";

            try
            {
                // Step 1: Find meaningful content (skip boilerplate)
                var meaningfulContent = FindMeaningfulContent(plainText);
                if (string.IsNullOrEmpty(meaningfulContent))
                    meaningfulContent = plainText;

                // Step 2: Smart truncation at word boundary
                if (meaningfulContent.Length <= maxLength)
                    return meaningfulContent.Trim();

                // Find good truncation point
                var truncated = meaningfulContent.Substring(0, maxLength);
                var lastSpace = truncated.LastIndexOf(' ');
                var lastPeriod = truncated.LastIndexOf('.');
                var lastComma = truncated.LastIndexOf(',');

                // Use the best break point
                var bestBreakPoint = Math.Max(lastSpace, Math.Max(lastPeriod, lastComma));
                
                // Only use break point if it's past 70% of max length (avoid tiny previews)
                if (bestBreakPoint > maxLength * 0.7)
                {
                    truncated = meaningfulContent.Substring(0, bestBreakPoint + 1);
                }

                return truncated.Trim() + (meaningfulContent.Length > truncated.Length ? "..." : "");
            }
            catch
            {
                return "Preview unavailable";
            }
        }

        /// <summary>
        /// Extract clean preview from RTF content in one operation (for indexing)
        /// </summary>
        /// <param name="rtfContent">Raw RTF content</param>
        /// <param name="maxLength">Maximum preview length</param>
        /// <returns>Ready-to-use preview text</returns>
        public static string ExtractPreview(string rtfContent, int maxLength = 150)
        {
            var plainText = ExtractPlainText(rtfContent);
            return GenerateSmartPreview(plainText, maxLength);
        }

        #region Private Helper Methods

        private static string RemoveFontTable(string rtfContent)
        {
            // Remove font table definitions that pollute extracted text
            // Pattern: {\fonttbl{...}} 
            var fontTableRegex = new Regex(@"\{\\fonttbl\{[^}]*\}[^}]*\}", RegexOptions.IgnoreCase);
            var text = fontTableRegex.Replace(rtfContent, "");
            
            // Also remove color table
            var colorTableRegex = new Regex(@"\{\\colortbl[^}]*\}", RegexOptions.IgnoreCase);
            text = colorTableRegex.Replace(text, "");
            
            return text;
        }

        private static string ExtractContentFromLtrchBlocks(string rtfContent)
        {
            // Extract content from {\ltrch actual content} blocks
            var ltrchRegex = new Regex(@"\{\\ltrch\s+([^}]*)\}", RegexOptions.IgnoreCase);
            var matches = ltrchRegex.Matches(rtfContent);
            
            if (matches.Count > 0)
            {
                // Combine all ltrch content
                var contentParts = matches.Cast<Match>()
                    .Select(m => m.Groups[1].Value.Trim())
                    .Where(content => !string.IsNullOrWhiteSpace(content))
                    .ToArray();
                
                if (contentParts.Length > 0)
                {
                    return string.Join(" ", contentParts);
                }
            }
            
            // If no ltrch blocks found, return original (will be processed further)
            return rtfContent;
        }

        private static string CleanFontPollution(string text)
        {
            // Remove common font names that leak through
            var fontNames = new[] { 
                "Times New Roman", "Segoe UI", "Calibri", "Arial", "Helvetica", 
                "Verdana", "Georgia", "Trebuchet MS", "Comic Sans MS" 
            };
            
            foreach (var fontName in fontNames)
            {
                text = text.Replace(fontName, "");
            }
            
            // Clean up orphaned semicolons and extra spaces
            text = text.Replace(";;;", "").Replace(";;", "").Replace("; ; ;", "").Replace("; ;", "");
            
            return text;
        }

        private static int FindContentStart(string rtfContent)
        {
            // Common RTF content markers
            var contentMarkers = new[] { @"\viewkind", @"\uc1", @"\pard", @"\f0" };
            
            foreach (var marker in contentMarkers)
            {
                var markerIndex = rtfContent.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex >= 0)
                {
                    // Find end of this control sequence
                    var spaceAfter = rtfContent.IndexOf(' ', markerIndex);
                    if (spaceAfter > markerIndex)
                        return spaceAfter + 1;
                }
            }

            return 0; // Use full content if no markers found
        }

        private static string FindMeaningfulContent(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Split into lines and find first meaningful content
            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                // Skip empty lines or boilerplate
                if (string.IsNullOrWhiteSpace(trimmed) || IsBoilerplate(trimmed))
                    continue;
                
                // Found meaningful content - return rest of text starting from here
                var lineStart = text.IndexOf(trimmed, StringComparison.Ordinal);
                if (lineStart >= 0)
                    return text.Substring(lineStart);
            }

            // No meaningful content found, return original
            return text;
        }

        private static bool IsBoilerplate(string text)
        {
            if (text.Length < 5)  // Changed from 10 to 5 to allow shorter meaningful content
                return true;

            // Common boilerplate patterns
            var boilerplatePatterns = new[]
            {
                "date:",
                "created:",
                "modified:",
                "author:",
                "title:",
                "subject:",
                "category:",
                "tags:",
                "template",
                "document"
            };

            var lowerText = text.ToLowerInvariant();
            
            // Check if starts with boilerplate
            foreach (var pattern in boilerplatePatterns)
            {
                if (lowerText.StartsWith(pattern))
                    return true;
            }

            // Check if it's mostly numbers/dates
            var digitCount = text.Count(char.IsDigit);
            if (digitCount > text.Length * 0.5) // More than 50% digits
                return true;

            // Check if it's too short to be meaningful
            var wordCount = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount < 3)
                return true;

            return false;
        }

        #endregion
    }
}
