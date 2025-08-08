using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NoteNest.UI.Controls
{
    public class SmartTextEditor : TextBox
    {
        private bool _isProcessingKey = false;
        
        public SmartTextEditor()
        {
            AcceptsReturn = true;
            AcceptsTab = true;
            FontFamily = new System.Windows.Media.FontFamily("Consolas");
            PreviewKeyDown += OnPreviewKeyDown;
        }

        #region Core Key Handling

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_isProcessingKey) return;
            
            switch (e.Key)
            {
                case Key.Enter:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        ToggleTaskComplete();
                        e.Handled = true;
                    }
                    else
                    {
                        HandleEnterKey(e);
                    }
                    break;
                case Key.Tab:
                    HandleTabKey(e);
                    break;
                case Key.Back:
                    HandleBackspaceKey(e);
                    break;
            }
        }

        private void HandleEnterKey(KeyEventArgs e)
        {
            var caretIndex = CaretIndex;
            var currentLine = GetCurrentLine();
            var lineStart = GetCurrentLineStart();
            
            // Check for bullet list patterns (including •)
            var bulletMatch = Regex.Match(currentLine, @"^(\s*)([-*+•])\s+(.*)$");
            if (bulletMatch.Success)
            {
                e.Handled = true;
                _isProcessingKey = true;
                
                var indent = bulletMatch.Groups[1].Value;
                var bullet = bulletMatch.Groups[2].Value;  // Preserve original bullet
                var content = bulletMatch.Groups[3].Value;
                
                if (string.IsNullOrWhiteSpace(content))
                {
                    // Empty bullet - remove it
                    Text = Text.Remove(lineStart, currentLine.Length);
                    CaretIndex = lineStart;
                }
                else
                {
                    // Continue with SAME bullet character
                    var newBullet = $"\n{indent}{bullet} ";
                    Text = Text.Insert(caretIndex, newBullet);
                    CaretIndex = caretIndex + newBullet.Length;
                }
                
                _isProcessingKey = false;
                return;
            }
            
            // Check for numbered list patterns
            var numberMatch = Regex.Match(currentLine, @"^(\s*)(\d+)([.)])\s+(.*)$");
            if (numberMatch.Success)
            {
                e.Handled = true;
                _isProcessingKey = true;
                
                var indent = numberMatch.Groups[1].Value;
                var number = int.Parse(numberMatch.Groups[2].Value);
                var content = numberMatch.Groups[4].Value;
                
                if (string.IsNullOrWhiteSpace(content))
                {
                    // Empty numbered item - remove it
                    Text = Text.Remove(lineStart, currentLine.Length);
                    CaretIndex = lineStart;
                }
                else
                {
                    // Continue numbered list (do not renumber here to avoid double-increment)
                    var nextNumber = number + 1;
                    var newItem = $"\n{indent}{nextNumber}. ";
                    Text = Text.Insert(caretIndex, newItem);
                    CaretIndex = caretIndex + newItem.Length;
                }
                
                _isProcessingKey = false;
                return;
            }
            
            // Check for task list patterns
            var taskMatch = Regex.Match(currentLine, @"^(\s*)([-*+•])\s+\[([ xX])\]\s+(.*)$");
            if (taskMatch.Success)
            {
                e.Handled = true;
                _isProcessingKey = true;
                
                var indent = taskMatch.Groups[1].Value;
                var bullet = taskMatch.Groups[2].Value;  // Preserve original bullet
                var content = taskMatch.Groups[4].Value;
                
                if (string.IsNullOrWhiteSpace(content))
                {
                    Text = Text.Remove(lineStart, currentLine.Length);
                    CaretIndex = lineStart;
                }
                else
                {
                    // Use same bullet character for task continuation
                    var newTask = $"\n{indent}{bullet} [ ] ";
                    Text = Text.Insert(caretIndex, newTask);
                    CaretIndex = caretIndex + newTask.Length;
                }
                
                _isProcessingKey = false;
                return;
            }
        }

        private void HandleTabKey(KeyEventArgs e)
        {
            // Check if we have a selection spanning multiple lines
            if (SelectionLength > 0)
            {
                e.Handled = true;
                _isProcessingKey = true;
                
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    OutdentSelectedLines();
                }
                else
                {
                    IndentSelectedLines();
                }
                
                _isProcessingKey = false;
                return;
            }
            
            // Single line handling
            var currentLine = GetCurrentLine();
            var lineStart = GetCurrentLineStart();
            
            var bulletMatch = Regex.Match(currentLine, @"^(\s*)([-*+•])\s+(.*)$");
            var numberMatch = Regex.Match(currentLine, @"^(\s*)(\d+)[.)]\s+(.*)$");
            var taskMatch = Regex.Match(currentLine, @"^(\s*)([-*+•])\s+\[([ xX])\]\s+(.*)$");
            
            if (bulletMatch.Success || numberMatch.Success || taskMatch.Success)
            {
                e.Handled = true;
                _isProcessingKey = true;
                
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    OutdentLine(lineStart, currentLine);
                }
                else
                {
                    IndentLine(lineStart, currentLine);
                }
                
                _isProcessingKey = false;
            }
        }

        private void HandleBackspaceKey(KeyEventArgs e)
        {
            if (CaretIndex == 0) return;
            
            var currentLine = GetCurrentLine();
            var lineStart = GetCurrentLineStart();
            var caretOffset = CaretIndex - lineStart;
            
            var bulletMatch = Regex.Match(currentLine, @"^(\s*)([-*+•])\s+$");
            var numberMatch = Regex.Match(currentLine, @"^(\s*)(\d+)[.)]\s+$");
            var taskMatch = Regex.Match(currentLine, @"^(\s*)([-*+•])\s+\[([ xX])\]\s+$");
            
            if ((bulletMatch.Success || numberMatch.Success || taskMatch.Success) && 
                caretOffset == currentLine.Length)
            {
                e.Handled = true;
                _isProcessingKey = true;
                Text = Text.Remove(lineStart, currentLine.Length);
                CaretIndex = lineStart;
                _isProcessingKey = false;
            }
        }

        #endregion

        #region Multi-line Operations

        public void ConvertSelectionToBullets()
        {
            _isProcessingKey = true;
            var lines = GetSelectedOrCurrentLines();
            
            foreach (var lineInfo in lines)
            {
                var line = lineInfo.Text;
                var start = lineInfo.StartIndex;
                
                // Remove existing list markers
                var cleanLine = RemoveListMarkers(line);
                
                // Add bullet
                var newLine = "• " + cleanLine.TrimStart();
                ReplaceLineAt(start, line.Length, newLine);
            }
            
            _isProcessingKey = false;
            RenumberEntireList(); // In case we converted numbered items
        }

        public void ConvertSelectionToNumbers()
        {
            _isProcessingKey = true;
            var lines = GetSelectedOrCurrentLines();
            int number = 1;
            
            foreach (var lineInfo in lines)
            {
                var line = lineInfo.Text;
                var start = lineInfo.StartIndex;
                
                // Get current indent
                var indent = Regex.Match(line, @"^(\s*)").Groups[1].Value;
                
                // Remove existing list markers
                var cleanLine = RemoveListMarkers(line);
                
                // Add number
                var newLine = $"{indent}{number}. {cleanLine.TrimStart()}";
                ReplaceLineAt(start, line.Length, newLine);
                number++;
            }
            
            _isProcessingKey = false;
            RenumberEntireList();
        }

        public void ConvertSelectionToTasks()
        {
            _isProcessingKey = true;
            var lines = GetSelectedOrCurrentLines();
            
            foreach (var lineInfo in lines)
            {
                var line = lineInfo.Text;
                var start = lineInfo.StartIndex;
                
                // Check if already a task
                var taskMatch = Regex.Match(line, @"^(\s*)([-*+•])\s+\[([ xX])\]\s+(.*)$");
                if (taskMatch.Success) continue;
                
                // Get current indent
                var indent = Regex.Match(line, @"^(\s*)").Groups[1].Value;
                
                // Remove existing list markers
                var cleanLine = RemoveListMarkers(line);
                
                // Add task checkbox
                var newLine = $"{indent}- [ ] {cleanLine.TrimStart()}";
                ReplaceLineAt(start, line.Length, newLine);
            }
            
            _isProcessingKey = false;
        }

        public void RemoveListFormatting()
        {
            _isProcessingKey = true;
            var lines = GetSelectedOrCurrentLines();
            
            foreach (var lineInfo in lines)
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

        #region Line Operations

        private void IndentLine(int lineStart, string currentLine)
        {
            var indent = "\t";
            Text = Text.Insert(lineStart, indent);
            CaretIndex = Math.Min(CaretIndex + indent.Length, Text.Length);
        }

        private void OutdentLine(int lineStart, string currentLine)
        {
            if (currentLine.StartsWith("\t"))
            {
                Text = Text.Remove(lineStart, 1);
                CaretIndex = Math.Max(CaretIndex - 1, lineStart);
            }
            else if (currentLine.StartsWith("    "))
            {
                Text = Text.Remove(lineStart, 4);
                CaretIndex = Math.Max(CaretIndex - 4, lineStart);
            }
            else if (currentLine.StartsWith(" "))
            {
                var spacesToRemove = Math.Min(4, currentLine.TakeWhile(c => c == ' ').Count());
                Text = Text.Remove(lineStart, spacesToRemove);
                CaretIndex = Math.Max(CaretIndex - spacesToRemove, lineStart);
            }
        }

        #endregion

        #region Public Methods for Toolbar

        public void InsertBulletList()
        {
            if (SelectionLength > 0)
            {
                ConvertSelectionToBullets();
            }
            else
            {
                _isProcessingKey = true;
                var lineStart = GetCurrentLineStart();
                var currentLine = GetCurrentLine();
                
                if (!Regex.IsMatch(currentLine, @"^(\s*)([-*+•])\s+"))
                {
                    Text = Text.Insert(lineStart, "• ");
                    CaretIndex = lineStart + 2;
                }
                _isProcessingKey = false;
            }
        }

        public void InsertNumberedList()
        {
            if (SelectionLength > 0)
            {
                ConvertSelectionToNumbers();
            }
            else
            {
                _isProcessingKey = true;
                var lineStart = GetCurrentLineStart();
                var currentLine = GetCurrentLine();
                
                if (!Regex.IsMatch(currentLine, @"^(\s*)(\d+)[.)]\s+"))
                {
                    Text = Text.Insert(lineStart, "1. ");
                    CaretIndex = lineStart + 3;
                }
                _isProcessingKey = false;
            }
        }

        public void InsertTaskList()
        {
            if (SelectionLength > 0)
            {
                ConvertSelectionToTasks();
            }
            else
            {
                _isProcessingKey = true;
                var lineStart = GetCurrentLineStart();
                var currentLine = GetCurrentLine();
                
                if (!Regex.IsMatch(currentLine, @"^(\s*)([-*+•])\s+\[([ xX])\]\s+"))
                {
                    Text = Text.Insert(lineStart, "- [ ] ");
                    CaretIndex = lineStart + 6;
                }
                _isProcessingKey = false;
            }
        }

        public void ToggleTaskComplete()
        {
            var lines = GetSelectedOrCurrentLines();
            _isProcessingKey = true;
            
            foreach (var lineInfo in lines)
            {
                var match = Regex.Match(lineInfo.Text, @"^(\s*)([-*+•])\s+\[([ xX])\]\s+(.*)$");
                if (match.Success)
                {
                    var isChecked = match.Groups[3].Value.ToLower() == "x";
                    var newLine = lineInfo.Text.Replace(
                        isChecked ? $"[{match.Groups[3].Value}]" : "[ ]",
                        isChecked ? "[ ]" : "[x]"
                    );
                    ReplaceLineAt(lineInfo.StartIndex, lineInfo.Text.Length, newLine);
                }
            }
            
            _isProcessingKey = false;
        }

        public void IndentSelection()
        {
            _isProcessingKey = true;
            IndentSelectedLines();
            _isProcessingKey = false;
        }
        
        public void OutdentSelection()
        {
            _isProcessingKey = true;
            OutdentSelectedLines();
            _isProcessingKey = false;
        }

        #endregion

        #region Helper Methods

        private List<LineInfo> GetSelectedOrCurrentLines()
        {
            var result = new List<LineInfo>();
            
            if (SelectionLength == 0)
            {
                // Just current line
                result.Add(new LineInfo
                {
                    Text = GetCurrentLine(),
                    StartIndex = GetCurrentLineStart(),
                    LineNumber = GetCurrentLineNumber()
                });
            }
            else
            {
                // All lines in selection
                var selStart = SelectionStart;
                var selEnd = SelectionStart + SelectionLength;
                
                var lines = Text.Split('\n');
                var currentPos = 0;
                
                for (int i = 0; i < lines.Length; i++)
                {
                    var lineStart = currentPos;
                    var lineEnd = currentPos + lines[i].Length;
                    
                    // Check if this line is part of the selection
                    if (lineEnd >= selStart && lineStart <= selEnd)
                    {
                        result.Add(new LineInfo
                        {
                            Text = lines[i],
                            StartIndex = lineStart,
                            LineNumber = i
                        });
                    }
                    
                    currentPos = lineEnd + 1; // +1 for the \n
                }
            }
            
            return result;
        }

        private void ReplaceLineAt(int startIndex, int length, string newText)
        {
            Text = Text.Remove(startIndex, length).Insert(startIndex, newText);
        }
        
        private string GetLineAt(int startIndex)
        {
            var endIndex = Text.IndexOf('\n', startIndex);
            if (endIndex == -1) endIndex = Text.Length;
            return Text.Substring(startIndex, endIndex - startIndex);
        }
        
        private string GetCurrentLine()
        {
            if (string.IsNullOrEmpty(Text)) return "";
            
            var lines = Text.Split('\n');
            var currentLineIndex = Text.Substring(0, Math.Min(CaretIndex, Text.Length))
                                      .Count(c => c == '\n');
            
            return currentLineIndex < lines.Length ? lines[currentLineIndex] : "";
        }
        
        private int GetCurrentLineStart()
        {
            if (string.IsNullOrEmpty(Text)) return 0;
            
            var textBeforeCaret = Text.Substring(0, Math.Min(CaretIndex, Text.Length));
            var lastNewline = textBeforeCaret.LastIndexOf('\n');
            return lastNewline + 1;
        }
        
        private int GetCurrentLineNumber()
        {
            return Text.Substring(0, Math.Min(CaretIndex, Text.Length)).Count(c => c == '\n');
        }
        
        private void RenumberList(int fromPosition, string indent, int startNumber)
        {
            var lines = Text.Split('\n');
            var currentLineIndex = Text.Substring(0, fromPosition).Count(c => c == '\n');
            var modified = false;
            var currentNumber = startNumber;
            
            for (int i = currentLineIndex + 1; i < lines.Length; i++)
            {
                var lineMatch = Regex.Match(lines[i], @"^(\s*)(\d+)([.)])\s+(.*)$");
                if (lineMatch.Success && lineMatch.Groups[1].Value == indent)
                {
                    lines[i] = $"{indent}{currentNumber}. {lineMatch.Groups[4].Value}";
                    currentNumber++;
                    modified = true;
                }
                else if (!lineMatch.Success || lineMatch.Groups[1].Value.Length < indent.Length)
                {
                    break;
                }
            }
            
            if (modified)
            {
                var savedCaret = CaretIndex;
                Text = string.Join("\n", lines);
                CaretIndex = savedCaret;
            }
        }
        
        private void RenumberEntireList()
        {
            var lines = Text.Split('\n');
            var numbersByIndent = new Dictionary<string, int>();
            var modified = false;
            
            for (int i = 0; i < lines.Length; i++)
            {
                var match = Regex.Match(lines[i], @"^(\s*)(\d+)([.)])\s+(.*)$");
                if (match.Success)
                {
                    var indent = match.Groups[1].Value;
                    
                    var keysToRemove = numbersByIndent.Keys.Where(k => k.Length > indent.Length).ToList();
                    foreach (var key in keysToRemove)
                    {
                        numbersByIndent.Remove(key);
                    }
                    
                    if (!numbersByIndent.ContainsKey(indent))
                    {
                        numbersByIndent[indent] = 1;
                    }
                    
                    var expectedNumber = numbersByIndent[indent];
                    var actualNumber = int.Parse(match.Groups[2].Value);
                    
                    if (actualNumber != expectedNumber)
                    {
                        // Preserve the delimiter style (. or ))
                        var delimiter = match.Groups[3].Value;
                        lines[i] = $"{indent}{expectedNumber}{delimiter} {match.Groups[4].Value}";
                        modified = true;
                    }
                    
                    numbersByIndent[indent] = expectedNumber + 1;
                }
                else
                {
                    numbersByIndent.Clear();
                }
            }
            
            if (modified)
            {
                var savedCaret = CaretIndex;
                var savedSelStart = SelectionStart;
                var savedSelLength = SelectionLength;
                Text = string.Join("\n", lines);
                CaretIndex = Math.Min(savedCaret, Text.Length);
                try
                {
                    SelectionStart = savedSelStart;
                    SelectionLength = savedSelLength;
                }
                catch { }
            }
        }
        
        private class LineInfo
        {
            public string Text { get; set; }
            public int StartIndex { get; set; }
            public int LineNumber { get; set; }
        }

        #endregion
    }
}


