using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NoteNest.Core.Models;

namespace NoteNest.UI.Controls
{
    public partial class SmartTextEditor : TextBox
    {
        private bool _isProcessingKey = false;
        public static readonly System.Windows.DependencyProperty IsModifiedProperty = System.Windows.DependencyProperty.Register(
            name: "IsModified",
            propertyType: typeof(bool),
            ownerType: typeof(SmartTextEditor),
            typeMetadata: new System.Windows.FrameworkPropertyMetadata(false, System.Windows.FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly System.Windows.DependencyProperty FilePathProperty = System.Windows.DependencyProperty.Register(
            name: "FilePath",
            propertyType: typeof(string),
            ownerType: typeof(SmartTextEditor),
            typeMetadata: new System.Windows.FrameworkPropertyMetadata(string.Empty, System.Windows.FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public bool IsModified
        {
            get => (bool)GetValue(IsModifiedProperty);
            set => SetValue(IsModifiedProperty, value);
        }

        public string FilePath
        {
            get => (string)GetValue(FilePathProperty);
            set => SetValue(FilePathProperty, value);
        }
        
        public SmartTextEditor()
        {
            AcceptsReturn = true;
            AcceptsTab = true;
            FontFamily = new System.Windows.Media.FontFamily("Consolas");
            // Enable wrapping by default
            TextWrapping = System.Windows.TextWrapping.Wrap;
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
            PreviewKeyDown += OnPreviewKeyDown;

            // Default spell-check settings (can be overridden via methods below)
            try
            {
                SpellCheck.IsEnabled = true;
                Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-US");
            }
            catch { }
        }

        protected override void OnTextChanged(TextChangedEventArgs e)
        {
            base.OnTextChanged(e);
            _lineCount = 0;
            if (!_isProcessingKey)
            {
                IsModified = true;
            }
        }

        

        

        

        #region Line Operations

        private void IndentLine(int lineStart, string currentLine)
        {
            _isProcessingKey = true;
            
            var indent = "\t";
            var caretOffset = CaretIndex - lineStart; // Remember position within line
            
            // Check if this is a numbered list item that should become a bullet when indented
            var numberMatch = Regex.Match(currentLine, @"^(\s*)(\d+)[.)]\s+(.*)$");
            if (numberMatch.Success)
            {
                var existingIndent = numberMatch.Groups[1].Value;
                var content = numberMatch.Groups[3].Value;
                
                // Convert to bullet list when indenting
                var newLine = $"{existingIndent}\t• {content}";
                
                // Replace the entire line
                var lineEnd = Text.IndexOf('\n', lineStart);
                if (lineEnd == -1) lineEnd = Text.Length;
                
                Text = Text.Remove(lineStart, lineEnd - lineStart);
                Text = Text.Insert(lineStart, newLine);
                
                // Position cursor after the bullet and space
                CaretIndex = lineStart + existingIndent.Length + indent.Length + 2; // +2 for "• "
            }
            else
            {
                // For bullets or regular text, just add indent
                Text = Text.Insert(lineStart, indent);
                
                // Restore cursor position (accounting for added tab)
                CaretIndex = lineStart + caretOffset + indent.Length;
            }
            
            _isProcessingKey = false;
        }

        private void OutdentLine(int lineStart, string currentLine)
        {
            _isProcessingKey = true;
            
            var caretOffset = CaretIndex - lineStart;
            var removedChars = 0;
            
            // If a single-tab indented bullet is being outdented to root and the preceding
            // sibling at that level is a numbered item, convert back to a number. Otherwise,
            // simply remove one level of indentation and keep it a bullet.
            var bulletIndentMatch = Regex.Match(currentLine, @"^(\t+)([-*+•])\s+(.*)$");
            if (bulletIndentMatch.Success)
            {
                var existingTabs = bulletIndentMatch.Groups[1].Value.Length;
                var content = bulletIndentMatch.Groups[3].Value;

                if (existingTabs == 1)
                {
                    if (TryFindNextNumberForOutdentAtLevel(lineStart, targetIndentTabs: 0, out var nextNumber))
                    {
                        var newLine = $"{nextNumber}. {content}";
                        var lineEndIdx = Text.IndexOf('\n', lineStart);
                        if (lineEndIdx == -1) lineEndIdx = Text.Length;
                        Text = Text.Remove(lineStart, lineEndIdx - lineStart);
                        Text = Text.Insert(lineStart, newLine);

                        CaretIndex = lineStart + Math.Min(caretOffset, newLine.Length);

                        _isProcessingKey = false;
                        RenumberEntireList();
                        _isProcessingKey = true;
                        _isProcessingKey = false;
                        return;
                    }
                }
            }

            // Default behavior: remove one indentation level without changing bullet/number style
            if (currentLine.StartsWith("\t"))
            {
                Text = Text.Remove(lineStart, 1);
                removedChars = 1;
                CaretIndex = Math.Max(lineStart, lineStart + caretOffset - removedChars);
            }
            else if (currentLine.StartsWith("    "))
            {
                Text = Text.Remove(lineStart, 4);
                removedChars = 4;
                CaretIndex = Math.Max(lineStart, lineStart + caretOffset - removedChars);
            }
            else if (currentLine.StartsWith(" "))
            {
                var spacesToRemove = Math.Min(4, currentLine.TakeWhile(c => c == ' ').Count());
                Text = Text.Remove(lineStart, spacesToRemove);
                removedChars = spacesToRemove;
                CaretIndex = Math.Max(lineStart, lineStart + caretOffset - removedChars);
            }
            else
            {
                // No indentation to remove
                CaretIndex = lineStart + caretOffset;
            }
            
            _isProcessingKey = false;
        }

        private bool TryFindNextNumberForOutdentAtLevel(int lineStart, int targetIndentTabs, out int nextNumber)
        {
            nextNumber = 1;
            var lines = Text.Split('\n');
            var currentLineIndex = Text.Substring(0, lineStart).Count(c => c == '\n');

            for (int i = currentLineIndex - 1; i >= 0; i--)
            {
                var line = lines[i];

                // Count leading tabs only; mixed spaces are ignored for conversion
                int tabs = 0;
                while (tabs < line.Length && line[tabs] == '\t') tabs++;

                if (tabs > targetIndentTabs)
                {
                    // Deeper indent, skip up the tree
                    continue;
                }

                if (tabs < targetIndentTabs)
                {
                    // We reached a shallower level; no numbered context at desired level
                    return false;
                }

                // Same level as target
                var numberMatch = Regex.Match(line, @"^(\t*)(\d+)[.)]\s+");
                if (numberMatch.Success && numberMatch.Groups[1].Value.Length == targetIndentTabs)
                {
                    nextNumber = int.Parse(numberMatch.Groups[2].Value) + 1;
                    return true;
                }

                // Encountered a non-numbered line at the same level → stop, don't convert
                return false;
            }

            // No previous line at the same level → do not convert
            return false;
        }

        private int FindNextNumberForOutdent(int lineStart)
        {
            var lines = Text.Split('\n');
            var currentLineIndex = Text.Substring(0, lineStart).Count(c => c == '\n');
            
            // Look backwards for the last non-indented numbered item
            for (int i = currentLineIndex - 1; i >= 0; i--)
            {
                // Skip indented lines (bullets or indented numbers)
                if (lines[i].StartsWith("\t") || lines[i].StartsWith(" "))
                    continue;
                
                var match = Regex.Match(lines[i], @"^(\d+)[.)]\s+");
                if (match.Success)
                {
                    // Found a previous number at the same level, so we should be next
                    return int.Parse(match.Groups[1].Value) + 1;
                }
                
                // If we hit a non-list line at root level, stop looking
                if (!string.IsNullOrWhiteSpace(lines[i]) && !lines[i].StartsWith("\t"))
                    break;
            }
            
            // If no previous number found, we're starting a new list
            return 1;
        }

        #endregion

        #region Public Methods for Toolbar

        public void InsertBulletList()
        {
			_isProcessingKey = true;
			
			if (SelectionLength > 0)
			{
				ConvertSelectionToBullets();
			}
			else
			{
				var lineStart = GetCurrentLineStart();
				var currentLine = GetCurrentLine();
				
				// Check if already a bullet list
				if (!Regex.IsMatch(currentLine, @"^(\s*)([-*+•])\s+"))
				{
					// Store the relative position in the line
					var relativePosition = CaretIndex - lineStart;
					
					// If current line is empty, just add bullet
					if (string.IsNullOrWhiteSpace(currentLine))
					{
						Text = Text.Insert(CaretIndex, "• ");
						CaretIndex = CaretIndex + 2;
					}
					else
					{
						// Add bullet at beginning of line
						Text = Text.Insert(lineStart, "• ");
						// Position cursor after the bullet, maintaining relative position
						CaretIndex = lineStart + 2 + relativePosition;
					}
				}
			}
			
			_isProcessingKey = false;
        }

        public void InsertNumberedList()
        {
			_isProcessingKey = true;
			
			if (SelectionLength > 0)
			{
				ConvertSelectionToNumbers();
			}
			else
			{
				var lineStart = GetCurrentLineStart();
				var currentLine = GetCurrentLine();
				
				// Check if already a numbered list
				if (!Regex.IsMatch(currentLine, @"^(\s*)(\d+)[.)]\s+"))
				{
					// Find the appropriate number
					var nextNumber = FindNextNumberForCurrentPosition();
					
					// Store the relative position in the line
					var relativePosition = CaretIndex - lineStart;
					
					// If current line is empty, just add number
					if (string.IsNullOrWhiteSpace(currentLine))
					{
						Text = Text.Insert(CaretIndex, $"{nextNumber}. ");
						CaretIndex = CaretIndex + nextNumber.ToString().Length + 2;
					}
					else
					{
						// Add number at beginning of line
						var numberPrefix = $"{nextNumber}. ";
						Text = Text.Insert(lineStart, numberPrefix);
						// Position cursor after the number, maintaining relative position
						CaretIndex = lineStart + numberPrefix.Length + relativePosition;
					}
					
					// Renumber if needed
					RenumberEntireList();
				}
			}
			
			_isProcessingKey = false;
        }

		// Add helper to find next number for current position
		private int FindNextNumberForCurrentPosition()
		{
			var lines = Text.Split('\n');
			var currentLineIndex = Text.Substring(0, Math.Min(CaretIndex, Text.Length))
									  .Count(c => c == '\n');
			
			// Look backwards for the last numbered item
			for (int i = currentLineIndex - 1; i >= 0; i--)
			{
				var match = Regex.Match(lines[i], @"^(\d+)[.)]\s+");
				if (match.Success)
				{
					return int.Parse(match.Groups[1].Value) + 1;
				}
				
				// If we hit a non-list line, start at 1
				if (!string.IsNullOrWhiteSpace(lines[i]) && 
					!lines[i].StartsWith("\t") && 
					!Regex.IsMatch(lines[i], @"^(\s*)([-*+•])\s+"))
				{
					break;
				}
			}
			
			return 1; // Default to 1 if no previous number found
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
            var savedCaret = CaretIndex;
            
            for (int i = 0; i < lines.Length; i++)
            {
                var match = Regex.Match(lines[i], @"^(\s*)(\d+)([.)])\s+(.*)$");
                if (match.Success)
                {
                    var indent = match.Groups[1].Value;
                    
                    // If this is at root level (no indent), reset any nested counters
                    if (string.IsNullOrEmpty(indent))
                    {
                        // Clear any indented number counters
                        var keysToRemoveRoot = numbersByIndent.Keys.Where(k => k.Length > 0).ToList();
                        foreach (var key in keysToRemoveRoot)
                        {
                            numbersByIndent.Remove(key);
                        }
                    }
                    else
                    {
                        // For indented items, clear deeper indents
                        var keysToRemove = numbersByIndent.Keys.Where(k => k.Length > indent.Length).ToList();
                        foreach (var key in keysToRemove)
                        {
                            numbersByIndent.Remove(key);
                        }
                    }
                    
                    // Initialize or get counter for this indent level
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
                else if (!lines[i].StartsWith("\t") && !lines[i].StartsWith(" "))
                {
                    // Non-indented, non-numbered line - reset root level counter
                    if (numbersByIndent.ContainsKey(""))
                    {
                        numbersByIndent.Remove("");
                    }
                }
                // Don't reset counters for indented bullets - they don't break the numbering
            }
            
            if (modified)
            {
                Text = string.Join("\n", lines);
                CaretIndex = Math.Min(savedCaret, Text.Length);
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


