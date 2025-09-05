using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Diagnostics;

namespace NoteNest.UI.Controls
{
    public static class ListFormatting
    {
        public const double HangingIndentSize = 24;
        public const double BulletWidth = 20;
        public const double NumberWidth = 30;
        
        // Existing method remains unchanged
        public static void ApplyHangingIndentToListItem(ListItem item)
        {
            if (item?.Blocks.FirstBlock is not Paragraph para) return;
            
            // Get the list style to determine indent size
            if (item.Parent is List list)
            {
                double indentSize = list.MarkerStyle == TextMarkerStyle.Decimal 
                    ? NumberWidth 
                    : BulletWidth;
                
                // Apply hanging indent
                // First line starts at 0, subsequent lines indented
                para.TextIndent = -indentSize;
                para.Padding = new Thickness(indentSize, 0, 0, 0);
                
                // Ensure proper line spacing
                para.LineHeight = para.FontSize * 1.5;
            }
        }
        
        // Existing method remains unchanged
        public static void ApplyNestedIndent(ListItem item, int level)
        {
            if (item?.Blocks.FirstBlock is not Paragraph para) return;
            
            // Each level adds more left margin
            double baseIndent = level * 30;
            para.Margin = new Thickness(baseIndent, 0, 0, 0);
        }
        
        // NEW: Validate and repair list structure
        public static bool ValidateAndRepairList(List list)
        {
            if (list == null) return false;
            
            bool isValid = true;
            
            try
            {
                // Check each list item
                foreach (ListItem item in list.ListItems)
                {
                    // Ensure every list item has at least one block
                    if (item.Blocks.Count == 0)
                    {
                        Debug.WriteLine("[REPAIR] Adding empty paragraph to list item with no blocks");
                        item.Blocks.Add(new Paragraph());
                        isValid = false;
                    }
                    
                    // Ensure first block is a paragraph
                    if (item.Blocks.FirstBlock is not Paragraph)
                    {
                        Debug.WriteLine("[WARNING] List item's first block is not a paragraph");
                        isValid = false;
                    }
                    
                    // Apply proper formatting
                    ApplyHangingIndentToListItem(item);
                }
                
                return isValid;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] List validation failed: {ex.Message}");
                return false;
            }
        }
        
        // NEW: Get clean text from paragraph (no formatting)
        public static string GetCleanText(Paragraph para)
        {
            if (para == null) return string.Empty;
            
            try
            {
                var range = new TextRange(para.ContentStart, para.ContentEnd);
                return range.Text?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
        
        // NEW: Calculate precise caret offset
        public static int GetCaretOffset(Paragraph para, TextPointer caretPosition)
        {
            if (para == null || caretPosition == null) return 0;
            
            try
            {
                var range = new TextRange(para.ContentStart, caretPosition);
                var text = range.Text ?? string.Empty;
                
                // Account for any special characters
                return text.Length;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WARNING] GetCaretOffset failed: {ex.Message}");
                return 0;
            }
        }
        
        // NEW: Set caret at specific offset with validation
        public static void SetCaretAtOffset(Paragraph para, int offset)
        {
            if (para == null) return;
            
            try
            {
                var position = para.ContentStart;
                int currentOffset = 0;
                
                while (position != null && currentOffset < offset)
                {
                    var next = position.GetNextInsertionPosition(LogicalDirection.Forward);
                    if (next == null) break;
                    
                    position = next;
                    currentOffset++;
                }
                
                if (position != null)
                {
                    // Set the caret
                    var editor = para.Parent as RichTextBox;
                    if (editor != null)
                    {
                        editor.CaretPosition = position;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] SetCaretAtOffset failed: {ex.Message}");
            }
        }
    }
}