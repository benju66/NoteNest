using System;
using System.Text.RegularExpressions;

namespace NoteNest.Core.Utils
{
    /// <summary>
    /// Simple RTF text extraction utility for search indexing
    /// Replaces the complex legacy RTFTextExtractor with a lightweight implementation
    /// </summary>
    public static class RTFTextExtractor
    {
        // Compiled regex patterns for better performance
        private static readonly Regex RtfControlPattern = new Regex(@"\\[a-z]+\d*\s?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RtfBracesPattern = new Regex(@"[{}]", RegexOptions.Compiled);
        private static readonly Regex WhitespacePattern = new Regex(@"\s+", RegexOptions.Compiled);

        /// <summary>
        /// Extract plain text from RTF content
        /// Simple but effective approach for search indexing
        /// </summary>
        /// <param name="rtfContent">RTF formatted content</param>
        /// <returns>Plain text content</returns>
        public static string ExtractPlainText(string rtfContent)
        {
            if (string.IsNullOrEmpty(rtfContent))
                return string.Empty;

            try
            {
                // Skip if not RTF content
                if (!rtfContent.StartsWith(@"{\rtf", StringComparison.OrdinalIgnoreCase))
                {
                    return rtfContent; // Return as-is if not RTF
                }

                var text = rtfContent;

                // Remove RTF control codes (e.g., \par, \b, \i, \fs24, etc.)
                text = RtfControlPattern.Replace(text, string.Empty);

                // Remove braces
                text = RtfBracesPattern.Replace(text, string.Empty);

                // Clean up multiple whitespaces
                text = WhitespacePattern.Replace(text, " ");

                // Trim and return
                return text.Trim();
            }
            catch (Exception)
            {
                // If extraction fails, return empty string rather than crashing
                return string.Empty;
            }
        }

        /// <summary>
        /// Extract search preview with length limit
        /// </summary>
        /// <param name="rtfContent">RTF formatted content</param>
        /// <param name="maxLength">Maximum preview length</param>
        /// <returns>Plain text preview</returns>
        public static string ExtractSearchPreview(string rtfContent, int maxLength = 200)
        {
            var plainText = ExtractPlainText(rtfContent);
            
            if (plainText.Length <= maxLength)
                return plainText;
            
            // Trim to max length and try to end at word boundary
            var truncated = plainText.Substring(0, maxLength);
            var lastSpace = truncated.LastIndexOf(' ');
            
            if (lastSpace > maxLength * 0.8) // If we can trim to space without losing too much
            {
                truncated = truncated.Substring(0, lastSpace);
            }
            
            return truncated + "...";
        }
    }
}
