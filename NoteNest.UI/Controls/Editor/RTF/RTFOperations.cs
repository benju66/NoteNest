using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows;
using System.Collections.Generic;
using System.Linq;

namespace NoteNest.UI.Controls.Editor.RTF
{
    /// <summary>
    /// Stateless RTF operations - can be used by any component
    /// Single Responsibility: RTF format operations (load, save, extract)
    /// Follows SRP principles with pure static methods
    /// </summary>
    public static class RTFOperations
    {
        /// <summary>
        /// Save RichTextBox content to RTF format string with single spacing preservation
        /// </summary>
        public static string SaveToRTF(RichTextBox editor)
        {
            if (editor?.Document == null) return string.Empty;
            
            try
            {
                using (var stream = new MemoryStream())
                {
                    var range = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd);
                    range.Save(stream, System.Windows.DataFormats.Rtf);
                    var rtfContent = Encoding.UTF8.GetString(stream.ToArray());
                    
                    // Enhance RTF with single spacing control codes
                    return EnhanceRTFForSingleSpacing(rtfContent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFOperations] Save failed: {ex.Message}");
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Enhance RTF content with single spacing control codes
        /// </summary>
        private static string EnhanceRTFForSingleSpacing(string rtfContent)
        {
            if (string.IsNullOrEmpty(rtfContent)) return rtfContent;
            
            try
            {
                var enhanced = rtfContent;
                
                // Insert single line spacing control codes
                // \sl0\slmult0 = single line spacing
                enhanced = enhanced.Replace(@"\f0\fs24", @"\f0\fs24\sl0\slmult0");
                
                // Remove excessive paragraph spacing that might be added by RTF
                enhanced = Regex.Replace(enhanced, @"\\sb\d+", "", RegexOptions.IgnoreCase);
                enhanced = Regex.Replace(enhanced, @"\\sa\d+", "", RegexOptions.IgnoreCase);
                
                return enhanced;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFOperations] RTF spacing enhancement failed: {ex.Message}");
                return rtfContent; // Return original if enhancement fails
            }
        }
        
        /// <summary>
        /// Load RTF content into RichTextBox with error handling
        /// </summary>
        public static void LoadFromRTF(RichTextBox editor, string rtfContent)
        {
            if (editor?.Document == null || string.IsNullOrEmpty(rtfContent)) return;
            
            try
            {
                // Security validation
                if (!IsValidRTF(rtfContent))
                {
                    LoadAsPlainText(editor, rtfContent);
                    return;
                }
                
                // Sanitize content
                var sanitizedContent = SanitizeRTFContent(rtfContent);
                
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sanitizedContent)))
                {
                    var range = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd);
                    range.Load(stream, System.Windows.DataFormats.Rtf);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFOperations] Load failed: {ex.Message}");
                // Fallback to plain text
                LoadAsPlainText(editor, rtfContent);
            }
        }
        
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
                
                // Remove encoding artifacts (enhanced from current RTF editor)
                text = Regex.Replace(text, @"cpg\d+", "", RegexOptions.IgnoreCase);
                
                // Remove font declarations
                text = Regex.Replace(text, @"\\f\d+", "", RegexOptions.IgnoreCase);
                
                // Remove common font family names that leak through
                text = Regex.Replace(text, @"\b(Segoe UI|Calibri|Arial|Times New Roman)\b", "", RegexOptions.IgnoreCase);
                
                // Clean up whitespace
                text = Regex.Replace(text, @"\s+", " ");
                
                return text.Trim();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFOperations] Text extraction failed: {ex.Message}");
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Enhanced bulleted list extraction for search previews
        /// Ported from current RTF editor improvements
        /// </summary>
        public static string ExtractSearchPreview(string rtfContent, int maxLength = 200)
        {
            if (string.IsNullOrEmpty(rtfContent)) return string.Empty;
            
            try
            {
                var plain = ExtractPlainText(rtfContent);
                
                // Try to extract bulleted list items first (priority content)
                var listMatches = Regex.Matches(plain, @"\\bullet\s*([^\\]+?)(?=\\|$)", RegexOptions.IgnoreCase);
                var listItems = new System.Collections.Generic.List<string>();
                
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFOperations] Preview extraction failed: {ex.Message}");
                return ExtractPlainText(rtfContent);
            }
        }
        
        /// <summary>
        /// Validate RTF content for security
        /// </summary>
        private static bool IsValidRTF(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;
            
            // Basic RTF validation
            return content.TrimStart().StartsWith("{\\rtf", StringComparison.OrdinalIgnoreCase) &&
                   content.TrimEnd().EndsWith("}");
        }
        
        /// <summary>
        /// Sanitize RTF content to remove potentially dangerous elements
        /// </summary>
        private static string SanitizeRTFContent(string rtfContent)
        {
            if (string.IsNullOrEmpty(rtfContent)) return rtfContent;
            
            try
            {
                var sanitized = rtfContent;
                
                // Remove embedded objects and fields (keep formatting only)
                sanitized = Regex.Replace(sanitized, @"\\object[^}]*}", "", RegexOptions.IgnoreCase);
                sanitized = Regex.Replace(sanitized, @"\\field[^}]*}", "", RegexOptions.IgnoreCase);
                sanitized = Regex.Replace(sanitized, @"\\pict[^}]*}", "", RegexOptions.IgnoreCase);
                
                // Remove script-like content
                sanitized = Regex.Replace(sanitized, @"javascript:[^}]*", "", RegexOptions.IgnoreCase);
                
                return sanitized;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFOperations] Sanitization failed: {ex.Message}");
                return rtfContent; // Return original if sanitization fails
            }
        }
        
        /// <summary>
        /// Load content as plain text fallback
        /// </summary>
        private static void LoadAsPlainText(RichTextBox editor, string content)
        {
            try
            {
                editor.Document.Blocks.Clear();
                editor.Document.Blocks.Add(new Paragraph(new Run(content)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFOperations] Plain text fallback failed: {ex.Message}");
            }
        }
    }
}
