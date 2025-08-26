using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NoteNest.Core.Models;

namespace NoteNest.UI.Controls
{
    public partial class SmartTextEditor
    {
        #region Scroll Preservation Support

        private int _lineCount;
        private NoteFormat _editingFormat = NoteFormat.PlainText;

        public NoteFormat EditingFormat
        {
            get => _editingFormat;
            set
            {
                _editingFormat = value;
                UpdateEditingMode();
            }
        }

        private int LogicalLineCount
        {
            get
            {
                if (_lineCount == 0 && !string.IsNullOrEmpty(Text))
                    _lineCount = Text.Split('\n').Length;
                return _lineCount;
            }
        }

        private int GetFirstVisibleLogicalLineIndex()
        {
            try
            {
                var scrollViewer = GetScrollViewer();
                if (scrollViewer == null) return -1;
                
                var verticalOffset = scrollViewer.VerticalOffset;
                var lineHeight = FontSize * 1.3;
                return Math.Max(0, (int)(verticalOffset / lineHeight));
            }
            catch
            {
                return -1;
            }
        }

        private void ScrollToLogicalLine(int lineIndex)
        {
            if (lineIndex < 0) return;
            
            try
            {
                var scrollViewer = GetScrollViewer();
                if (scrollViewer == null) return;
                
                var lineHeight = FontSize * 1.3;
                var offset = lineIndex * lineHeight;
                scrollViewer.ScrollToVerticalOffset(offset);
            }
            catch { }
        }

        private ScrollViewer GetScrollViewer()
        {
            if (Template?.FindName("PART_ContentHost", this) is ScrollViewer sv)
                return sv;
            return null;
        }

        private void UpdateEditingMode()
        {
            // Update tooltip and behavior based on format
            if (_editingFormat == NoteFormat.Markdown)
            {
                ToolTip = "Markdown mode - syntax preserved";
            }
            else
            {
                ToolTip = "Plain text mode";
            }
        }

        #endregion

        #region Spell Check & Format Helpers

        public void SetSpellCheckEnabled(bool enabled)
        {
            try { SpellCheck.IsEnabled = enabled; } catch { }
        }

        public void SetSpellCheckLanguage(string cultureName)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(cultureName))
                {
                    Language = System.Windows.Markup.XmlLanguage.GetLanguage(cultureName);
                }
            }
            catch
            {
                try { Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-US"); } catch { }
            }
        }

        public void UpdateFormatSettings(NoteFormat format)
        {
            EditingFormat = format;
            // Additional format-specific tweaks could be added here later
        }

        #endregion
    }
}
