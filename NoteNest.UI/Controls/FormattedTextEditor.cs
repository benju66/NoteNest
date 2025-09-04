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
using NoteNest.UI.Controls.ListHandling;

namespace NoteNest.UI.Controls
{
    public class FormattedTextEditor : RichTextBox
    {
        private bool _isUpdating;
        private readonly MarkdownFlowDocumentConverter _converter;
        private readonly DispatcherTimer _debounceTimer;
        private readonly ListStateTracker _listTracker = new ListStateTracker();

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

        public FormattedTextEditor()
        {
            _converter = new MarkdownFlowDocumentConverter();
            IsReadOnly = false;
            IsReadOnlyCaretVisible = true;
            Focusable = true;
            IsHitTestVisible = true;
            Background = System.Windows.Media.Brushes.Transparent;
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
                    Document.Resources[typeof(Paragraph)] = pStyle;
                }
            }
            catch { }

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                PushDocumentToMarkdown();
            };

            TextChanged += OnTextChanged;
            GotFocus += (s, e) => Keyboard.Focus(this);

            // Editing command keybindings
            InputBindings.Add(new KeyBinding(EditingCommands.ToggleBold, Key.B, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(EditingCommands.ToggleItalic, Key.I, ModifierKeys.Control));

            // Smart list behaviors
            PreviewKeyDown += OnPreviewKeyDown;
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
                    para = CaretPosition?.Paragraph;
                    if (para != null)
                    {
                        // Check empty list item first
                        if (IsEmptyListItem(para))
                        {
                            e.Handled = true;
                            RemoveCurrentEmptyListItem(para);
                            return;
                        }
                        
                        // Check if at start of list item
                        if (IsAtListItemStart() && para.Parent is ListItem li && li.Parent is List list)
                        {
                            e.Handled = true;
                            
                            // If nested, outdent first
                            if (list.Parent is ListItem)
                            {
                                TryChangeListIndent(para, true);
                            }
                            else
                            {
                                // At root level, convert to paragraph
                                ConvertListItemToParagraph(para, mergeWithPreviousIfParagraph: true);
                            }
                            return;
                        }
                    }
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
                    if (para != null && TryChangeListIndent(para, Keyboard.Modifiers == ModifierKeys.Shift))
                    {
                        e.Handled = true;
                        return;
                    }
                    break;
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
                
                // Empty list item: convert to normal paragraph
                if (string.IsNullOrWhiteSpace(text))
                {
                    ConvertListItemToParagraph(currentPara, mergeWithPreviousIfParagraph: false);
                    return true;
                }

                // Create new list item
                var caret = CaretPosition ?? currentPara.ContentEnd;
                var newPara = new Paragraph { Margin = new Thickness(0) };
                var newLi = new ListItem();
                newLi.Blocks.Add(newPara);

                // Handle content splitting if caret is not at end
                if (caret.CompareTo(currentPara.ContentEnd) < 0)
                {
                    var trailing = new TextRange(caret, currentPara.ContentEnd);
                    using (var stream = new MemoryStream())
                    {
                        trailing.Save(stream, DataFormats.Xaml);
                        trailing.Text = string.Empty; // Clear original
                        
                        stream.Position = 0;
                        var dest = new TextRange(newPara.ContentStart, newPara.ContentEnd);
                        dest.Load(stream, DataFormats.Xaml);
                    }
                }

                // Insert the new list item
                int index = GetListItemIndex(list, li);
                InsertListItemAt(list, index + 1, newLi);
                
                // Position caret at start of new item
                CaretPosition = newPara.ContentStart;
                return true;
            }
            finally
            {
                _isUpdating = false;
                _debounceTimer.Stop();
                _debounceTimer.Start();
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
                int index = GetListItemIndex(list, li);
                var parentBlocks = GetParentBlockCollection(list);
                
                // Remove the empty list item
                list.ListItems.Remove(li);
                
                // Determine where to position caret
                if (GetListItemCount(list) > 0)
                {
                    // Move to previous item if exists
                    if (index > 0)
                    {
                        var prevItem = GetListItemAt(list, index - 1);
                        if (prevItem?.Blocks.LastBlock is Paragraph prevPara)
                        {
                            CaretPosition = prevPara.ContentEnd;
                            return;
                        }
                    }
                    // Or move to next item (now at same index)
                    else
                    {
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
                    // List is now empty, remove it and create a paragraph
                    if (parentBlocks != null)
                    {
                        var newPara = new Paragraph();
                        parentBlocks.InsertAfter(list, newPara);
                        parentBlocks.Remove(list);
                        CaretPosition = newPara.ContentStart;
                        return;
                    }
                }
                
                // Fallback
                CaretPosition = Document.ContentEnd;
            }
            finally
            {
                _isUpdating = false;
                _debounceTimer.Stop();
                _debounceTimer.Start();
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
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }

        private bool TryIndentListItem(ListItem item, List parentList)
        {
            int index = GetListItemIndex(parentList, item);
            if (index <= 0) return false; // Can't indent first item

            var prevItem = GetListItemAt(parentList, index - 1);
            if (prevItem == null) return false;

            List nested;
            if (prevItem.Blocks.LastBlock is List existing)
            {
                nested = existing;
            }
            else
            {
                nested = new List { MarkerStyle = parentList.MarkerStyle, Margin = new Thickness(0, 1, 0, 1) };
                prevItem.Blocks.Add(nested);
            }

            RemoveListItemAt(parentList, index);
            AddListItem(nested, item);

            if (item.Blocks.FirstBlock is Paragraph firstP)
            {
                firstP.Margin = new Thickness(0);
                CaretPosition = firstP.ContentStart;
            }
            return true;
        }

        private bool TryOutdentListItem(ListItem item, List list)
        {
            // If nested: move item to the parent list after its container list item
            if (list.Parent is ListItem parentItem)
            {
                if (parentItem.Parent is not List grandParentList) return false;
                int insertIndex = GetListItemIndex(grandParentList, parentItem) + 1;
                try { list.ListItems.Remove(item); } catch { }
                InsertListItemAt(grandParentList, insertIndex, item);

                // Clean up empty nested list
                try
                {
                    if (GetListItemCount(list) == 0)
                    {
                        parentItem.Blocks.Remove(list);
                    }
                }
                catch { }

                if (item.Blocks.FirstBlock is Paragraph p)
                {
                    p.Margin = new Thickness(0);
                    CaretPosition = p.ContentStart;
                }
                return true;
            }

            // Root-level: convert to paragraph
            if (item.Blocks.FirstBlock is Paragraph rootPara)
            {
                ConvertListItemToParagraph(rootPara, mergeWithPreviousIfParagraph: false);
                return true;
            }

            return false;
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
            
            // More reliable check using GetOffsetToPosition
            var startPos = para.ContentStart;
            var currentPos = CaretPosition;
            
            return startPos.GetOffsetToPosition(currentPos) <= 0;
        }

        private bool IsAtListItemEnd()
        {
            var para = CaretPosition?.Paragraph;
            if (para?.Parent is not ListItem) return false;
            
            // Check if we're at or very close to the end
            var endPos = para.ContentEnd;
            var currentPos = CaretPosition;
            
            // Use GetOffsetToPosition for accurate comparison
            var offset = currentPos.GetOffsetToPosition(endPos);
            // Allow for 1 character of tolerance (for the paragraph break)
            return offset >= -1 && offset <= 0;
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
                
                // Handle merge with previous paragraph
                if (mergeWithPreviousIfParagraph && index == 0)
                {
                    Block prevBlock = list.PreviousBlock;
                    if (prevBlock is Paragraph prevPara)
                    {
                        // Save caret position before merge
                        var content = new TextRange(para.ContentStart, para.ContentEnd).Text;
                        var prevLength = new TextRange(prevPara.ContentStart, prevPara.ContentEnd).Text.Length;
                        
                        // Merge content
                        MergeParagraphInto(para, prevPara);
                        list.ListItems.Remove(li);
                        
                        // Remove empty list
                        if (GetListItemCount(list) == 0)
                        {
                            parentBlocks.Remove(list);
                        }
                        
                        // Position caret at merge point
                        CaretPosition = prevPara.ContentStart.GetPositionAtOffset(prevLength);
                        return;
                    }
                }

                // Create new paragraph with content
                var newPara = new Paragraph();
                CopyParagraphContent(para, newPara);

                // Determine where to insert the new paragraph
                if (index == 0)
                {
                    // Insert before the list
                    parentBlocks.InsertBefore(list, newPara);
                }
                else if (index >= GetListItemCount(list) - 1)
                {
                    // Insert after the list
                    parentBlocks.InsertAfter(list, newPara);
                }
                else
                {
                    // Split the list (middle item)
                    SplitListAtItem(list, li, newPara, parentBlocks);
                    CaretPosition = newPara.ContentStart;
                    return;
                }

                // Remove the list item
                list.ListItems.Remove(li);
                
                // Remove empty list
                if (GetListItemCount(list) == 0)
                {
                    parentBlocks.Remove(list);
                }
                
                // Position caret at start of new paragraph
                CaretPosition = newPara.ContentStart;
            }
            finally
            {
                _isUpdating = false;
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }

        private void MergeParagraphInto(Paragraph from, Paragraph to)
        {
            if (from == null || to == null) return;
            
            // Use TextRange for reliable content transfer
            var fromRange = new TextRange(from.ContentStart, from.ContentEnd);
            var toEndPos = to.ContentEnd;
            
            // Insert space if needed
            var toText = new TextRange(to.ContentStart, to.ContentEnd).Text;
            if (!string.IsNullOrEmpty(toText) && !toText.EndsWith(" "))
            {
                toEndPos.InsertTextInRun(" ");
            }
            
            // Copy content with formatting
            using (var stream = new MemoryStream())
            {
                fromRange.Save(stream, DataFormats.Xaml);
                stream.Position = 0;
                var insertRange = new TextRange(toEndPos, toEndPos);
                insertRange.Load(stream, DataFormats.Xaml);
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
            
            // Use TextRange for complete content transfer
            var sourceRange = new TextRange(source.ContentStart, source.ContentEnd);
            using (var stream = new MemoryStream())
            {
                sourceRange.Save(stream, DataFormats.Xaml);
                stream.Position = 0;
                var targetRange = new TextRange(target.ContentStart, target.ContentEnd);
                targetRange.Load(stream, DataFormats.Xaml);
            }
        }

        private BlockCollection GetParentBlockCollection(Block block)
        {
            if (block.Parent is FlowDocument doc) return doc.Blocks;
            if (block.Parent is Section section) return section.Blocks;
            if (block.Parent is ListItem listItem) return listItem.Blocks;
            if (block.Parent is TableCell cell) return cell.Blocks;
            return null;
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

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;
            _debounceTimer.Stop();
            _debounceTimer.Start();
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
                    foreach (var block in flow.Blocks.ToList())
                    {
                        flow.Blocks.Remove(block);
                        Document.Blocks.Add(block);
                    }
                }
                catch { /* ignore copy failures */ }
                try { CaretPosition = Document?.ContentEnd ?? caret; } catch { }

                // Placeholder: list formatting restore will be invoked by caller on load if needed
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void PushDocumentToMarkdown()
        {
            _isUpdating = true;
            try
            {
                var markdown = _converter.ConvertToMarkdown(Document);
                SetCurrentValue(MarkdownContentProperty, markdown);

                // Optionally persist list formatting metadata (Phase 1: no-op here; handled by caller on save)
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
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }

        private List<Paragraph> GetParagraphRange(Paragraph startPara, Paragraph endPara)
        {
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

        private void ApplyListToParagraphs(List<Paragraph> paragraphs, TextMarkerStyle style)
        {
            if (paragraphs == null || paragraphs.Count == 0) return;

            var firstPara = paragraphs.First();
            var parentCollection = (firstPara.Parent as FlowDocument)?.Blocks
                ?? (firstPara.Parent as Section)?.Blocks
                ?? (firstPara.Parent as ListItem)?.Blocks
                ?? Document.Blocks;

            var list = new List { MarkerStyle = style, Margin = new Thickness(0, 1, 0, 1) };

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
                var startPara = Selection.Start?.Paragraph ?? CaretPosition?.Paragraph;
                var endPara = Selection.End?.Paragraph ?? startPara;
                if (startPara == null) return;

                var paragraphs = GetParagraphRange(startPara, endPara);
                if (paragraphs.Count == 0) return;

                bool allInSameType = paragraphs.All(p => IsInListOfType(p, markerStyle));
                bool anyNotInList = paragraphs.Any(p => p?.Parent is not ListItem);
                bool anyInDifferentType = paragraphs.Any(p => (p?.Parent is ListItem li && li.Parent is List list) && list.MarkerStyle != markerStyle);

                if (allInSameType)
                {
                    // Remove formatting for selected items only
                    RemoveListFromParagraphs(paragraphs);
                }
                else if (anyNotInList)
                {
                    // Mixed selection → apply list to all
                    ApplyListToParagraphs(paragraphs, markerStyle);
                }
                else if (anyInDifferentType)
                {
                    // Change marker style in place
                    ChangeMarkerStyleForSelectedLists(paragraphs, markerStyle);
                }
                else
                {
                    // Default: apply list
                    ApplyListToParagraphs(paragraphs, markerStyle);
                }
            }
            finally
            {
                _isUpdating = false;
                _debounceTimer.Stop();
                _debounceTimer.Start();
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
    }
}


