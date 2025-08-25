using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using NoteNest.Core.Models;

namespace NoteNest.UI.Controls
{
    public partial class SmartTextEditor
    {
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
            if (EditingFormat == NoteFormat.Markdown)
            {
                // In markdown mode, preserve raw syntax; let default behavior handle Enter
                return;
            }

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
            if (EditingFormat == NoteFormat.Markdown)
            {
                // In markdown mode, just insert a tab character
                e.Handled = true;
                var caret = CaretIndex;
                Text = Text.Insert(caret, "\t");
                CaretIndex = caret + 1;
                return;
            }

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
            
            // Store the original caret position
            var originalCaret = CaretIndex;
            
            var bulletMatch = Regex.Match(currentLine, @"^(\s*)([-*+•])\s+(.*)$");
            var numberMatch = Regex.Match(currentLine, @"^(\s*)(\d+)[.)]\s+(.*)$");
            var taskMatch = Regex.Match(currentLine, @"^(\s*)([-*+•])\s+\[([ xX])\]\s+(.*)$");
            
            if (bulletMatch.Success || numberMatch.Success || taskMatch.Success)
            {
                e.Handled = true;
                
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    OutdentLine(lineStart, currentLine);
                }
                else
                {
                    IndentLine(lineStart, currentLine);
                }
                
                // Ensure cursor remains visible and positioned appropriately
                this.Focus();
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
    }
}


