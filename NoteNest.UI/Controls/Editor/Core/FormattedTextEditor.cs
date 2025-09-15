using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NoteNest.UI.Controls.Editor.Converters;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using NoteNest.Core.Models;
// Removed: using NoteNest.UI.Controls.Editor.Support (deleted in new architecture)

namespace NoteNest.UI.Controls.Editor.Core
{
    public partial class FormattedTextEditor : RichTextBox, INotesEditor
    {
        private readonly MarkdownFlowDocumentConverter _converter;
        private NoteModel _currentNote;
        private NoteNest.Core.Services.NoteMetadataManager _metadataManager;
        
        // New architecture - FlowDocument is single source of truth during editing
        private bool _isDirty = false;
        private string _originalMarkdown = string.Empty;
        
        // Lightweight markdown cache for WAL protection
        private string _cachedMarkdown = "";
        private DateTime _lastCacheTime = DateTime.MinValue;
        
        // UX POLISH: Event for toolbar button state feedback
        public event EventHandler<ListStateChangedEventArgs>? ListStateChanged;
        private ListState _currentListState = new ListState();
        
        // PERFORMANCE: Debounced state change notifications
        private readonly DispatcherTimer _stateUpdateTimer;
        private bool _stateUpdatePending = false;

        // Removed: MarkdownContentProperty with two-way binding
        // New architecture: Use LoadFromMarkdown() and SaveToMarkdown() instead
        
        public bool IsDirty => _isDirty;
        
        public string OriginalMarkdown => _originalMarkdown;

        // INotesEditor interface implementation
        public NoteFormat Format => NoteFormat.Markdown;
        public event EventHandler ContentChanged;
        public string OriginalContent => _originalMarkdown;
        
        // Interface methods that delegate to existing implementations
        public void LoadContent(string content) => LoadFromMarkdown(content);
        public string SaveContent() => SaveToMarkdown();
        public string GetQuickContent() => GetQuickMarkdown();
        
        public void ToggleBold() => EditingCommands.ToggleBold.Execute(null, this);
        public void ToggleItalic() => EditingCommands.ToggleItalic.Execute(null, this);

        public NoteModel CurrentNote 
        {
            get => _currentNote;
            set => _currentNote = value;
        }

        public void SetMetadataManager(NoteNest.Core.Services.NoteMetadataManager manager)
        {
            _metadataManager = manager;
        }

        private void ApplySettings(EditorSettings settings)
        {
            if (settings == null) return;
            
            Document.FontFamily = new System.Windows.Media.FontFamily(settings.FontFamily);
            Document.FontSize = settings.FontSize;
            // Apply other settings...
        }

        public FormattedTextEditor()
        {
            _converter = new MarkdownFlowDocumentConverter();
            IsReadOnly = false;
            IsReadOnlyCaretVisible = true;
            Focusable = true;
            IsHitTestVisible = true;
            SetResourceReference(BackgroundProperty, "SystemControlBackgroundAltHighBrush");
            SetResourceReference(ForegroundProperty, "SystemControlForegroundBaseHighBrush");
            AcceptsReturn = true;
            AcceptsTab = true;
            FocusVisualStyle = null;

            // Normalize default paragraph spacing: small bottom margin, no extra top
            try
            {
                if (Document != null)
                {
                    InitializeDocumentStyles(Document);
                }
            }
            catch { }

            // PROPER ARCHITECTURE: Simple dirty tracking only
            // SplitPaneView will attach direct TextChanged handler for save coordination
            TextChanged += (s, e) => MarkDirty();
            GotFocus += (s, e) => Keyboard.Focus(this);
            
            // PERFORMANCE: Initialize debounced state update timer
            _stateUpdateTimer = new DispatcherTimer 
            { 
                Interval = TimeSpan.FromMilliseconds(50) // Debounce rapid changes
            };
            _stateUpdateTimer.Tick += (s, e) => {
                _stateUpdateTimer.Stop();
                _stateUpdatePending = false;
                CheckAndNotifyListStateChange();
            };
            
            // UX POLISH: Monitor caret position changes with performance optimization
            SelectionChanged += (s, e) => {
                if (!_stateUpdatePending) {
                    _stateUpdatePending = true;
                    _stateUpdateTimer.Start();
                }
            };

            // Editing command keybindings
            InputBindings.Add(new KeyBinding(EditingCommands.ToggleBold, Key.B, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(EditingCommands.ToggleItalic, Key.I, ModifierKeys.Control));

            // Register custom command bindings
            RegisterCommandBindings();
            
            // Removed: Complex numbering system initialization

            // Smart list behaviors
            PreviewKeyDown += OnPreviewKeyDown;
            PreviewKeyUp += OnPreviewKeyUp;
            
            // PHASE 1.5: Clean paste behavior - always paste as plain text
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, OnPreviewPaste));
        }

        /// <summary>
        /// PHASE 2: Perfect predictable key handlers for professional editing experience
        /// </summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    if (Keyboard.Modifiers == ModifierKeys.Shift)
                    {
                        // Shift+Enter: soft line break (always)
                        e.Handled = true;
                        EditingCommands.EnterLineBreak.Execute(null, this);
                        return;
                    }
                    
                    // PHASE 2: Predictable list behavior per original plan
                    if (IsInList())
                    {
                        if (CurrentListItemIsEmpty())
                        {
                            ExitList();  // Empty item = exit list
                        }
                        else
                        {
                            CreateNewListItem();  // Continue list
                        }
                        e.Handled = true;
                    }
                    break;
                    
                case Key.Tab:
                    // HYBRID APPROACH: Use reliable WPF commands, no arbitrary limits
                    if (IsInList())
                    {
                        try
                        {
                            if (Keyboard.Modifiers == ModifierKeys.Shift)
                            {
                                EditingCommands.DecreaseIndentation.Execute(null, this);
                                System.Diagnostics.Debug.WriteLine("[EDITOR] Outdented using WPF command (reliable)");
                            }
                            else
                            {
                                EditingCommands.IncreaseIndentation.Execute(null, this);
                                System.Diagnostics.Debug.WriteLine("[EDITOR] Indented using WPF command (reliable)");
                            }
                            e.Handled = true;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ERROR] Tab operation failed: {ex.Message}");
                            // Don't handle the event, let WPF try default behavior
                        }
                    }
                    break;
                    
                case Key.Back:
                    // PHASE 2: Predictable backspace behavior
                    if (IsInList() && IsAtStartOfListItem())
                    {
                        RemoveListFormattingFromCurrentItem();
                        e.Handled = true;
                    }
                    break;
                    
                case Key.Delete:
                    // Enhanced delete behavior for lists
                    if (IsInList() && IsAtEndOfListItem())
                    {
                        if (TryMergeWithNextListItem())
                        {
                            e.Handled = true;
                        }
                    }
                    break;
            }
        }

        private void OnPreviewKeyUp(object sender, KeyEventArgs e)
        {
            // No longer tracking double-enter behavior
        }
        
        /// <summary>
        /// PHASE 1.5: Clean paste handler - strips all external formatting
        /// </summary>
        private void OnPreviewPaste(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command == ApplicationCommands.Paste)
            {
                PerformCleanPaste();
                e.Handled = true;
            }
        }
        
        /// <summary>
        /// PHASE 2: Enhanced clean paste - strips ALL external formatting
        /// Handles text from Word, web pages, other rich editors
        /// </summary>
        private void PerformCleanPaste()
        {
            try
            {
                string plainText = null;
                
                // PHASE 2: Try multiple clipboard formats to ensure we get clean text
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    plainText = Clipboard.GetText(TextDataFormat.UnicodeText);
                }
                else if (Clipboard.ContainsText(TextDataFormat.Text))
                {
                    plainText = Clipboard.GetText(TextDataFormat.Text);
                }
                else if (Clipboard.ContainsText())
                {
                    plainText = Clipboard.GetText(); // Default format
                }
                
                if (!string.IsNullOrEmpty(plainText))
                {
                    // PHASE 2: Clean the text thoroughly
                    plainText = CleanPastedText(plainText);
                    
                    // Insert plain text at current selection
                    if (Selection != null && !Selection.IsEmpty)
                    {
                        Selection.Text = plainText;
                    }
                    else
                    {
                        // Insert at caret position
                        var currentPos = CaretPosition;
                        if (currentPos != null)
                        {
                            currentPos.InsertTextInRun(plainText);
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[EDITOR] Clean paste: {plainText.Length} chars as clean plain text");
                }
            }
            catch (Exception ex)
            {
                // Graceful fallback to default paste behavior
                System.Diagnostics.Debug.WriteLine($"[ERROR] Clean paste failed, using default: {ex.Message}");
                try
                {
                    ApplicationCommands.Paste.Execute(null, this);
                }
                catch
                {
                    // Last resort - do nothing rather than crash
                    System.Diagnostics.Debug.WriteLine("[ERROR] Even default paste failed");
                }
            }
        }
        
        /// <summary>
        /// PHASE 2: Clean pasted text - remove problematic characters and normalize
        /// </summary>
        private string CleanPastedText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            try
            {
                // Remove common problematic characters from rich text sources
                text = text.Replace("\r\n", "\n")      // Normalize line endings
                          .Replace("\r", "\n")         // Handle Mac line endings
                          .Replace("\u00A0", " ")      // Non-breaking space -> regular space
                          .Replace("\u2028", "\n")     // Line separator -> newline
                          .Replace("\u2029", "\n\n");  // Paragraph separator -> double newline
                
                // Remove zero-width characters that can cause issues
                text = text.Replace("\u200B", "")      // Zero-width space
                          .Replace("\u200C", "")       // Zero-width non-joiner
                          .Replace("\u200D", "")       // Zero-width joiner
                          .Replace("\uFEFF", "");      // Byte order mark
                
                // Normalize multiple consecutive spaces (common in HTML/Word)
                while (text.Contains("  "))
                {
                    text = text.Replace("  ", " ");
                }
                
                // Clean up excessive newlines (more than 2 consecutive)
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
                
                return text;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Text cleaning failed: {ex.Message}");
                return text; // Return original if cleaning fails
            }
        }

        // SIMPLIFIED: Replace HandleBackspaceKey
        private void HandleBackspaceKey(KeyEventArgs e)
            {
                var para = CaretPosition?.Paragraph;
            if (para?.Parent is not ListItem li || li.Parent is not List list) 
                return;
            
            // Check if at start using pointer comparison (more reliable)
            if (para.ContentStart.CompareTo(CaretPosition) != 0)
                return;
            
                    e.Handled = true;
            
            // Save state for restoration
            var savedIndex = GetCaretCharacterIndex();
            
            BeginChange();
            try
            {
                var text = new TextRange(para.ContentStart, para.ContentEnd).Text;
                
                if (string.IsNullOrWhiteSpace(text))
                {
                    // Empty item - remove it
                    RemoveEmptyListItem(list, li);
                    }
                    else
                    {
                    // Non-empty - convert to paragraph
                    RemoveListFormattingInternal(para);
                }
                
                // Defer caret positioning until after layout
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    // Position at start of the new paragraph
                    var newPara = CaretPosition?.Paragraph;
                    if (newPara != null)
                    {
                        CaretPosition = newPara.ContentStart;
                    }
                }));
            }
            finally
            {
                EndChange();
            }
        }

        private bool TryContinueList(Paragraph currentPara)
        {
            if (currentPara.Parent is not ListItem li || li.Parent is not List list) 
                return false;

                // Removed: _isUpdating flag
                try
                {
                var text = new TextRange(currentPara.ContentStart, currentPara.ContentEnd).Text;
                
                if (string.IsNullOrWhiteSpace(text))
                {
                    // Single Enter on empty item - exit list (standard behavior)
                    ConvertListItemToParagraph(currentPara, mergeWithPreviousIfParagraph: false);
                    return true;
                }

                // Non-empty item - create new item normally
                
                // Split content if caret is mid-paragraph
                var caret = CaretPosition ?? currentPara.ContentEnd;
                var newPara = new Paragraph { Margin = new Thickness(0) };
                    var newLi = new ListItem();
                    newLi.Blocks.Add(newPara);

                // Handle content after caret
                    if (caret.CompareTo(currentPara.ContentEnd) < 0)
                    {
                        var trailing = new TextRange(caret, currentPara.ContentEnd);
                    
                    // Save content BEFORE clearing
                    byte[] savedContent = null;
                    try
                    {
                        using (var stream = new MemoryStream())
                        {
                            trailing.Save(stream, DataFormats.Xaml);
                            savedContent = stream.ToArray();
                        }
                    }
                    catch { }
                    
                    // Only clear if we successfully saved
                    if (savedContent != null && savedContent.Length > 0)
                    {
                        trailing.Text = string.Empty;
                        
                        // Restore in new paragraph
                        try
                        {
                            using (var stream = new MemoryStream(savedContent))
                            {
                                    var dest = new TextRange(newPara.ContentStart, newPara.ContentEnd);
                                dest.Load(stream, DataFormats.Xaml);
                                }
                                }
                        catch { }
                        }
                    }

                // Insert the new list item
                    int index = GetListItemIndex(list, li);
                    InsertListItemAt(list, index + 1, newLi);
                    
                    // Apply hanging indent to new item
                    ListFormatting.ApplyHangingIndentToListItem(newLi);
                
                // Position caret at start of new item
                    CaretPosition = newPara.ContentStart;
                    
                    // Removed: ScheduleRenumbering (part of deleted numbering system)
                    
                return true;
                }
                finally
                {
                    // Removed: _isUpdating flag
                }
        }

        private bool IsEmptyListItem(Paragraph para)
        {
            if (para.Parent is ListItem li)
            {
                var text = new TextRange(para.ContentStart, para.ContentEnd).Text;
                return string.IsNullOrWhiteSpace(text);
            }
            return false;
        }

        private void RemoveCurrentEmptyListItem(Paragraph para)
        {
            if (para?.Parent is not ListItem li || li.Parent is not List list) 
                return;

                // Removed: _isUpdating flag
                try
                {
                // SAFETY CHECK: Only remove if this is a single-paragraph list item
                int blockCount = 0;
                foreach (var block in li.Blocks)
                {
                    blockCount++;
                    if (blockCount > 1)
                    {
                        // Multiple blocks - only remove the empty paragraph, not the whole item
                        li.Blocks.Remove(para);
                        
                        // Position caret at the next block
                        if (li.Blocks.FirstBlock is Block nextBlock)
                        {
                            if (nextBlock is Paragraph nextPara)
                                CaretPosition = nextPara.ContentStart;
                            else
                                CaretPosition = Document.ContentEnd;
                        }
                        return;
                    }
                }
                
                // Single block - safe to remove entire list item
                    int index = GetListItemIndex(list, li);
                var parentBlocks = GetParentBlockCollection(list);
                
                    list.ListItems.Remove(li);
                
                // Position caret appropriately
                if (GetListItemCount(list) > 0)
                {
                    if (index > 0)
                    {
                        var prevItem = GetListItemAt(list, index - 1);
                        
                                        // Check for nested list in previous item
                if (prevItem?.Blocks.LastBlock is List nestedList && 
                    GetListItemCount(nestedList) > 0)
                {
                    var lastNestedItem = GetListItemAt(nestedList, GetListItemCount(nestedList) - 1);
                    if (lastNestedItem?.Blocks.FirstBlock is Paragraph lastPara)
                    {
                        // Position at actual end of text
                        PositionCaretAtEndOfParagraph(lastPara);
                        return;
                    }
                }
                
                // Position at end of previous item's first paragraph
                if (prevItem?.Blocks.FirstBlock is Paragraph prevPara)
                {
                    PositionCaretAtEndOfParagraph(prevPara);
                    return;
                }
                
                // Fallback: Check any paragraph in previous item
                foreach (var block in prevItem?.Blocks ?? Enumerable.Empty<Block>())
                {
                    if (block is Paragraph p)
                    {
                        PositionCaretAtEndOfParagraph(p);
                        return;
                    }
                }
                    }
                    else
                    {
                        // First item was removed, position at start of next item (now at index 0)
                        var nextItem = GetListItemAt(list, 0);
                        if (nextItem?.Blocks.FirstBlock is Paragraph nextPara)
                        {
                            CaretPosition = nextPara.ContentStart;
                            return;
                        }
                    }
                }
                else
                {
                    // List is now empty
                    if (list.Parent is ListItem parentItem)
                    {
                        // Nested list - remove it without deleting parent
                        parentItem.Blocks.Remove(list);
                        
                        // Position at end of parent item's content
                        if (parentItem.Blocks.FirstBlock is Paragraph parentPara)
                        {
                            PositionCaretAtEndOfParagraph(parentPara);
                        }
                    }
                    else if (parentBlocks != null)
                    {
                        // Root list - replace with paragraph
                        var newPara = new Paragraph();
                        parentBlocks.InsertAfter(list, newPara);
                        parentBlocks.Remove(list);
                        CaretPosition = newPara.ContentStart;
                    }
                    }
                }
                finally
                {
                    // Removed: _isUpdating flag
                    // Debouncing removed - content updates immediately
            }
        }

        private void RemoveListItem(List list, ListItem item)
        {
            RemoveListItemSimple(list, item);
            
            // Trigger renumbering if it's a numbered list
            if (list?.MarkerStyle == TextMarkerStyle.Decimal)
            {
                // Removed: ScheduleRenumbering (part of deleted numbering system)
            }
        }

        // Simplified and more predictable list item removal
        private void RemoveListItemSimple(List list, ListItem item)
        {
            if (list == null || item == null) return;
            
            try
            {
                int index = GetListItemIndex(list, item);
                list.ListItems.Remove(item);
                
                // Position caret predictably
                if (GetListItemCount(list) > 0)
                {
                    if (index > 0)
                    {
                        // Go to end of previous item at same level (ignore nested lists)
                        var prevItem = GetListItemAt(list, index - 1);
                        if (prevItem?.Blocks.FirstBlock is Paragraph prevPara)
                        {
                            PositionCaretAtEndOfParagraphReliable(prevPara);
                            return;
                        }
                    }
                    else
                    {
                        // Was first item, go to start of new first item
                        var nextItem = GetListItemAt(list, 0);
                        if (nextItem?.Blocks.FirstBlock is Paragraph nextPara)
                        {
                            CaretPosition = nextPara.ContentStart;
                            return;
                        }
                    }
                }
                else
                {
                    // List is now empty - remove it and create replacement paragraph
                    var parentBlocks = GetParentBlockCollection(list);
                    if (parentBlocks != null)
                    {
                        var newPara = new Paragraph();
                        
                        // Insert before removing to maintain document structure
                        try
                        {
                            parentBlocks.InsertAfter(list, newPara);
                            parentBlocks.Remove(list);
                            CaretPosition = newPara.ContentStart;
                        }
                        catch
                        {
                            // Fallback: just position at document end
                            CaretPosition = Document.ContentEnd;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] RemoveListItemSimple failed: {ex.Message}");
            }
        }

        // More reliable end-of-paragraph positioning
        private void PositionCaretAtEndOfParagraphReliable(Paragraph para)
        {
            if (para == null) return;
            
            try
            {
                var range = new TextRange(para.ContentStart, para.ContentEnd);
                var text = range.Text ?? string.Empty;
                
                if (string.IsNullOrEmpty(text))
                {
                    // Empty paragraph
                    CaretPosition = para.ContentStart;
                    return;
                }
                
                // Find actual end position (not including paragraph markers)
                var position = para.ContentStart;
                var lastGoodPosition = position;
                
                while (position != null)
                {
                    var next = position.GetNextInsertionPosition(LogicalDirection.Forward);
                    if (next == null) break;
                    
                    var testRange = new TextRange(para.ContentStart, next);
                    if (testRange.Text.Length <= text.TrimEnd().Length)
                    {
                        lastGoodPosition = next;
                        position = next;
                    }
                    else
                    {
                        break;
                    }
                }
                
                CaretPosition = lastGoodPosition ?? para.ContentEnd;
                
                LogListOperation("POSITION_END", $"Text length: {text.Length}, Positioned at end");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WARNING] PositionCaretAtEndOfParagraphReliable failed: {ex.Message}");
                CaretPosition = para.ContentEnd; // Safe fallback
            }
        }

        // SIMPLIFIED: Remove empty list item
        private void RemoveEmptyListItem(List list, ListItem item)
        {
            if (list == null || item == null) return;
            
            int index = GetListItemIndex(list, item);
            list.ListItems.Remove(item);
            
            if (GetListItemCount(list) > 0)
            {
                // Position at end of previous item or start of next
                ListItem targetItem = null;
                bool atEnd = true;
                
                if (index > 0)
                {
                    targetItem = GetListItemAt(list, index - 1);
                    atEnd = true;
                }
                else if (GetListItemCount(list) > 0)
                {
                    targetItem = GetListItemAt(list, 0);
                    atEnd = false;
                }
                
                if (targetItem?.Blocks.FirstBlock is Paragraph targetPara)
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                    {
                        CaretPosition = atEnd ? targetPara.ContentEnd : targetPara.ContentStart;
                    }));
                }
            }
            else
            {
                // List is empty - remove it
                var parentBlocks = GetParentBlockCollection(list);
                if (parentBlocks != null)
                {
                    var newPara = new Paragraph();
                    parentBlocks.InsertAfter(list, newPara);
                    parentBlocks.Remove(list);
                    
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                    {
                        CaretPosition = newPara.ContentStart;
                    }));
                }
            }
        }

        // SIMPLIFIED: Remove list formatting from paragraph
        private void RemoveListFormattingInternal(Paragraph para)
        {
            if (para?.Parent is not ListItem li || li.Parent is not List list) return;
            
            var parentBlocks = GetParentBlockCollection(list);
            if (parentBlocks == null) return;
            
            // Create new paragraph with content
            var newPara = new Paragraph();
            
            // Copy content
            var text = new TextRange(para.ContentStart, para.ContentEnd).Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                newPara.Inlines.Add(new Run(text));
            }
            
            // Determine position
            int index = GetListItemIndex(list, li);
            
            // Remove list item
            list.ListItems.Remove(li);
            
            // Insert new paragraph
            if (GetListItemCount(list) == 0)
            {
                // Replace empty list with paragraph
                parentBlocks.InsertAfter(list, newPara);
                parentBlocks.Remove(list);
            }
            else if (index == 0)
            {
                // Was first item
                parentBlocks.InsertBefore(list, newPara);
            }
            else
            {
                // Was last or middle - insert after list
                parentBlocks.InsertAfter(list, newPara);
            }
        }

        private bool TryChangeListIndent(Paragraph para, bool outdent)
        {
            if (para.Parent is not ListItem li) return false;
            if (li.Parent is not List parentList) return false;

            // Removed: _isUpdating flag
            try
            {
                // HYBRID APPROACH: Use reliable WPF commands instead of complex custom logic
                if (outdent)
                {
                    EditingCommands.DecreaseIndentation.Execute(null, this);
                    System.Diagnostics.Debug.WriteLine("[EDITOR] Outdented using reliable WPF command");
                }
                else
                {
                    EditingCommands.IncreaseIndentation.Execute(null, this);
                    System.Diagnostics.Debug.WriteLine("[EDITOR] Indented using reliable WPF command");
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Indent/Outdent operation failed: {ex.Message}");
                return false;
            }
        }

        private bool TryIndentListItem(ListItem item, List parentList)
        {
            // HYBRID APPROACH: Removed complex nesting level checking
            // Now relying on WPF's natural behavior without artificial limits
            
            // Save current caret position relative to the paragraph
            int caretOffset = 0;
            if (item.Blocks.FirstBlock is Paragraph itemPara && CaretPosition != null)
            {
                var range = new TextRange(itemPara.ContentStart, CaretPosition);
                caretOffset = range.Text.Length;
            }
            
            int index = GetListItemIndex(parentList, item);
            
            // Allow indenting ANY item, including the first
            List nestedList = null;
            ListItem containerItem = null;
            
            if (index > 0)
            {
                // Try to add to previous item's nested list
            var prevItem = GetListItemAt(parentList, index - 1);
                containerItem = prevItem;

            if (prevItem.Blocks.LastBlock is List existing)
            {
                    nestedList = existing;
                }
            }
            else
            {
                // First item - create a container for it
                containerItem = new ListItem();
                containerItem.Blocks.Add(new Paragraph()); // Placeholder
                InsertListItemAt(parentList, 0, containerItem);
                index = 1; // Adjust since we inserted
            }
            
            if (nestedList == null)
            {
                // PHASE 2: Smart marker style for nested lists per original plan
                var nestedMarkerStyle = parentList.MarkerStyle;
                
                // Convert numbered lists to bullets when nested
                if (parentList.MarkerStyle == TextMarkerStyle.Decimal)
                {
                    nestedMarkerStyle = TextMarkerStyle.Disc;
                    System.Diagnostics.Debug.WriteLine($"[EDITOR] Creating nested list: numbered -> bullets (per original plan)");
                }
                
                // Create new nested list
                nestedList = new List 
                { 
                    MarkerStyle = nestedMarkerStyle, 
                    Margin = new Thickness(0, 0, 0, 0)
                };
                containerItem.Blocks.Add(nestedList);
            }
            
            // Move item to nested list
            parentList.ListItems.Remove(item);
            nestedList.ListItems.Add(item);
            
            // Apply hanging indent to the moved item
            ListFormatting.ApplyHangingIndentToListItem(item);
            
            // Restore caret position
            if (item.Blocks.FirstBlock is Paragraph p)
            {
                var newPosition = p.ContentStart;
                for (int i = 0; i < caretOffset && newPosition != null; i++)
                {
                    newPosition = newPosition.GetNextInsertionPosition(LogicalDirection.Forward);
                }
                if (newPosition != null)
                    CaretPosition = newPosition;
                else
                    CaretPosition = p.ContentStart;
            }
            
            // Trigger renumbering if it's a numbered list
            if (parentList?.MarkerStyle == TextMarkerStyle.Decimal)
            {
                // Removed: ScheduleRenumbering (part of deleted numbering system)
            }
            
            return true;
        }

        // SIMPLIFIED: Replace TryOutdentListItem
        private bool TryOutdentListItem(ListItem item, List list)
        {
            if (item == null || list == null) return false;
            
            // Save position using character index (more stable)
            var savedIndex = GetCaretCharacterIndex();
            
            BeginChange();
            try
            {
                // For nested lists, move to parent
                if (list.Parent is ListItem parentItem && 
                    parentItem.Parent is List grandParentList)
                {
                    int insertIndex = GetListItemIndex(grandParentList, parentItem) + 1;
                    list.ListItems.Remove(item);
                    InsertListItemAt(grandParentList, insertIndex, item);
                    
                    if (GetListItemCount(list) == 0)
                    {
                        parentItem.Blocks.Remove(list);
                        if (parentItem.Blocks.Count == 0)
                        {
                            grandParentList.ListItems.Remove(parentItem);
                        }
                    }
                    
                    // Defer positioning
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                    {
                        SetCaretAtCharacterIndex(savedIndex);
                    }));
                    
                    // Trigger renumbering if it's a numbered list
                    if (grandParentList?.MarkerStyle == TextMarkerStyle.Decimal)
                    {
                        // Removed: ScheduleRenumbering (part of deleted numbering system)
                    }
                    
                    return true;
                }
                
                // At root level - use command
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    RemoveListFormattingCommand.Execute(null, this);
                }));
                
                return true;
            }
            finally
            {
                EndChange();
            }
        }


        // Add validation helper
        private bool ValidateListStructure(List list)
        {
            if (list == null) return false;
            
            try
            {
                int count = 0;
                foreach (var item in list.ListItems)
                {
                    count++;
                    if (item.Blocks.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("[WARNING] Found list item with no blocks");
                        return false;
                    }
                }
                return count > 0;
            }
            catch
            {
                return false;
            }
        }

        // NEW: Character-based position tracking
        private int GetCaretCharacterIndex()
        {
            try
            {
                var start = Document.ContentStart;
                var caret = CaretPosition ?? start;
                var range = new TextRange(start, caret);
                return range.Text.Length;
            }
            catch
            {
                return 0;
            }
        }

        private void SetCaretAtCharacterIndex(int index)
        {
            try
            {
                var navigator = Document.ContentStart;
                int currentIndex = 0;
                
                while (navigator != null && currentIndex < index)
                {
                    var next = navigator.GetNextContextPosition(LogicalDirection.Forward);
                    if (next == null) break;
                    
                    var text = new TextRange(navigator, next).Text;
                    if (currentIndex + text.Length <= index)
                    {
                        navigator = next;
                        currentIndex += text.Length;
                    }
                    else
                    {
                        // We need to go character by character
                        for (int i = 0; i < (index - currentIndex); i++)
                        {
                            var charNext = navigator.GetNextInsertionPosition(LogicalDirection.Forward);
                            if (charNext != null) navigator = charNext;
                        }
                        break;
                    }
                }
                
                CaretPosition = navigator ?? Document.ContentEnd;
            }
            catch
            {
                CaretPosition = Document.ContentEnd;
            }
        }

        private string GetFullDocumentText()
        {
            return new TextRange(Document.ContentStart, Document.ContentEnd).Text;
        }

        // Helpers for ListItemCollection (no indexers provided)
        private static int GetListItemIndex(List list, ListItem item)
        {
            int i = 0;
            foreach (ListItem li in list.ListItems)
            {
                if (ReferenceEquals(li, item)) return i;
                i++;
            }
            return -1;
        }

        private static int GetListItemCount(List list)
        {
            int count = 0;
            foreach (var _ in list.ListItems) count++;
            return count;
        }

        private static ListItem GetListItemAt(List list, int index)
        {
            int i = 0;
            foreach (ListItem li in list.ListItems)
            {
                if (i == index) return li;
                i++;
            }
            return null;
        }

        private static void InsertListItemAt(List list, int index, ListItem item)
        {
            if (index <= 0)
            {
                list.ListItems.Add(item);
                return;
            }
            int i = 0;
            ListItem before = null;
            foreach (ListItem li in list.ListItems)
            {
                if (i == index)
                {
                    before = li;
                    break;
                }
                i++;
            }
            if (before != null)
            {
                list.ListItems.InsertBefore(before, item);
            }
            else
            {
                list.ListItems.Add(item);
            }
        }

        private static void RemoveListItemAt(List list, int index)
        {
            int i = 0;
            ListItem target = null;
            foreach (ListItem li in list.ListItems)
            {
                if (i == index) { target = li; break; }
                i++;
            }
            if (target != null) list.ListItems.Remove(target);
        }

        private static void AddListItem(List list, ListItem item)
        {
            list.ListItems.Add(item);
        }

        private bool IsAtListItemStart()
        {
            var para = CaretPosition?.Paragraph;
            if (para?.Parent is not ListItem) return false;
            
            try
            {
            // Check if we have any actual content before the caret
            var range = new TextRange(para.ContentStart, CaretPosition);
            var textBeforeCaret = range.Text;
            
            // We're at start only if there's NO text before caret
            return string.IsNullOrEmpty(textBeforeCaret);
            }
            catch
            {
                // If we can't determine position, assume we're not at start
                return false;
            }
        }

        // New reliable position detection
        private bool IsAtListItemStartReliable()
        {
            try
            {
                var para = CaretPosition?.Paragraph;
                if (para?.Parent is not ListItem) return false;
                
                // Method 1: Direct position comparison
                if (para.ContentStart.CompareTo(CaretPosition) == 0)
                    return true;
                
                // Method 2: Check if only whitespace/formatting before caret
                var range = new TextRange(para.ContentStart, CaretPosition);
                var textBefore = range.Text;
                
                // No text at all before caret
                if (string.IsNullOrEmpty(textBefore))
                    return true;
                
                // Only whitespace before caret (handles formatting markers)
                if (string.IsNullOrWhiteSpace(textBefore) && textBefore.Length <= 2)
                    return true;
                
                // Method 3: Try getting positions and counting
                var start = para.ContentStart;
                var current = CaretPosition;
                int steps = 0;
                
                while (start != null && start.CompareTo(current) < 0 && steps < 3)
                {
                    start = start.GetNextInsertionPosition(LogicalDirection.Forward);
                    steps++;
                }
                
                // We're very close to the start (within 2 positions)
                return steps <= 2 && string.IsNullOrWhiteSpace(textBefore);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WARNING] IsAtListItemStartReliable failed: {ex.Message}");
                return false; // Safe default
            }
        }

        private bool IsAtListItemEnd()
        {
            var para = CaretPosition?.Paragraph;
            if (para?.Parent is not ListItem) return false;
            
            // Check if we have any actual content after the caret
            var range = new TextRange(CaretPosition, para.ContentEnd);
            var textAfterCaret = range.Text;
            
            // We're at end only if there's NO text after caret (ignoring trailing whitespace)
            return string.IsNullOrWhiteSpace(textAfterCaret);
        }

        private void ConvertListItemToParagraph(Paragraph para, bool mergeWithPreviousIfParagraph)
        {
            if (para?.Parent is not ListItem li || li.Parent is not List list) return;

            // Removed: _isUpdating flag
            try
            {
                var parentBlocks = GetParentBlockCollection(list);
                if (parentBlocks == null) return;

                int index = GetListItemIndex(list, li);
                
                // Only merge if we're at the first item AND there's a previous paragraph
                if (mergeWithPreviousIfParagraph && index == 0)
                {
                    Block prevBlock = null;
                    try { prevBlock = list.PreviousBlock; } catch { }
                    
                    if (prevBlock is Paragraph prevPara)
                    {
                        // Get the content that will be merged
                        var contentToMerge = new TextRange(para.ContentStart, para.ContentEnd).Text;
                        
                        // Only merge if there's actual content to merge
                        if (!string.IsNullOrWhiteSpace(contentToMerge))
                        {
                            // Remember position for caret
                            var prevText = new TextRange(prevPara.ContentStart, prevPara.ContentEnd).Text;
                            var mergePosition = prevText.Length;
                            
                            // Merge the content
                            MergeParagraphInto(para, prevPara);
                            
                            // Remove the list item
                            list.ListItems.Remove(li);
                            
                            // Remove empty list
                            if (GetListItemCount(list) == 0)
                            {
                                parentBlocks.Remove(list);
                            }
                            
                            // Position caret at merge point
                            try
                            {
                                var newPos = prevPara.ContentStart.GetPositionAtOffset(mergePosition);
                                if (newPos != null) CaretPosition = newPos;
                            }
                            catch
                            {
                                CaretPosition = prevPara.ContentEnd;
                            }
                            return;
                        }
                        else
                        {
                            // Empty content - just remove
                            list.ListItems.Remove(li);
                            if (GetListItemCount(list) == 0)
                            {
                                parentBlocks.Remove(list);
                            }
                            CaretPosition = prevPara.ContentEnd;
                            return;
                        }
                    }
                }

                // No merge - create standalone paragraph
                var newPara = new Paragraph();
                
                // Copy content if any
                var text = new TextRange(para.ContentStart, para.ContentEnd).Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    CopyParagraphContent(para, newPara);
                }

                // Insert the paragraph at the appropriate position
                if (index == 0)
                {
                    parentBlocks.InsertBefore(list, newPara);
                }
                else if (index >= GetListItemCount(list) - 1)
                {
                    parentBlocks.InsertAfter(list, newPara);
                }
                else
                {
                    // Middle item - split the list
                    SplitListAtItem(list, li, newPara, parentBlocks);
                    CaretPosition = newPara.ContentStart;
                    return;
                }

                // Remove the list item
                list.ListItems.Remove(li);
                
                // Clean up empty list
                if (GetListItemCount(list) == 0)
                {
                    parentBlocks.Remove(list);
                }
                
                // Position caret appropriately
                CaretPosition = newPara.ContentStart;
            }
            finally
            {
                // Removed: _isUpdating flag
                // Debouncing removed - content updates immediately
            }
        }

        private void MergeParagraphInto(Paragraph from, Paragraph to)
        {
            if (from == null || to == null) return;
            
            try
            {
                var fromText = new TextRange(from.ContentStart, from.ContentEnd).Text;
                
                // Only merge if there's actual content
                if (string.IsNullOrWhiteSpace(fromText)) return;
                
                // Add space between if needed
                var toText = new TextRange(to.ContentStart, to.ContentEnd).Text;
                if (!string.IsNullOrWhiteSpace(toText) && !toText.EndsWith(" "))
                {
                    to.ContentEnd.InsertTextInRun(" ");
                }
                
                // Transfer content with formatting
                using (var stream = new MemoryStream())
                {
                    var fromRange = new TextRange(from.ContentStart, from.ContentEnd);
                    fromRange.Save(stream, DataFormats.Xaml);
                    stream.Position = 0;
                    
                    var insertRange = new TextRange(to.ContentEnd, to.ContentEnd);
                    insertRange.Load(stream, DataFormats.Xaml);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WARNING] Merge failed: {ex.Message}");
                // Fallback to plain text merge
                try
                {
                    var text = new TextRange(from.ContentStart, from.ContentEnd).Text;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        to.ContentEnd.InsertTextInRun(" " + text);
                    }
                }
                catch { }
            }
        }

        private void SplitListAtItem(List originalList, ListItem itemToRemove, Paragraph newPara, BlockCollection parentBlocks)
        {
            // Create a new list for items after the removed one
            var newList = new List 
            { 
                MarkerStyle = originalList.MarkerStyle,
                Margin = originalList.Margin,
                Padding = originalList.Padding
            };
            
            // Collect items to move
            int splitIndex = GetListItemIndex(originalList, itemToRemove);
            var itemsToMove = new List<ListItem>();
            
            int currentIndex = 0;
            foreach (ListItem item in originalList.ListItems)
            {
                if (currentIndex > splitIndex)
                {
                    itemsToMove.Add(item);
                }
                currentIndex++;
            }
            
            // Move items to new list
            foreach (var item in itemsToMove)
            {
                originalList.ListItems.Remove(item);
                newList.ListItems.Add(item);
            }
            
            // Remove the item being converted
            originalList.ListItems.Remove(itemToRemove);
            
            // Insert new paragraph and new list
            parentBlocks.InsertAfter(originalList, newPara);
            if (newList.ListItems.Count > 0)
            {
                parentBlocks.InsertAfter(newPara, newList);
            }
            
            // Remove original list if empty
            if (GetListItemCount(originalList) == 0)
            {
                parentBlocks.Remove(originalList);
            }
        }

        private void CopyParagraphContent(Paragraph source, Paragraph target)
        {
            if (source == null || target == null) return;
            
            try
            {
                var sourceText = new TextRange(source.ContentStart, source.ContentEnd).Text;
                
                // Only copy if there's actual content
                if (!string.IsNullOrWhiteSpace(sourceText))
                {
                    // Clear target first
                    target.Inlines.Clear();
                    
                    // Try to preserve formatting
                    using (var stream = new MemoryStream())
                    {
                        var sourceRange = new TextRange(source.ContentStart, source.ContentEnd);
                        sourceRange.Save(stream, DataFormats.Xaml);
                        stream.Position = 0;
                        
                        var targetRange = new TextRange(target.ContentStart, target.ContentEnd);
                        targetRange.Load(stream, DataFormats.Xaml);
                    }
                }
            }
            catch (Exception ex)
            {
                // Fallback: plain text copy
                System.Diagnostics.Debug.WriteLine($"[WARNING] Format copy failed: {ex.Message}");
                try
                {
                    var text = new TextRange(source.ContentStart, source.ContentEnd).Text;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        target.Inlines.Clear();
                        target.Inlines.Add(new Run(text));
                    }
                }
                catch { }
            }
        }

        private BlockCollection GetParentBlockCollection(Block block)
        {
            if (block?.Parent is FlowDocument doc) return doc.Blocks;
            if (block?.Parent is Section section) return section.Blocks;
            if (block?.Parent is ListItem listItem) return listItem.Blocks;
            if (block?.Parent is TableCell cell) return cell.Blocks;
            if (block?.Parent is BlockUIContainer container) return null; // Can't have block children
            return null;
        }

        // New method for converting with caret preservation
        private void ConvertListItemToParagraphWithCaretPreservation(
            Paragraph para, 
            bool mergeWithPreviousIfParagraph, 
            int caretOffset,
            string expectedTextBeforeCaret)
        {
            if (para?.Parent is not ListItem li || li.Parent is not List list) return;

            // Removed: _isUpdating flag
            try
            {
                var parentBlocks = GetParentBlockCollection(list);
                if (parentBlocks == null) 
                {
                    System.Diagnostics.Debug.WriteLine("[ERROR] Parent block collection is null");
                    return;
                }

                int index = GetListItemIndex(list, li);
                
                // Create new paragraph and copy content
                var newPara = new Paragraph();
                
                // Preserve text content with formatting
                string fullText = string.Empty;
                try
                {
                    var sourceRange = new TextRange(para.ContentStart, para.ContentEnd);
                    fullText = sourceRange.Text ?? string.Empty;
                    
                    if (!string.IsNullOrWhiteSpace(fullText))
                    {
                        // Try to preserve formatting
                        using (var stream = new MemoryStream())
                        {
                            sourceRange.Save(stream, DataFormats.Xaml);
                            stream.Position = 0;
                            var targetRange = new TextRange(newPara.ContentStart, newPara.ContentEnd);
                            targetRange.Load(stream, DataFormats.Xaml);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Fallback to plain text
                    System.Diagnostics.Debug.WriteLine($"[WARNING] Format preservation failed: {ex.Message}");
                    if (!string.IsNullOrWhiteSpace(fullText))
                    {
                        newPara.Inlines.Add(new Run(fullText));
                    }
                }

                // Determine insertion position
                if (index == 0)
                {
                    parentBlocks.InsertBefore(list, newPara);
                }
                else if (index >= GetListItemCount(list) - 1)
                {
                    parentBlocks.InsertAfter(list, newPara);
                }
                else
                {
                    // Middle item - need to split list
                    SplitListAtItem(list, li, newPara, parentBlocks);
                    RestoreCaretPosition(newPara, caretOffset, expectedTextBeforeCaret);
                    return;
                }

                // Remove the list item
                list.ListItems.Remove(li);
                
                // Clean up empty list
                if (GetListItemCount(list) == 0)
                {
                    parentBlocks.Remove(list);
                }
                
                // CRITICAL: Restore caret to exact position
                RestoreCaretPosition(newPara, caretOffset, expectedTextBeforeCaret);
            }
            finally
            {
                // Removed: _isUpdating flag
                // Debouncing removed - content updates immediately
            }
        }

        // New robust caret restoration method
        private void RestoreCaretPosition(Paragraph para, int offset, string expectedTextBefore)
        {
            if (para == null) 
            {
                System.Diagnostics.Debug.WriteLine("[WARNING] Cannot restore caret - paragraph is null");
                return;
            }
            
            try
            {
                // First, ensure the paragraph is loaded
                para.Loaded += (s, e) => DoRestoreCaretPosition(para, offset, expectedTextBefore);
                
                // Try immediate restoration
                DoRestoreCaretPosition(para, offset, expectedTextBefore);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to restore caret position: {ex.Message}");
                CaretPosition = para.ContentStart;
            }
        }

        private void DoRestoreCaretPosition(Paragraph para, int offset, string expectedTextBefore)
        {
            try
            {
                if (offset <= 0)
                {
                    CaretPosition = para.ContentStart;
                    return;
                }
                
                // Navigate to the target position
                var position = para.ContentStart;
                int currentOffset = 0;
                
                while (position != null && currentOffset < offset)
                {
                    var next = position.GetNextInsertionPosition(LogicalDirection.Forward);
                    if (next == null) break;
                    
                    var range = new TextRange(position, next);
                    var charCount = range.Text.Length;
                    
                    if (currentOffset + charCount <= offset)
                    {
                        position = next;
                        currentOffset += charCount;
                    }
                    else
                    {
                        // We've gone too far, stay at current position
                        break;
                    }
                }
                
                if (position != null)
                {
                    // Verify the position is correct
                    var verification = new TextRange(para.ContentStart, position);
                    var actualText = verification.Text ?? string.Empty;
                    
                    // Only set if we're reasonably close to expected position
                    if (Math.Abs(actualText.Length - offset) <= 2) // Allow 2 char tolerance
                    {
                        CaretPosition = position;
                        LogListOperation("CARET_RESTORED", $"Offset: {offset}, Actual: {actualText.Length}");
                    }
                    else
                    {
                        // Fallback to approximate position
                        var fallbackPos = para.ContentStart;
                        for (int i = 0; i < offset && fallbackPos != null; i++)
                        {
                            var next = fallbackPos.GetNextInsertionPosition(LogicalDirection.Forward);
                            if (next != null) fallbackPos = next;
                        }
                        CaretPosition = fallbackPos ?? para.ContentStart;
                        LogListOperation("CARET_FALLBACK", $"Target: {offset}, Used fallback");
                    }
                }
                else
                {
                    CaretPosition = para.ContentEnd;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] DoRestoreCaretPosition failed: {ex.Message}");
                CaretPosition = para.ContentStart;
            }
        }

        private TextPointer GetRealEndPosition(Paragraph para)
        {
            if (para == null) return null;
            
            // Get the actual text content
            var text = new TextRange(para.ContentStart, para.ContentEnd).Text;
            
            if (string.IsNullOrEmpty(text))
            {
                return para.ContentStart; // Empty paragraph
            }
            
            // Try to position just before any trailing paragraph markers
            // The -1 offset moves back from the absolute end which often includes formatting markers
            var endPos = para.ContentEnd.GetPositionAtOffset(-1, LogicalDirection.Backward);
            
            if (endPos != null)
            {
                // Verify this position is actually after the text content
                var testRange = new TextRange(para.ContentStart, endPos);
                var testText = testRange.Text;
                
                // Check if we're at or after the actual text content
                if (testText.Length >= text.TrimEnd().Length)
                {
                    return endPos;
                }
            }
            
            // Fallback to ContentEnd
            return para.ContentEnd;
        }

        private void PositionCaretAtEndOfParagraph(Paragraph para)
        {
            if (para == null) return;
            
            var text = new TextRange(para.ContentStart, para.ContentEnd).Text;
            
            if (string.IsNullOrEmpty(text))
            {
                CaretPosition = para.ContentStart;
                return;
            }
            
            // Position just before the paragraph break marker
            var endPos = para.ContentEnd.GetPositionAtOffset(-1, LogicalDirection.Backward);
            if (endPos != null)
            {
                // Verify this is actually after the text
                var testRange = new TextRange(para.ContentStart, endPos);
                if (testRange.Text.Length >= text.TrimEnd().Length)
                {
                    CaretPosition = endPos;
                    return;
                }
            }
            
            // Fallback
            CaretPosition = para.ContentEnd;
        }

        private bool TryDeleteMergeAtEnd(Paragraph para)
        {
            if (para?.Parent is not ListItem li || li.Parent is not List list) 
                return false;

            int index = GetListItemIndex(list, li);
            int count = GetListItemCount(list);
            
            // Try to merge with next list item
            if (index < count - 1)
            {
                var nextLi = GetListItemAt(list, index + 1);
                if (nextLi?.Blocks.FirstBlock is Paragraph nextPara)
                {
                    // Merge next paragraph content into current
                    MergeParagraphInto(nextPara, para);
                    
                    // Move any remaining blocks from next item
                    var remainingBlocks = nextLi.Blocks.Skip(1).ToList();
                    foreach (var block in remainingBlocks)
                    {
                        nextLi.Blocks.Remove(block);
                        li.Blocks.Add(block);
                    }
                    
                    // Remove the next list item
                    list.ListItems.Remove(nextLi);
                    
                    // Keep caret at merge point
                    // (it stays where it was, at the end of para)
                    return true;
                }
            }
            
            // Try to merge with following paragraph outside list
            var parentBlocks = GetParentBlockCollection(list);
            if (parentBlocks != null && list.NextBlock is Paragraph nextParaOutside)
            {
                MergeParagraphInto(nextParaOutside, para);
                parentBlocks.Remove(nextParaOutside);
                // Caret stays at end of para
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// NEW ARCHITECTURE: Load markdown content into FlowDocument (load boundary only)
        /// </summary>
        public void LoadFromMarkdown(string markdown)
        {
            try
            {
                markdown = markdown ?? string.Empty;
                _originalMarkdown = markdown;
                _isDirty = false;

                var app = Application.Current as App;
                var config = app?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.ConfigurationService)) as NoteNest.Core.Services.ConfigurationService;
                var editorSettings = config?.Settings?.EditorSettings;
                var fontFamily = editorSettings?.FontFamily;
                double? fontSize = editorSettings?.FontSize;

                // Convert markdown to FlowDocument
                var flow = _converter.ConvertToFlowDocument(markdown, fontFamily, fontSize);
                
                // Clear and populate document
                Document.Blocks.Clear();
                
                // Ensure document has proper styles for lists
                var listStyle = new Style(typeof(List));
                listStyle.Setters.Add(new Setter(List.MarginProperty, new Thickness(0, 2, 0, 2)));
                listStyle.Setters.Add(new Setter(List.PaddingProperty, new Thickness(20, 0, 0, 0)));
                Document.Resources[typeof(List)] = listStyle;
                
                var listItemStyle = new Style(typeof(ListItem));
                listItemStyle.Setters.Add(new Setter(ListItem.MarginProperty, new Thickness(0, 1, 0, 1)));
                Document.Resources[typeof(ListItem)] = listItemStyle;
                
                // Move blocks from converter document to this document
                foreach (var block in flow.Blocks.ToList())
                {
                    flow.Blocks.Remove(block);
                    Document.Blocks.Add(block);
                }
                
                // Position caret at start
                CaretPosition = Document.ContentStart;
                
                System.Diagnostics.Debug.WriteLine($"[EDITOR] Loaded {markdown.Length} chars, IsDirty: {_isDirty}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] LoadFromMarkdown failed: {ex.Message}");
                // Create empty document on error
                Document.Blocks.Clear();
                Document.Blocks.Add(new Paragraph());
            }
        }

        /// <summary>
        /// Get quick markdown with caching for WAL protection (avoids repeated conversions)
        /// </summary>
        public string GetQuickMarkdown()
        {
            // Cache for 100ms to avoid repeated conversions during rapid operations
            if ((DateTime.Now - _lastCacheTime).TotalMilliseconds < 100)
                return _cachedMarkdown;
            
            try
            {
                _cachedMarkdown = _converter.ConvertToMarkdown(Document);
                _lastCacheTime = DateTime.Now;
                return _cachedMarkdown;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EDITOR] GetQuickMarkdown failed: {ex.Message}");
                return _originalMarkdown ?? "";
            }
        }

        /// <summary>
        /// NEW ARCHITECTURE: Convert FlowDocument to markdown (save boundary only)
        /// </summary>
        public string SaveToMarkdown()
        {
            try
            {
                var markdown = _converter.ConvertToMarkdown(Document);
                _originalMarkdown = markdown;
                _cachedMarkdown = markdown;  // Update cache
                _lastCacheTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"[EDITOR] Converted to {markdown?.Length ?? 0} chars markdown");
                return markdown ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] SaveToMarkdown failed: {ex.Message}");
                return _originalMarkdown ?? ""; // Return original as fallback
            }
        }

        /// <summary>
        /// Mark content as clean after successful save
        /// </summary>
        public void MarkClean()
        {
            _isDirty = false;
            System.Diagnostics.Debug.WriteLine($"[EDITOR] Marked clean");
        }

        /// <summary>
        /// Mark content as dirty
        /// </summary>
        public void MarkDirty()
        {
            if (!_isDirty)
            {
                _isDirty = true;
                System.Diagnostics.Debug.WriteLine($"[EDITOR] Marked dirty");
            }
        }




        public void InsertBulletList()
        {
            SmartToggleList(TextMarkerStyle.Disc);
        }

        public void InsertNumberedList()
        {
            SmartToggleList(TextMarkerStyle.Decimal);
        }

        private void WrapSelectionInList(TextMarkerStyle markerStyle)
        {
            // Removed: _isUpdating flag
            try
            {
                var startPara = Selection.Start?.Paragraph ?? CaretPosition?.Paragraph;
                var endPara = Selection.End?.Paragraph ?? startPara;
                if (startPara == null) return;

                // Collect paragraphs between start and end (inclusive)
                var paragraphs = new List<Paragraph>();
                var cursor = startPara as Block;
                while (cursor != null)
                {
                    if (cursor is Paragraph p)
                    {
                        paragraphs.Add(p);
                    }
                    if (cursor == endPara) break;
                    cursor = cursor.NextBlock;
                }
                if (paragraphs.Count == 0) return;

                var firstPara = paragraphs.First();
                var parentCollection = (firstPara.Parent as FlowDocument)?.Blocks
                    ?? (firstPara.Parent as Section)?.Blocks
                    ?? (firstPara.Parent as ListItem)?.Blocks
                    ?? Document.Blocks;

                var list = new List { MarkerStyle = markerStyle, Margin = new Thickness(0, 1, 0, 1) };
                list.SetResourceReference(List.ForegroundProperty, "SystemControlForegroundBaseHighBrush");

                // Insert the list BEFORE we detach paragraphs, so the reference is valid
                try { parentCollection.InsertBefore(firstPara, list); }
                catch { Document.Blocks.Add(list); }

                foreach (var p in paragraphs)
                {
                    // Remove from its current parent collection
                    var pParentBlocks = (p.Parent as FlowDocument)?.Blocks
                        ?? (p.Parent as Section)?.Blocks
                        ?? (p.Parent as ListItem)?.Blocks
                        ?? Document.Blocks;
                    try { pParentBlocks.Remove(p); } catch { }

                    var li = new ListItem();
                    // Normalize paragraph margin inside list items to eliminate unexpected spacing
                    try { p.Margin = new Thickness(0); } catch { }
                    li.Blocks.Add(p);
                    list.ListItems.Add(li);
                }
            }
            finally
            {
                // Removed: _isUpdating flag
                // Debouncing removed - content updates immediately
            }
        }

        private List<Paragraph> GetSelectedParagraphs()
        {
            var paragraphs = new List<Paragraph>();
            var startPara = Selection.Start?.Paragraph ?? CaretPosition?.Paragraph;
            var endPara = Selection.End?.Paragraph ?? startPara;
            
            if (startPara == null) return paragraphs;
            
            var cursor = startPara as Block;
            while (cursor != null)
            {
                if (cursor is Paragraph p)
                {
                    paragraphs.Add(p);
                }
                if (cursor == endPara) break;
                cursor = cursor.NextBlock;
            }
            return paragraphs;
        }

        private bool IsInListOfType(Paragraph para, TextMarkerStyle style)
        {
            if (para?.Parent is ListItem li && li.Parent is List list)
            {
                return list.MarkerStyle == style;
            }
            return false;
        }

        private void RemoveListFromParagraphs(List<Paragraph> paragraphs)
        {
            foreach (var para in paragraphs)
            {
                if (para?.Parent is ListItem)
                {
                    ConvertListItemToParagraph(para, mergeWithPreviousIfParagraph: false);
                }
            }
        }

        private void ChangeMarkerStyleForSelectedLists(List<Paragraph> paragraphs, TextMarkerStyle style)
        {
            var touched = new HashSet<List>();
            foreach (var para in paragraphs)
            {
                if (para?.Parent is ListItem li && li.Parent is List list && !touched.Contains(list))
                {
                    list.MarkerStyle = style;
                    touched.Add(list);
                }
            }
        }

        private void ApplyListToSelection(List<Paragraph> paragraphs, TextMarkerStyle style)
        {
            if (paragraphs.Count == 0) return;
            
            // Group consecutive paragraphs into single lists
            var groups = GroupConsecutiveParagraphs(paragraphs);
            
            foreach (var group in groups)
            {
                var list = new List 
                { 
                    MarkerStyle = style,
                    Margin = new Thickness(0, 1, 0, 1)
                };
                
                var firstPara = group.First();
                var parentBlocks = GetParentBlockCollection(firstPara);
                parentBlocks.InsertBefore(firstPara, list);
                
                foreach (var para in group)
                {
                    // Remove from current location
                    var blocks = GetParentBlockCollection(para);
                    blocks.Remove(para);
                    
                    // Add to list
                    var item = new ListItem();
                    para.Margin = new Thickness(0);
                    item.Blocks.Add(para);
                    list.ListItems.Add(item);
                    
                    // Apply hanging indent
                    ListFormatting.ApplyHangingIndentToListItem(item);
                }
            }
        }

        private List<List<Paragraph>> GroupConsecutiveParagraphs(List<Paragraph> paragraphs)
        {
            var groups = new List<List<Paragraph>>();
            var currentGroup = new List<Paragraph>();
            
            Block lastBlock = null;
            foreach (var para in paragraphs)
            {
                if (lastBlock != null && lastBlock.NextBlock != para)
                {
                    // Not consecutive, start new group
                    if (currentGroup.Count > 0)
                    {
                        groups.Add(currentGroup);
                        currentGroup = new List<Paragraph>();
                    }
                }
                
                currentGroup.Add(para);
                lastBlock = para;
            }
            
            if (currentGroup.Count > 0)
            {
                groups.Add(currentGroup);
            }
            
            return groups;
        }

        private void ApplyListToParagraphs(List<Paragraph> paragraphs, TextMarkerStyle style)
        {
            if (paragraphs == null || paragraphs.Count == 0) return;

            var firstPara = paragraphs.First();
            var parentCollection = (firstPara.Parent as FlowDocument)?.Blocks
                ?? (firstPara.Parent as Section)?.Blocks
                ?? (firstPara.Parent as ListItem)?.Blocks
                ?? Document.Blocks;

            var list = new List { MarkerStyle = style, Margin = new Thickness(0, 1, 0, 1) };
            list.SetResourceReference(List.ForegroundProperty, "SystemControlForegroundBaseHighBrush");

            try { parentCollection.InsertBefore(firstPara, list); }
            catch { Document.Blocks.Add(list); }

            foreach (var p in paragraphs)
            {
                Paragraph targetParagraph = p;
                if (p.Parent is ListItem li && li.Parent is List parentList)
                {
                    // Copy content to preserve formatting and remove original list item
                    var newPara = new Paragraph();
                    try
                    {
                        var sourceRange = new TextRange(p.ContentStart, p.ContentEnd);
                        using (var ms = new MemoryStream())
                        {
                            try
                            {
                                sourceRange.Save(ms, DataFormats.XamlPackage);
                                ms.Position = 0;
                                var dest = new TextRange(newPara.ContentStart, newPara.ContentEnd);
                                dest.Load(ms, DataFormats.XamlPackage);
                            }
                            catch
                            {
                                newPara.Inlines.Add(new Run(sourceRange.Text));
                            }
                        }
                    }
                    catch { }

                    // Remove the entire list item to avoid leaving empty bullets
                    try { parentList.ListItems.Remove(li); } catch { }
                    targetParagraph = newPara;
                }
                else
                {
                    // Detach paragraph from its current parent
                    var pParentBlocks = (p.Parent as FlowDocument)?.Blocks
                        ?? (p.Parent as Section)?.Blocks
                        ?? (p.Parent as ListItem)?.Blocks
                        ?? Document.Blocks;
                    try { pParentBlocks.Remove(p); } catch { }
                }

                try
                {
                    var newLi = new ListItem();
                    try { targetParagraph.Margin = new Thickness(0); } catch { }
                    newLi.Blocks.Add(targetParagraph);
                    list.ListItems.Add(newLi);
                }
                catch { }
            }
        }

        private void SmartToggleList(TextMarkerStyle markerStyle)
        {
            // Removed: _isUpdating flag
            try
            {
                var selection = GetSelectedParagraphs();
                
                // ENHANCED: Analyze selection for intelligent mixed-type handling
                var selectionState = AnalyzeSelection(selection, markerStyle);
                
                System.Diagnostics.Debug.WriteLine($"[EDITOR] Selection analysis: Action={selectionState.DominantAction}, Mixed={selectionState.HasMixedListTypes}, NonList={selectionState.HasNonListContent}");
                
                if (selectionState.AllInTargetList)
                {
                    // All selected paragraphs are already in target list type - remove formatting
                    foreach (var para in selection)
                    {
                        ConvertListItemToParagraph(para, false);
                    }
                    return;
                }
                
                // Handle mixed selections intelligently based on dominant action
                switch (selectionState.DominantAction)
                {
                    case ListAction.RemoveAllLists:
                        foreach (var para in selection)
                        {
                            if (para.Parent is ListItem)
                            {
                                ConvertListItemToParagraph(para, false);
                            }
                        }
                        return;
                        
                    case ListAction.ConvertToTarget:
                        // Convert all list items to target type, add list formatting to plain paragraphs
                        break; // Continue with normal formatting logic
                        
                    case ListAction.AddToTarget:
                        // Add target list formatting to all paragraphs
                        break; // Continue with normal formatting logic
                }
                
                // Continue with enhanced list formatting
                {
                    // PROFESSIONAL: Enhanced nested list support with alternating patterns
                    var currentPara = CaretPosition?.Paragraph;
                    var effectiveMarkerStyle = GetEffectiveMarkerStyle(markerStyle, currentPara);
                    
                    if (effectiveMarkerStyle != markerStyle)
                    {
                        System.Diagnostics.Debug.WriteLine($"[EDITOR] Professional nesting: {markerStyle} → {effectiveMarkerStyle}");
                    }
                    
                    // Apply list formatting with improved caret handling
                    var caretOffset = 0;
                    bool shouldRestoreCaret = false;
                    
                    // Save position for single paragraph selection (empty or with content)
                    if (selection.Count == 1 && currentPara != null && CaretPosition != null)
                    {
                        var range = new TextRange(currentPara.ContentStart, CaretPosition);
                        caretOffset = range.Text.Length;
                        shouldRestoreCaret = true;
                    }
                    
                    ApplyListToSelection(selection, effectiveMarkerStyle);
                    
                    // Restore caret position after list formatting
                    if (shouldRestoreCaret && currentPara != null)
                    {
                        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                        {
                            try
                            {
                                // Find the paragraph - it should now be inside a ListItem
                                var listItem = FindListItemContainingOriginalParagraph(currentPara);
                                if (listItem?.Blocks.FirstBlock is Paragraph para)
                                {
                                    if (caretOffset == 0)
                                    {
                                        // Empty paragraph - position after the bullet
                                        CaretPosition = para.ContentStart;
                                    }
                                    else
                                    {
                                        // Non-empty - restore to saved position
                                        var position = para.ContentStart;
                                        for (int i = 0; i < caretOffset && position != null; i++)
                                        {
                                            var next = position.GetNextInsertionPosition(LogicalDirection.Forward);
                                            if (next == null || next.CompareTo(para.ContentEnd) > 0) break;
                                            position = next;
                                        }
                                        CaretPosition = position;
                                    }
                                }
                            }
                            catch
                            {
                                // Silently fail - don't disrupt the user experience
                            }
                        }));
                    }
                }
            }
            finally
            {
                // Removed: _isUpdating flag
                // Debouncing removed - content updates immediately
            }
        }
        
        private ListItem FindListItemContainingOriginalParagraph(Paragraph originalPara)
        {
            try
            {
                // The paragraph reference should still be valid, just in a new parent
                if (originalPara.Parent is ListItem li)
                    return li;
                    
                return null;
            }
            catch
            {
                return null;
            }
        }

        public void IndentSelection()
        {
            try
            {
                // HYBRID APPROACH: Use reliable WPF command for all indentation
                EditingCommands.IncreaseIndentation.Execute(null, this);
                System.Diagnostics.Debug.WriteLine("[EDITOR] Indented using reliable WPF command");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Indent failed: {ex.Message}");
            }
        }

        public void OutdentSelection()
        {
            try
            {
                // HYBRID APPROACH: Use reliable WPF command for all outdentation  
                EditingCommands.DecreaseIndentation.Execute(null, this);
                System.Diagnostics.Debug.WriteLine("[EDITOR] Outdented using reliable WPF command");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Outdent failed: {ex.Message}");
            }
        }
        
        // REMOVED: Complex nesting level calculation and custom indent/outdent methods
        // Now using reliable WPF EditingCommands for all indent/outdent operations

        public void BatchUpdate(Action updateAction)
        {
            BeginChange();
            try
            {
                // Removed: _isUpdating flag
                updateAction();
            }
            finally
            {
                // Removed: _isUpdating flag
                EndChange();
            }
        }
        
        /// <summary>
        /// PHASE 2: Initialize consistent document styles for professional appearance
        /// </summary>
        private void InitializeDocumentStyles(FlowDocument document)
        {
            try
            {
                document.PagePadding = new Thickness(0);
                
                // PHASE 2: Enhanced paragraph styles with consistent spacing
                var paragraphStyle = new Style(typeof(Paragraph));
                paragraphStyle.Setters.Add(new Setter(Paragraph.MarginProperty, new Thickness(0, 0, 0, 8))); // 8px bottom margin
                paragraphStyle.Setters.Add(new Setter(Paragraph.ForegroundProperty, new DynamicResourceExtension("SystemControlForegroundBaseHighBrush")));
                paragraphStyle.Setters.Add(new Setter(Paragraph.LineHeightProperty, document.FontSize * 1.5)); // Consistent line height
                document.Resources[typeof(Paragraph)] = paragraphStyle;
                
                // PHASE 2: Perfect hanging indent list styles  
                var listStyle = new Style(typeof(List));
                listStyle.Setters.Add(new Setter(List.MarginProperty, new Thickness(0, 4, 0, 4)));        // Professional spacing
                listStyle.Setters.Add(new Setter(List.PaddingProperty, new Thickness(28, 0, 0, 0)));      // Perfect hanging indent
                listStyle.Setters.Add(new Setter(List.ForegroundProperty, new DynamicResourceExtension("SystemControlForegroundBaseHighBrush")));
                document.Resources[typeof(List)] = listStyle;
                
                // List items with minimal spacing
                var listItemStyle = new Style(typeof(ListItem));
                listItemStyle.Setters.Add(new Setter(ListItem.MarginProperty, new Thickness(0, 1, 0, 1)));
                document.Resources[typeof(ListItem)] = listItemStyle;
                
                // PHASE 2: Headers with consistent spacing and professional appearance
                var headerBaseStyle = new Style(typeof(Paragraph));
                headerBaseStyle.Setters.Add(new Setter(Paragraph.MarginProperty, new Thickness(0, 16, 0, 8))); // More space before headers
                headerBaseStyle.Setters.Add(new Setter(Paragraph.FontWeightProperty, FontWeights.Bold));
                headerBaseStyle.Setters.Add(new Setter(Paragraph.ForegroundProperty, new DynamicResourceExtension("SystemControlForegroundBaseHighBrush")));
                // Headers will be applied dynamically based on FontSize in ConvertBlock
                
                System.Diagnostics.Debug.WriteLine("[EDITOR] Document styles initialized for consistent, professional formatting");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Document style initialization failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// PHASE 2: Apply theme-aware formatting (per original plan)
        /// </summary>
        public void ApplyTheme(bool isDarkMode)
        {
            try
            {
                // Update document-level theme colors
                if (Document != null)
                {
                    // Theme-aware foreground color
                    var foregroundBrush = isDarkMode 
                        ? new SolidColorBrush(Colors.White) 
                        : new SolidColorBrush(Colors.Black);
                    
                    // Update all paragraph styles to use theme-appropriate colors
                    if (Document.Resources[typeof(Paragraph)] is Style paraStyle)
                    {
                        // Update existing setters or add new ones
                        var foregroundSetter = paraStyle.Setters.OfType<Setter>()
                            .FirstOrDefault(s => s.Property == Paragraph.ForegroundProperty);
                        if (foregroundSetter != null)
                        {
                            foregroundSetter.Value = foregroundBrush;
                        }
                        else
                        {
                            paraStyle.Setters.Add(new Setter(Paragraph.ForegroundProperty, foregroundBrush));
                        }
                    }
                    
                    // Update list styles for theme
                    if (Document.Resources[typeof(List)] is Style listStyle)
                    {
                        var foregroundSetter = listStyle.Setters.OfType<Setter>()
                            .FirstOrDefault(s => s.Property == List.ForegroundProperty);
                        if (foregroundSetter != null)
                        {
                            foregroundSetter.Value = foregroundBrush;
                        }
                        else
                        {
                            listStyle.Setters.Add(new Setter(List.ForegroundProperty, foregroundBrush));
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[EDITOR] Applied {(isDarkMode ? "dark" : "light")} theme to document");
                }
                
                // Update editor foreground (background handled by container/theme system)
                this.Foreground = isDarkMode 
                    ? new SolidColorBrush(Colors.White) 
                    : new SolidColorBrush(Colors.Black);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Theme application failed: {ex.Message}");
            }
        }

        /// <summary>
        /// PHASE 2: Helper methods for predictable key behavior (per original plan)
        /// </summary>
        private bool IsInList()
        {
            return CaretPosition?.Paragraph?.Parent is ListItem;
        }
        
        private bool CurrentListItemIsEmpty()
        {
            var para = CaretPosition?.Paragraph;
            if (para?.Parent is ListItem)
            {
                var range = new TextRange(para.ContentStart, para.ContentEnd);
                return string.IsNullOrWhiteSpace(range.Text);
            }
            return false;
        }
        
        private void ExitList()
        {
            var para = CaretPosition?.Paragraph;
            if (para?.Parent is ListItem listItem)
            {
                // Convert current list item to regular paragraph
                ConvertListItemToParagraph(para, true);
                System.Diagnostics.Debug.WriteLine("[EDITOR] Exited list (empty item - double Enter)");
            }
        }
        
        private void CreateNewListItem()
        {
            var para = CaretPosition?.Paragraph;
            if (para != null && TryContinueList(para))
            {
                System.Diagnostics.Debug.WriteLine("[EDITOR] Created new list item (Enter with content)");
            }
        }
        
        // REMOVED: Complex helper methods that caused infinite loops
        // Now using simple, reliable WPF commands directly
        
        private bool IsAtStartOfListItem()
        {
            var para = CaretPosition?.Paragraph;
            if (para?.Parent is ListItem && CaretPosition != null)
            {
                return CaretPosition.CompareTo(para.ContentStart) == 0;
            }
            return false;
        }
        
        private bool IsAtEndOfListItem()
        {
            var para = CaretPosition?.Paragraph;
            if (para?.Parent is ListItem && CaretPosition != null)
            {
                return CaretPosition.CompareTo(para.ContentEnd) == 0;
            }
            return false;
        }
        
        private void RemoveListFormattingFromCurrentItem()
        {
            var para = CaretPosition?.Paragraph;
            if (para?.Parent is ListItem)
            {
                ConvertListItemToParagraph(para, true);
                System.Diagnostics.Debug.WriteLine("[EDITOR] Removed list formatting (backspace at start)");
            }
        }
        
        private bool TryMergeWithNextListItem()
        {
            var para = CaretPosition?.Paragraph;
            if (para?.Parent is not ListItem currentItem || 
                currentItem.Parent is not List list) return false;
            
            // Begin undoable transaction for bulletproof safety
            BeginChange();
            try
            {
                int index = GetListItemIndex(list, currentItem);
                var nextItem = GetListItemAt(list, index + 1);
                
                if (nextItem?.Blocks.FirstBlock is not Paragraph nextPara) return false;
                
                // Save merge point for precise caret restoration
                var mergePoint = para.ContentEnd;
                
                // BULLETPROOF: Use WPF's TextRange for proper formatting preservation
                var sourceRange = new TextRange(nextPara.ContentStart, nextPara.ContentEnd);
                var targetRange = new TextRange(mergePoint, mergePoint);
                
                // Preserve all formatting during merge using XAML serialization
                using (var stream = new System.IO.MemoryStream())
                {
                    try
                    {
                        sourceRange.Save(stream, DataFormats.Xaml);
                        stream.Position = 0;
                        targetRange.Load(stream, DataFormats.Xaml);
                    }
                    catch
                    {
                        // Fallback to plain text if XAML fails
                        var text = sourceRange.Text;
                        if (!string.IsNullOrEmpty(text))
                        {
                            targetRange.Text = text;
                        }
                    }
                }
                
                // Handle remaining blocks in next item (nested lists, images, etc.)
                var remainingBlocks = nextItem.Blocks.Skip(1).ToList();
                foreach (var block in remainingBlocks)
                {
                    nextItem.Blocks.Remove(block);
                    currentItem.Blocks.Add(block);
                }
                
                // Remove merged item safely
                list.ListItems.Remove(nextItem);
                
                // Position caret at merge boundary with safety checks
                var newCaretPosition = mergePoint.GetPositionAtOffset(0, LogicalDirection.Forward) ?? mergePoint;
                CaretPosition = newCaretPosition;
                
                System.Diagnostics.Debug.WriteLine("[EDITOR] Safely merged with next list item (formatting preserved)");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Safe merge failed: {ex.Message}");
                return false;
            }
            finally
            {
                EndChange(); // Ensures undo support and document consistency
            }
        }

        // Removed: List formatting methods (part of deleted list tracking system)
        public string GetListFormatting()
        {
            return string.Empty; // Simplified - no complex list formatting
        }

        public void RestoreListFormatting(string formatting)
        {
            // Simplified - no complex list formatting restoration needed
        }

        #region Debug Helper
        private void LogListOperation(string operation, string details = "")
        {
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"[LIST-OP] {operation}: {details}");
            
            // Log current state
            var para = CaretPosition?.Paragraph;
            if (para != null)
            {
                var text = new TextRange(para.ContentStart, para.ContentEnd).Text;
                var beforeCaret = new TextRange(para.ContentStart, CaretPosition).Text;
                System.Diagnostics.Debug.WriteLine($"  Text: '{text}'");
                System.Diagnostics.Debug.WriteLine($"  Before Caret: '{beforeCaret}'");
                System.Diagnostics.Debug.WriteLine($"  Is List Item: {para.Parent is ListItem}");
            }
            #endif
        }
        #endregion

        #region List Formatting Metadata
        private string ExtractListFormattingMetadata()
        {
            // Since the markdown converter already handles lists correctly,
            // we don't need to store additional metadata.
            // The issue is likely in rendering or styles, not in conversion.
            return string.Empty;
        }

        private void ApplyListFormattingMetadata(string listFormatting)
        {
            // List styles are now applied directly when the document is loaded.
            // This method is kept for future enhancements where we might need
            // to restore custom list formatting from metadata.
        }
        #endregion
        
        /// <summary>
        /// UX POLISH: Get current list state for toolbar feedback
        /// </summary>
        public ListState GetCurrentListState()
        {
            var para = CaretPosition?.Paragraph;
            if (para?.Parent is ListItem listItem && listItem.Parent is List list)
            {
                return new ListState
                {
                    IsInList = true,
                    IsInBulletList = list.MarkerStyle == TextMarkerStyle.Disc,
                    IsInNumberedList = list.MarkerStyle == TextMarkerStyle.Decimal,
                    NestingLevel = GetSimpleNestingLevel(list)
                };
            }
            
            return new ListState { IsInList = false };
        }
        
        /// <summary>
        /// UX POLISH: Simple, safe nesting level calculation (no infinite loops)
        /// </summary>
        private int GetSimpleNestingLevel(List list)
        {
            int level = 1;
            var current = list.Parent;
            int maxChecks = 10; // Safety limit
            
            while (current != null && maxChecks-- > 0)
            {
                if (current is ListItem listItem && listItem.Parent is List parentList)
                {
                    level++;
                    current = parentList.Parent;
                }
                else
                {
                    break;
                }
            }
            
            return level;
        }
        
        /// <summary>
        /// PROFESSIONAL: Determine optimal marker style based on nesting level and user intent
        /// </summary>
        private TextMarkerStyle GetEffectiveMarkerStyle(TextMarkerStyle requestedStyle, Paragraph currentPara)
        {
            if (currentPara?.Parent is not ListItem parentListItem || 
                parentListItem.Parent is not List parentList) 
                return requestedStyle;
            
            var nestingLevel = GetSimpleNestingLevel(parentList);
            
            // Professional alternating pattern based on nesting depth
            return (requestedStyle, nestingLevel) switch
            {
                // Numbered list professional hierarchy
                (TextMarkerStyle.Decimal, 1) => TextMarkerStyle.Decimal,      // 1. Top level numbers
                (TextMarkerStyle.Decimal, 2) => TextMarkerStyle.LowerLatin,   // a. Second level letters  
                (TextMarkerStyle.Decimal, 3) => TextMarkerStyle.LowerRoman,   // i. Third level roman
                (TextMarkerStyle.Decimal, _) => TextMarkerStyle.Disc,         // • Deep nesting fallback
                
                // Bullet list professional hierarchy
                (TextMarkerStyle.Disc, 1) => TextMarkerStyle.Disc,            // • Top level bullets
                (TextMarkerStyle.Disc, 2) => TextMarkerStyle.Circle,          // ○ Second level circles
                (TextMarkerStyle.Disc, 3) => TextMarkerStyle.Square,          // ▪ Third level squares  
                (TextMarkerStyle.Disc, _) => TextMarkerStyle.Disc,            // • Deep nesting fallback
                
                // Handle other marker styles gracefully
                _ => requestedStyle
            };
        }
        
        /// <summary>
        /// ENHANCED: Analyze mixed selection for smart list handling
        /// </summary>
        private ListSelectionState AnalyzeSelection(List<Paragraph> selection, TextMarkerStyle targetStyle)
        {
            int totalParagraphs = selection.Count;
            int inTargetList = 0, inOtherList = 0, notInList = 0;
            var listTypes = new HashSet<TextMarkerStyle>();
            
            foreach (var para in selection)
            {
                if (para.Parent is ListItem li && li.Parent is List list)
                {
                    listTypes.Add(list.MarkerStyle);
                    if (list.MarkerStyle == targetStyle)
                        inTargetList++;
                    else
                        inOtherList++;
                }
                else
                {
                    notInList++;
                }
            }
            
            return new ListSelectionState
            {
                AllInTargetList = inTargetList == totalParagraphs,
                AllInList = (inTargetList + inOtherList) == totalParagraphs,
                HasMixedListTypes = listTypes.Count > 1,
                HasNonListContent = notInList > 0,
                DominantAction = DetermineDominantAction(inTargetList, inOtherList, notInList, targetStyle)
            };
        }

        /// <summary>
        /// ENHANCED: Determine best action for mixed selections using smart heuristics
        /// </summary>
        private ListAction DetermineDominantAction(int target, int other, int none, TextMarkerStyle targetStyle)
        {
            // Smart heuristics for mixed selections
            if (target > (other + none)) return ListAction.RemoveAllLists;  // Mostly target → remove
            if ((target + other) < none) return ListAction.AddToTarget;     // Mostly plain → add
            return ListAction.ConvertToTarget;  // Mixed → standardize to target
        }
        
        /// <summary>
        /// UX POLISH: Check for list state changes and notify toolbar
        /// </summary>
        private void CheckAndNotifyListStateChange()
        {
            var newState = GetCurrentListState();
            
            if (!_currentListState.Equals(newState))
            {
                _currentListState = newState;
                ListStateChanged?.Invoke(this, new ListStateChangedEventArgs(newState));
                
                System.Diagnostics.Debug.WriteLine($"[EDITOR] List state changed: InList={newState.IsInList}, Bullets={newState.IsInBulletList}, Numbers={newState.IsInNumberedList}");
            }
        }
    }
    
    /// <summary>
    /// UX POLISH: Event args for list state changes
    /// </summary>
    public class ListStateChangedEventArgs : EventArgs
    {
        public ListState State { get; }
        
        public ListStateChangedEventArgs(ListState state)
        {
            State = state;
        }
    }
    
    /// <summary>
    /// UX POLISH: Represents current list formatting state with proper equality implementation
    /// </summary>
    public class ListState : IEquatable<ListState>
    {
        public bool IsInList { get; set; }
        public bool IsInBulletList { get; set; }
        public bool IsInNumberedList { get; set; }
        public int NestingLevel { get; set; }
        
        public bool Equals(ListState? other)
        {
            return other != null &&
                   IsInList == other.IsInList &&
                   IsInBulletList == other.IsInBulletList &&
                   IsInNumberedList == other.IsInNumberedList &&
                   NestingLevel == other.NestingLevel;
        }
        
        public override bool Equals(object? obj) => Equals(obj as ListState);
        
        public override int GetHashCode() => HashCode.Combine(IsInList, IsInBulletList, IsInNumberedList, NestingLevel);
        
        public static bool operator ==(ListState? left, ListState? right) => 
            ReferenceEquals(left, right) || (left?.Equals(right) == true);
            
        public static bool operator !=(ListState? left, ListState? right) => !(left == right);
    }
    
    /// <summary>
    /// ENHANCED: Represents the state of a mixed selection for intelligent list handling
    /// </summary>
    public class ListSelectionState
    {
        public bool AllInTargetList { get; set; }
        public bool AllInList { get; set; }
        public bool HasMixedListTypes { get; set; }
        public bool HasNonListContent { get; set; }
        public ListAction DominantAction { get; set; }
    }
    
    /// <summary>
    /// ENHANCED: Actions that can be taken on mixed list selections
    /// </summary>
    public enum ListAction
    {
        RemoveAllLists,    // Remove list formatting from all paragraphs
        ConvertToTarget,   // Convert all lists to target type  
        AddToTarget        // Add list formatting of target type
    }
}



