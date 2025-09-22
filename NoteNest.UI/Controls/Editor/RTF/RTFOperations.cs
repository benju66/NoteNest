using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows;
using System.Collections.Generic;
using System.Linq;
using NoteNest.Core.Diagnostics;

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
            #if DEBUG
            string result = string.Empty;
            EnhancedMemoryTracker.TrackServiceOperation("RTFOperations", "SaveToRTF", () =>
            {
            #endif
                if (editor?.Document == null) 
                {
                    #if DEBUG
                    result = string.Empty;
                    return;
                    #else
                    return string.Empty;
                    #endif
                }
                
                // MEMORY FIX: Proper TextRange disposal pattern
                TextRange range = null;
                try
                {
                    using (var stream = new MemoryStream())
                    {
                        range = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd);
                        range.Save(stream, System.Windows.DataFormats.Rtf);
                        
                        // MEMORY FIX: Eliminate ToArray() duplication
                        stream.Position = 0;
                        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
                        {
                            var rtfContent = reader.ReadToEnd();
                            
                            // Enhance RTF with single spacing control codes
                            #if DEBUG
                            result = EnhanceRTFForSingleSpacing(rtfContent);
                            #else
                            return EnhanceRTFForSingleSpacing(rtfContent);
                            #endif
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RTFOperations] Save failed: {ex.Message}");
                    #if DEBUG
                    result = string.Empty;
                    #else
                    return string.Empty;
                    #endif
                }
                finally
                {
                    // MEMORY FIX: Explicit TextRange cleanup
                    range = null;
                    
                    // Force collection for large documents to prevent accumulation
                    if (editor?.Document != null)
                    {
                        var docSize = EstimateDocumentSize(editor.Document);
                        if (docSize > 50 * 1024) // >50KB documents
                        {
                            GC.Collect(0, GCCollectionMode.Optimized);
                        }
                    }
                }
            #if DEBUG
            });
            return result;
            #endif
        }
        
        /// <summary>
        /// Estimate document size for memory management decisions
        /// </summary>
        private static long EstimateDocumentSize(FlowDocument document)
        {
            try
            {
                // Rough estimation based on block count and content
                var blockCount = document.Blocks.Count;
                var estimatedSize = blockCount * 1024; // ~1KB per block estimate
                
                // Add content size estimation
                foreach (var block in document.Blocks.Take(10)) // Sample first 10 blocks
                {
                    if (block is Paragraph paragraph)
                    {
                        var textLength = GetParagraphTextLength(paragraph);
                        estimatedSize += textLength * 2; // ~2 bytes per char for UTF-16
                    }
                }
                
                return estimatedSize;
            }
            catch
            {
                return 10 * 1024; // Default 10KB estimate if calculation fails
            }
        }
        
        /// <summary>
        /// Get approximate text length of a paragraph
        /// </summary>
        private static int GetParagraphTextLength(Paragraph paragraph)
        {
            try
            {
                var totalLength = 0;
                foreach (var inline in paragraph.Inlines.Take(5)) // Sample first 5 inlines
                {
                    if (inline is Run run && run.Text != null)
                    {
                        totalLength += run.Text.Length;
                    }
                }
                return totalLength;
            }
            catch
            {
                return 100; // Default estimate
            }
        }
        
        /// <summary>
        /// Enhance RTF content with single spacing control codes while preserving list hierarchy
        /// </summary>
        private static string EnhanceRTFForSingleSpacing(string rtfContent)
        {
            if (string.IsNullOrEmpty(rtfContent)) return rtfContent;
            
            try
            {
                // MEMORY FIX: Use StringBuilder to avoid intermediate string objects
                var enhanced = new StringBuilder(rtfContent, rtfContent.Length + 100);
                
                // Insert single line spacing control codes
                // \sl0\slmult0 = single line spacing
                enhanced.Replace(@"\f0\fs24", @"\f0\fs24\sl0\slmult0");
                
                // PRESERVE list hierarchy codes (\sb, \sa) - they're needed for nested list structure
                // Visual single spacing will be handled by post-load style application
                // No longer removing \sb and \sa codes to maintain RTF structural integrity
                
                System.Diagnostics.Debug.WriteLine("[RTFOperations] RTF enhanced with line spacing while preserving list hierarchy");
                return enhanced.ToString();
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
            #if DEBUG
            EnhancedMemoryTracker.TrackServiceOperation("RTFOperations", "LoadFromRTF", () =>
            {
            #endif
                if (editor?.Document == null || string.IsNullOrEmpty(rtfContent)) return;
                
                // MEMORY FIX: Proper TextRange disposal pattern
                TextRange range = null;
                try
                {
                    // Security validation
                    if (!IsValidRTF(rtfContent))
                    {
                        LoadAsPlainTextOptimized(editor, rtfContent);
                        return;
                    }
                    
                    // Sanitize content
                    var sanitizedContent = SanitizeRTFContent(rtfContent);
                    
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sanitizedContent)))
                    {
                        range = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd);
                        range.Load(stream, System.Windows.DataFormats.Rtf);
                        
                        // Re-enable spell check after RTF load (RTF loading can reset it)
                        System.Windows.Controls.SpellCheck.SetIsEnabled(editor, true);
                        System.Diagnostics.Debug.WriteLine("[RTFOperations] Spell check re-enabled after RTF load");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RTFOperations] Load failed: {ex.Message}");
                    // Fallback to plain text
                    LoadAsPlainTextOptimized(editor, rtfContent);
                }
                finally
                {
                    // MEMORY FIX: Explicit TextRange cleanup
                    range = null;
                    
                    // Force collection for large content to prevent accumulation
                    if (!string.IsNullOrEmpty(rtfContent) && rtfContent.Length > 50 * 1024) // >50KB content
                    {
                        GC.Collect(0, GCCollectionMode.Optimized);
                    }
                }
            #if DEBUG
            });
            #endif
        }
        
        /// <summary>
        /// HYBRID APPROACH: Extract plain text using WPF when editor available (memory efficient)
        /// Falls back to Core RTFTextExtractor for search indexing when no editor context
        /// </summary>
        public static string ExtractPlainTextHybrid(RichTextBox editor, string rtfContent = null)
        {
            // FAST PATH: Use WPF native extraction when editor available (90% of cases)
            if (editor?.Document != null)
            {
                TextRange range = null;
                try
                {
                    range = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd);
                    
                    // WPF automatically strips RTF codes - no Regex needed!
                    var plainText = range.Text;
                    System.Diagnostics.Debug.WriteLine($"[RTFOperations] WPF extraction: {plainText?.Length ?? 0} chars");
                    return plainText ?? string.Empty;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RTFOperations] WPF extraction failed: {ex.Message}");
                    // Fall through to Regex approach
                }
                finally
                {
                    // MEMORY FIX: Explicit cleanup
                    range = null;
                }
            }
            
            // FALLBACK PATH: Use Core RTFTextExtractor for search indexing (10% of cases)
            if (!string.IsNullOrEmpty(rtfContent))
            {
                System.Diagnostics.Debug.WriteLine("[RTFOperations] Using Core SmartRtfExtractor fallback");
                return NoteNest.Core.Utils.SmartRtfExtractor.ExtractPlainText(rtfContent);
            }
            
            return string.Empty;
        }
        
        /// <summary>
        /// Extract plain text from RTF content for search indexing
        /// Enhanced version that removes formatting artifacts
        /// </summary>
        public static string ExtractPlainText(string rtfContent)
        {
            // MEMORY FIX: Delegate to optimized Core implementation (compiled Regex patterns)
            return NoteNest.Core.Utils.SmartRtfExtractor.ExtractPlainText(rtfContent);
        }
        
        /// <summary>
        /// Enhanced bulleted list extraction for search previews
        /// MEMORY OPTIMIZED: Uses Core implementation with compiled patterns
        /// </summary>
        public static string ExtractSearchPreview(string rtfContent, int maxLength = 200)
        {
            // MEMORY FIX: Delegate to optimized Core implementation
            return NoteNest.Core.Utils.SmartRtfExtractor.ExtractPreview(rtfContent, maxLength);
        }
        
        /// <summary>
        /// Extract plain text with WPF native approach (for UI operations)
        /// Most memory-efficient method when RichTextBox is available
        /// </summary>
        public static string ExtractPlainTextFromEditor(RichTextBox editor)
        {
            if (editor?.Document == null) return string.Empty;
            
            TextRange range = null;
            try
            {
                range = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd);
                
                // WPF automatically strips RTF codes - no memory overhead!
                var plainText = range.Text;
                System.Diagnostics.Debug.WriteLine($"[RTFOperations] WPF native extraction: {plainText?.Length ?? 0} chars");
                return plainText ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFOperations] WPF extraction failed: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                // MEMORY FIX: Explicit cleanup
                range = null;
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
        
        // MEMORY FIX: Compiled security patterns (used less frequently, but important for security)
        private static readonly Regex ObjectsRegex = new Regex(@"\\object[^}]*}", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FieldsRegex = new Regex(@"\\field[^}]*}", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PicturesRegex = new Regex(@"\\pict[^}]*}", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ScriptsRegex = new Regex(@"javascript:[^}]*", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        /// <summary>
        /// Sanitize RTF content to remove potentially dangerous elements
        /// MEMORY OPTIMIZED: Uses compiled patterns for security filtering
        /// </summary>
        private static string SanitizeRTFContent(string rtfContent)
        {
            if (string.IsNullOrEmpty(rtfContent)) return rtfContent;
            
            try
            {
                // MEMORY FIX: Use StringBuilder for multiple replacements
                var sanitized = new StringBuilder(rtfContent);
                
                // Remove embedded objects and fields (keep formatting only) using compiled patterns
                var temp = ObjectsRegex.Replace(sanitized.ToString(), "");
                sanitized.Clear().Append(temp);
                
                temp = FieldsRegex.Replace(sanitized.ToString(), "");
                sanitized.Clear().Append(temp);
                
                temp = PicturesRegex.Replace(sanitized.ToString(), "");
                sanitized.Clear().Append(temp);
                
                // Remove script-like content
                temp = ScriptsRegex.Replace(sanitized.ToString(), "");
                
                return temp;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFOperations] Sanitization failed: {ex.Message}");
                return rtfContent; // Return original if sanitization fails
            }
        }
        
        /// <summary>
        /// Load content as plain text fallback - memory optimized
        /// </summary>
        private static void LoadAsPlainTextOptimized(RichTextBox editor, string content)
        {
            try
            {
                var document = editor.Document;
                
                // MEMORY FIX: Clear and force cleanup of old WPF objects
                document.Blocks.Clear();
                
                // For large content, force cleanup before creating new objects
                if (!string.IsNullOrEmpty(content) && content.Length > 10 * 1024) // >10KB
                {
                    GC.Collect(0, GCCollectionMode.Optimized);
                }
                
                // Create new content
                var paragraph = new Paragraph(new Run(content));
                document.Blocks.Add(paragraph);
                
                System.Diagnostics.Debug.WriteLine($"[RTFOperations] Plain text loaded: {content?.Length ?? 0} chars");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFOperations] Plain text fallback failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Legacy method for backward compatibility
        /// </summary>
        private static void LoadAsPlainText(RichTextBox editor, string content)
        {
            LoadAsPlainTextOptimized(editor, content);
        }
        
        // ====================================================================
        // PUBLIC INTEGRATION METHODS for RTFIntegratedSaveEngine
        // ====================================================================
        
        /// <summary>
        /// Public wrapper for RTF validation - used by RTFIntegratedSaveEngine
        /// </summary>
        public static bool IsValidRTFPublic(string content) => IsValidRTF(content);
        
        /// <summary>
        /// Public wrapper for RTF sanitization - used by RTFIntegratedSaveEngine
        /// </summary>
        public static string SanitizeRTFContentPublic(string rtfContent) => SanitizeRTFContent(rtfContent);
        
        /// <summary>
        /// Public wrapper for document size estimation - used by RTFIntegratedSaveEngine
        /// </summary>
        public static long EstimateDocumentSizePublic(FlowDocument document) => EstimateDocumentSize(document);
    }
}
