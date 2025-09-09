using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using NoteNest.UI.Services;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using NoteNest.Core.Models;
using NoteNest.UI.Controls.ListHandling;

namespace NoteNest.UI.Controls
{
    public partial class FormattedTextEditor : RichTextBox
    {
        private bool _isUpdating;
        private readonly MarkdownFlowDocumentConverter _converter;
        private readonly ListStateTracker _listTracker = new ListStateTracker();
        private NoteModel _currentNote;
        private NoteNest.Core.Services.NoteMetadataManager _metadataManager;

        public static readonly DependencyProperty MarkdownContentProperty =
            DependencyProperty.Register(
                nameof(MarkdownContent),
                typeof(string),
                typeof(FormattedTextEditor),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnMarkdownContentChanged));

        public string MarkdownContent
        {
            get => (string)GetValue(MarkdownContentProperty);
            set => SetValue(MarkdownContentProperty, value);
        }

        public NoteModel CurrentNote 
        {
            get => _currentNote;
            set => _currentNote = value;
        }

        public void SetMetadataManager(NoteNest.Core.Services.NoteMetadataManager manager)
        {
            _metadataManager = manager;
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
                    Document.PagePadding = new Thickness(0);
                    var pStyle = new Style(typeof(Paragraph));
                    pStyle.Setters.Add(new Setter(Paragraph.MarginProperty, new Thickness(0, 0, 0, 1)));
                    pStyle.Setters.Add(new Setter(Paragraph.ForegroundProperty, new DynamicResourceExtension("SystemControlForegroundBaseHighBrush")));
                    Document.Resources[typeof(Paragraph)] = pStyle;
                    
                    // Add explicit styles for List elements to ensure proper block-level rendering
                    var listStyle = new Style(typeof(List));
                    listStyle.Setters.Add(new Setter(List.MarginProperty, new Thickness(0, 2, 0, 2)));
                    listStyle.Setters.Add(new Setter(List.PaddingProperty, new Thickness(20, 0, 0, 0)));
                    Document.Resources[typeof(List)] = listStyle;
                    
                    var listItemStyle = new Style(typeof(ListItem));
                    listItemStyle.Setters.Add(new Setter(ListItem.MarginProperty, new Thickness(0, 1, 0, 1)));
                    Document.Resources[typeof(ListItem)] = listItemStyle;
                }
            }
            catch { }

            // Simple text changed - no debouncing
            TextChanged += (s, e) =>
            {
                if (!_isUpdating)
                {
                    _isUpdating = true;
                    var markdown = _converter.ConvertToMarkdown(Document);
                    SetCurrentValue(MarkdownContentProperty, markdown);
                    _isUpdating = false;
                }
            };
            GotFocus += (s, e) => Keyboard.Focus(this);

            // Editing command keybindings
            InputBindings.Add(new KeyBinding(EditingCommands.ToggleBold, Key.B, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(EditingCommands.ToggleItalic, Key.I, ModifierKeys.Control));

            // Register custom command bindings
            RegisterCommandBindings();
            
            // Initialize numbering system
            InitializeNumberingSystem();

            // Smart list behaviors
            PreviewKeyDown += OnPreviewKeyDown;
            PreviewKeyUp += OnPreviewKeyUp;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Don't process if we're already updating
            if (_isUpdating) return;
            
            switch (e.Key)
            {
                case Key.Enter:
                    if (Keyboard.Modifiers == ModifierKeys.Shift)
                    {
                        // Shift+Enter: soft line break
                        e.Handled = true;
                        EditingCommands.EnterLineBreak.Execute(null, this);
                        return;
                    }
                    
                var para = CaretPosition?.Paragraph;
                    if (para != null && TryContinueList(para))
                    {
                        e.Handled = true;
                        return;
                    }
                    break;
                    
                case Key.Back:
                    HandleBackspaceKey(e);
                    break;
                    
                case Key.Delete:
                    para = CaretPosition?.Paragraph;
                    if (para?.Parent is ListItem && IsAtListItemEnd())
                    {
                        if (TryDeleteMergeAtEnd(para))
                    {
                        e.Handled = true;
                        return;
                    }
                }
                    break;
                    
                case Key.Tab:
                    para = CaretPosition?.Paragraph;
                    if (para?.Parent is ListItem)
                    {
                        e.Handled = true;
                        
                        if (Keyboard.Modifiers == ModifierKeys.Shift)
                        {
                            // Outdent
                            OutdentListCommand.Execute(null, this);
                        }
                        else
                        {
                            // Indent - use built-in command
                            EditingCommands.IncreaseIndentation.Execute(null, this);
                        }
                        return;
                    }
                    break;
            }
        }

        private void OnPreviewKeyUp(object sender, KeyEventArgs e)
        {
            // No longer tracking double-enter behavior
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

                _isUpdating = true;
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
                    
                    // After creating new list item
                    if (list.MarkerStyle == TextMarkerStyle.Decimal)
                    {
                        // Schedule renumbering for numbered lists
                        ScheduleRenumbering();
                    }
                    
                return true;
                }
                finally
                {
                    _isUpdating = false;
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

                _isUpdating = true;
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
                    _isUpdating = false;
                    // Debouncing removed - content updates immediately
            }
        }

        private void RemoveListItem(List list, ListItem item)
        {
            RemoveListItemSimple(list, item);
            
            // Trigger renumbering if it's a numbered list
            if (list?.MarkerStyle == TextMarkerStyle.Decimal)
            {
                ScheduleRenumbering();
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

            _isUpdating = true;
            try
            {
                if (outdent)
                {
                    return TryOutdentListItem(li, parentList);
                }
                else
                {
                    return TryIndentListItem(li, parentList);
                }
            }
            finally
            {
                _isUpdating = false;
                // Debouncing removed - content updates immediately
            }
        }

        private bool TryIndentListItem(ListItem item, List parentList)
        {
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
                // Create new nested list
                nestedList = new List 
                { 
                    MarkerStyle = parentList.MarkerStyle, 
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
                ScheduleRenumbering();
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
                        ScheduleRenumbering();
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

            _isUpdating = true;
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
                _isUpdating = false;
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

            _isUpdating = true;
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
                _isUpdating = false;
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

        private static void OnMarkdownContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (FormattedTextEditor)d;
            if (editor._isUpdating) return;
            editor.UpdateDocumentFromMarkdown(e.NewValue as string ?? string.Empty);
        }


        private void UpdateDocumentFromMarkdown(string markdown)
        {
            _isUpdating = true;
            try
            {
                var app = Application.Current as App;
                var config = app?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.ConfigurationService)) as NoteNest.Core.Services.ConfigurationService;
                var fontFamily = config?.Settings?.FontFamily;
                double? fontSize = config?.Settings?.FontSize;
                bool enhancedLists = config?.Settings?.EnhancedListHandlingEnabled ?? false;

                // Preserve caret and avoid replacing the Document instance
                var caret = CaretPosition;
                var flow = _converter.ConvertToFlowDocument(markdown, fontFamily, fontSize);
                try
                {
                    Document.Blocks.Clear();
                    
                    // Ensure document has proper styles for lists
                    var listStyle = new Style(typeof(List));
                    listStyle.Setters.Add(new Setter(List.MarginProperty, new Thickness(0, 2, 0, 2)));
                    listStyle.Setters.Add(new Setter(List.PaddingProperty, new Thickness(20, 0, 0, 0)));
                    Document.Resources[typeof(List)] = listStyle;
                    
                    var listItemStyle = new Style(typeof(ListItem));
                    listItemStyle.Setters.Add(new Setter(ListItem.MarginProperty, new Thickness(0, 1, 0, 1)));
                    Document.Resources[typeof(ListItem)] = listItemStyle;
                    
                    foreach (var block in flow.Blocks.ToList())
                    {
                        flow.Blocks.Remove(block);
                        Document.Blocks.Add(block);
                    }
                }
                catch { /* ignore copy failures */ }
                try { CaretPosition = Document?.ContentEnd ?? caret; } catch { }

                // Load and apply list formatting metadata if available
                if (_currentNote != null && _metadataManager != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var listFormatting = await _metadataManager.LoadListFormattingAsync(_currentNote);
                            if (!string.IsNullOrEmpty(listFormatting))
                            {
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    ApplyListFormattingMetadata(listFormatting);
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to load list formatting: {ex.Message}");
                        }
                    });
                }
            }
            finally
            {
                _isUpdating = false;
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
            _isUpdating = true;
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
                _isUpdating = false;
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
            _isUpdating = true;
            try
            {
                var selection = GetSelectedParagraphs();
                
                // Check if ALL selected paragraphs are in lists of this type
                bool allInList = selection.All(p => 
                    p.Parent is ListItem li && 
                    li.Parent is List list && 
                    list.MarkerStyle == markerStyle);
                
                if (allInList)
                {
                    // Remove list formatting
                    foreach (var para in selection)
                    {
                        ConvertListItemToParagraph(para, false);
                    }
                }
                else
                {
                    // Apply list formatting - fix caret for both empty and non-empty paragraphs
                    var currentPara = CaretPosition?.Paragraph;
                    var caretOffset = 0;
                    bool shouldRestoreCaret = false;
                    
                    // Save position for single paragraph selection (empty or with content)
                    if (selection.Count == 1 && currentPara != null && CaretPosition != null)
                    {
                        var range = new TextRange(currentPara.ContentStart, CaretPosition);
                        caretOffset = range.Text.Length;
                        shouldRestoreCaret = true;
                    }
                    
                    ApplyListToSelection(selection, markerStyle);
                    
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
                _isUpdating = false;
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
            var para = CaretPosition?.Paragraph;
            if (para?.Parent is ListItem)
            {
                TryChangeListIndent(para, false); // false = indent
            }
            else if (para != null)
            {
                // Regular paragraph - add left margin
                para.Margin = new Thickness(
                    para.Margin.Left + 20, 
                    para.Margin.Top, 
                    para.Margin.Right, 
                    para.Margin.Bottom);
            }
        }

        public void OutdentSelection()
        {
            var para = CaretPosition?.Paragraph;
            if (para?.Parent is ListItem)
            {
                TryChangeListIndent(para, true); // true = outdent
            }
            else if (para != null)
            {
                // Regular paragraph - reduce left margin
                var newMargin = Math.Max(0, para.Margin.Left - 20);
                para.Margin = new Thickness(
                    newMargin, 
                    para.Margin.Top, 
                    para.Margin.Right, 
                    para.Margin.Bottom);
            }
        }

        public void BatchUpdate(Action updateAction)
        {
            BeginChange();
            try
            {
                _isUpdating = true;
                updateAction();
            }
            finally
            {
                _isUpdating = false;
                EndChange();
            }
        }

        public string GetListFormatting()
        {
            try
            {
                return _listTracker.SerializeState(Document) ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        public void RestoreListFormatting(string formatting)
        {
            if (string.IsNullOrWhiteSpace(formatting)) return;
            try
            {
                _listTracker.RestoreState(Document, formatting);
            }
            catch { }
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
    }
}



