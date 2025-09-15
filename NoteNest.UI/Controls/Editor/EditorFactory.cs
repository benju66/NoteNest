using System;
using System.IO;
using NoteNest.Core.Models;
using NoteNest.UI.Controls.Editor.Core;

namespace NoteNest.UI.Controls.Editor
{
    /// <summary>
    /// Factory for creating appropriate editor instances
    /// </summary>
    public static class EditorFactory
    {
        public static INotesEditor CreateEditor(string filePath)
        {
            var format = DetectFormat(filePath);
            return CreateEditor(format);
        }
        
        public static INotesEditor CreateEditor(NoteFormat format)
        {
            return format switch
            {
                NoteFormat.RTF => new RTFTextEditor(),
                NoteFormat.Markdown => new FormattedTextEditor(),
                NoteFormat.PlainText => new FormattedTextEditor(), // For now, use markdown editor for plain text
                _ => new FormattedTextEditor() // Default to markdown
            };
        }
        
        public static NoteFormat DetectFormat(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return NoteFormat.Markdown;
                
            var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
            return extension switch
            {
                ".rtf" => NoteFormat.RTF,
                ".md" => NoteFormat.Markdown,
                ".markdown" => NoteFormat.Markdown,
                ".txt" => NoteFormat.PlainText,
                _ => NoteFormat.Markdown
            };
        }
        
        public static string GetExtension(NoteFormat format)
        {
            return format switch
            {
                NoteFormat.RTF => ".rtf",
                NoteFormat.Markdown => ".md",
                NoteFormat.PlainText => ".txt",
                _ => ".md"
            };
        }
    }
}
