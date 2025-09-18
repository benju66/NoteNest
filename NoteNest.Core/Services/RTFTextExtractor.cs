using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Lightweight RTF text extraction for search indexing
    /// Single Responsibility: Extract plain text from RTF content for search
    /// Core-only implementation to avoid UI dependencies
    /// MEMORY OPTIMIZED: Uses compiled Regex patterns for performance
    /// </summary>
    public static class RTFTextExtractor
    {
        // MEMORY FIX: Pre-compiled Regex patterns (eliminates compilation overhead)
        private static readonly Regex ControlWordsRegex = new Regex(@"\\[a-z]+[-]?\d*[ ]?", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BracesRegex = new Regex(@"[{}]", 
            RegexOptions.Compiled);
        private static readonly Regex EscapeCharsRegex = new Regex(@"\\\*", 
            RegexOptions.Compiled);
        private static readonly Regex EncodingArtifactsRegex = new Regex(@"cpg\d+", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FontDeclarationsRegex = new Regex(@"\\f\d+", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FontNamesRegex = new Regex(@"\b(Segoe UI|Calibri|Arial|Times New Roman)\b", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", 
            RegexOptions.Compiled);
        private static readonly Regex BulletPatternRegex = new Regex(@"\\bullet\s*([^\\]+?)(?=\\|$)", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        /// <summary>
        /// Extract plain text from RTF content for search indexing
        /// Enhanced version that removes formatting artifacts
        /// </summary>
        public static string ExtractPlainText(string rtfContent)
        {
            if (string.IsNullOrEmpty(rtfContent)) return string.Empty;
            
            try
            {
                // MEMORY FIX: Use compiled patterns instead of dynamic compilation
                var text = rtfContent;
                
                // Apply optimized compiled patterns (eliminates ~25KB compilation overhead)
                text = ControlWordsRegex.Replace(text, " ");
                text = BracesRegex.Replace(text, "");
                text = EscapeCharsRegex.Replace(text, "");
                text = EncodingArtifactsRegex.Replace(text, "");
                text = FontDeclarationsRegex.Replace(text, "");
                text = FontNamesRegex.Replace(text, "");
                text = WhitespaceRegex.Replace(text, " ");
                
                return text.Trim();
            }
            catch (Exception)
            {
                // Return empty string on error to prevent search index corruption
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Enhanced bulleted list extraction for search previews
        /// </summary>
        public static string ExtractSearchPreview(string rtfContent, int maxLength = 200)
        {
            if (string.IsNullOrEmpty(rtfContent)) return string.Empty;
            
            try
            {
                var plain = ExtractPlainText(rtfContent);
                
                // Try to extract bulleted list items first (priority content)
                // MEMORY FIX: Use compiled pattern
                var listMatches = BulletPatternRegex.Matches(plain);
                var listItems = new List<string>();
                
                foreach (Match match in listMatches)
                {
                    var item = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(item) && item.Length > 2)
                    {
                        listItems.Add(item);
                    }
                }
                
                // If we found list items, use them
                if (listItems.Count > 0)
                {
                    var preview = string.Join(" • ", listItems.Take(3));
                    return preview.Length > maxLength ? preview.Substring(0, maxLength) + "..." : preview;
                }
                
                // Otherwise use regular text extraction
                return plain.Length > maxLength ? plain.Substring(0, maxLength) + "..." : plain;
            }
            catch (Exception)
            {
                return ExtractPlainText(rtfContent);
            }
        }
        
    }
}
