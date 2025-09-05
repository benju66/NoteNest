using System;
using System.Windows;
using System.Windows.Documents;

namespace NoteNest.UI.Controls
{
    public static class ListFormatting
    {
        public const double HangingIndentSize = 24;
        public const double BulletWidth = 20;
        public const double NumberWidth = 30;
        
        public static void ApplyHangingIndentToListItem(ListItem item)
        {
            if (item.Blocks.FirstBlock is not Paragraph para) return;
            
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
        
        public static void ApplyNestedIndent(ListItem item, int level)
        {
            if (item.Blocks.FirstBlock is not Paragraph para) return;
            
            // Each level adds more left margin
            double baseIndent = level * 30;
            para.Margin = new Thickness(baseIndent, 0, 0, 0);
        }
    }
}
