using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace NoteNest.UI.Controls
{
    public partial class SmartTextEditor
    {
        #region Multi-line Operations

        public void ConvertSelectionToBullets()
        {
            _isProcessingKey = true;
            var lines = GetSelectedOrCurrentLines();

            var savedCaret = CaretIndex;
            var savedSelStart = SelectionStart;
            var savedSelLength = SelectionLength;
            var firstVisible = GetFirstVisibleLineIndex();

            foreach (var lineInfo in lines.OrderByDescending(l => l.StartIndex))
            {
                var line = lineInfo.Text;
                var start = lineInfo.StartIndex;

                var cleanLine = RemoveListMarkers(line);
                var newLine = "• " + cleanLine.TrimStart();
                ReplaceLineAt(start, line.Length, newLine);
            }

            _isProcessingKey = false;
            RenumberEntireList();

            // Restore caret/selection/scroll
            SelectionStart = Math.Min(savedSelStart, Text.Length);
            SelectionLength = Math.Min(savedSelLength, Math.Max(0, Text.Length - SelectionStart));
            CaretIndex = Math.Min(savedCaret, Text.Length);
            if (firstVisible >= 0 && firstVisible < LineCount) ScrollToLine(firstVisible);
        }

        public void ConvertSelectionToNumbers()
        {
            _isProcessingKey = true;
            var lines = GetSelectedOrCurrentLines();

            var savedCaret = CaretIndex;
            var savedSelStart = SelectionStart;
            var savedSelLength = SelectionLength;
            var firstVisible = GetFirstVisibleLineIndex();

            int number = 1;
            foreach (var lineInfo in lines.OrderByDescending(l => l.StartIndex))
            {
                var line = lineInfo.Text;
                var start = lineInfo.StartIndex;

                var indent = Regex.Match(line, @"^(\s*)").Groups[1].Value;
                var cleanLine = RemoveListMarkers(line);
                var newLine = $"{indent}{number}. {cleanLine.TrimStart()}";
                ReplaceLineAt(start, line.Length, newLine);
                number++;
            }

            _isProcessingKey = false;
            RenumberEntireList();

            SelectionStart = Math.Min(savedSelStart, Text.Length);
            SelectionLength = Math.Min(savedSelLength, Math.Max(0, Text.Length - SelectionStart));
            CaretIndex = Math.Min(savedCaret, Text.Length);
            if (firstVisible >= 0 && firstVisible < LineCount) ScrollToLine(firstVisible);
        }

        public void ConvertSelectionToTasks()
        {
            _isProcessingKey = true;
            var lines = GetSelectedOrCurrentLines();

            var savedCaret = CaretIndex;
            var savedSelStart = SelectionStart;
            var savedSelLength = SelectionLength;
            var firstVisible = GetFirstVisibleLineIndex();

            foreach (var lineInfo in lines.OrderByDescending(l => l.StartIndex))
            {
                var line = lineInfo.Text;
                var start = lineInfo.StartIndex;

                var taskMatch = Regex.Match(line, @"^(\s*)([-*+•])\s+\[([ xX])\]\s+(.*)$");
                if (taskMatch.Success) continue;

                var indent = Regex.Match(line, @"^(\s*)").Groups[1].Value;
                var cleanLine = RemoveListMarkers(line);
                var newLine = $"{indent}- [ ] {cleanLine.TrimStart()}";
                ReplaceLineAt(start, line.Length, newLine);
            }

            _isProcessingKey = false;

            SelectionStart = Math.Min(savedSelStart, Text.Length);
            SelectionLength = Math.Min(savedSelLength, Math.Max(0, Text.Length - SelectionStart));
            CaretIndex = Math.Min(savedCaret, Text.Length);
            if (firstVisible >= 0 && firstVisible < LineCount) ScrollToLine(firstVisible);
        }

        public void RemoveListFormatting()
        {
            _isProcessingKey = true;
            var lines = GetSelectedOrCurrentLines();

            var savedCaret = CaretIndex;
            var savedSelStart = SelectionStart;
            var savedSelLength = SelectionLength;
            var firstVisible = GetFirstVisibleLineIndex();

            foreach (var lineInfo in lines.OrderByDescending(l => l.StartIndex))
            {
                var line = lineInfo.Text;
                var start = lineInfo.StartIndex;

                var cleanLine = RemoveListMarkers(line);
                if (cleanLine != line)
                {
                    ReplaceLineAt(start, line.Length, cleanLine.TrimStart());
                }
            }

            _isProcessingKey = false;

            SelectionStart = Math.Min(savedSelStart, Text.Length);
            SelectionLength = Math.Min(savedSelLength, Math.Max(0, Text.Length - SelectionStart));
            CaretIndex = Math.Min(savedCaret, Text.Length);
            if (firstVisible >= 0 && firstVisible < LineCount) ScrollToLine(firstVisible);
        }

        private string RemoveListMarkers(string line)
        {
            // Remove bullets
            line = Regex.Replace(line, @"^(\s*)([-*+•])\s+", "");
            // Remove numbers
            line = Regex.Replace(line, @"^(\s*)(\d+)[.)]\s+", "");
            // Remove task checkboxes
            line = Regex.Replace(line, @"^(\s*)([-*+•])\s+\[([ xX])\]\s+", "");
            return line;
        }

        private void IndentSelectedLines()
        {
            var lines = GetSelectedOrCurrentLines();
            var selStart = SelectionStart;
            var selLength = SelectionLength;
            
            foreach (var lineInfo in lines)
            {
                IndentLine(lineInfo.StartIndex, lineInfo.Text);
            }
            
            // Restore selection
            SelectionStart = selStart + lines.Count; // Account for added tabs
            SelectionLength = selLength + lines.Count;
            
            RenumberEntireList();
        }
        
        private void OutdentSelectedLines()
        {
            var lines = GetSelectedOrCurrentLines();
            var selStart = SelectionStart;
            var selLength = SelectionLength;
            var removed = 0;
            
            foreach (var lineInfo in lines)
            {
                var before = lineInfo.Text.Length;
                OutdentLine(lineInfo.StartIndex - removed, lineInfo.Text);
                var after = GetLineAt(lineInfo.StartIndex - removed).Length;
                removed += before - after;
            }
            
            // Restore selection (accounting for removed indentation)
            SelectionStart = Math.Max(0, selStart - (lines.Count > 0 ? 1 : 0));
            SelectionLength = Math.Max(0, selLength - removed);
            
            RenumberEntireList();
        }

        #endregion
    }
}


