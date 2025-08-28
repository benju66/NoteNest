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

        // private int GetFirstVisibleLogicalLineIndex()
        // {
        //     // Not reliable with TextBox; prefer caret/selection restore only
        //     return -1;
        // }

        // private void ScrollToLogicalLine(int lineIndex)
        // {
        //     // Not used; removing to avoid scroll jitter
        // }

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
