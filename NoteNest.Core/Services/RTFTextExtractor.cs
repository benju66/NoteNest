using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Lightweight RTF text extraction for search indexing
    /// Single Responsibility: Extract plain text from RTF content for search
    /// Core-only implementation to avoid UI dependencies
    /// </summary>
    public static class RTFTextExtractor
    {
        /// <summary>
        /// Extract plain text from RTF content for search indexing
        /// Enhanced version that removes formatting artifacts
        /// </summary>
        public static string ExtractPlainText(string rtfContent)
        {
            if (string.IsNullOrEmpty(rtfContent)) return string.Empty;
            
            try
            {
                var text = rtfContent;
                
                // Remove RTF control words
                text = Regex.Replace(text, @"\\[a-z]+[-]?\d*[ ]?", " ", RegexOptions.IgnoreCase);
                
                // Remove braces
                text = Regex.Replace(text, @"[{}]", "");
                
                // Remove escape characters
                text = Regex.Replace(text, @"\\\*", "");
                
                // Remove encoding artifacts
                text = Regex.Replace(text, @"cpg\d+", "", RegexOptions.IgnoreCase);
                
                // Remove font declarations
                text = Regex.Replace(text, @"\\f\d+", "", RegexOptions.IgnoreCase);
                
                // Remove common font family names that leak through
                text = Regex.Replace(text, @"\b(Segoe UI|Calibri|Arial|Times New Roman)\b", "", RegexOptions.IgnoreCase);
                
                // Clean up whitespace
                text = Regex.Replace(text, @"\s+", " ");
                
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
                var listMatches = Regex.Matches(plain, @"\\bullet\s*([^\\]+?)(?=\\|$)", RegexOptions.IgnoreCase);
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
