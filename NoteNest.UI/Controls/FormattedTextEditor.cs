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
            if (e.Key == Key.Enter)
            {
                // Shift+Enter inserts a soft line break in the current item/paragraph
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    e.Handled = true;
                    try { EditingCommands.EnterLineBreak.Execute(null, this); } catch { }
                    return;
                }
                var para = CaretPosition?.Paragraph;
                if (para != null)
                {
                    if (TryContinueList(para))
                    {
                        e.Handled = true;
                        return;
                    }
                }
            }
            else if (e.Key == Key.Back)
            {
                var para = CaretPosition?.Paragraph;
                if (para != null)
                {
                    if (IsEmptyListItem(para))
                    {
                        e.Handled = true;
                        RemoveCurrentEmptyListItem(para);
                        return;
                    }

                    if (IsAtListItemStart())
                    {
                        if (para.Parent is ListItem li && li.Parent is List list)
                        {
                            if (list.Parent is ListItem)
                            {
                                _isUpdating = true;
                                try
                                {
                                    if (TryChangeListIndent(para, true))
                                    {
                                        e.Handled = true;
                                        return;
                                    }
                                }
                                finally
                                {
                                    _isUpdating = false;
                                    _debounceTimer.Stop();
                                    _debounceTimer.Start();
                                }
                            }
                            else
                            {
                                _isUpdating = true;
                                try
                                {
                                    ConvertListItemToParagraph(para, mergeWithPreviousIfParagraph: true);
                                    e.Handled = true;
                                    return;
                                }
                                finally
                                {
                                    _isUpdating = false;
                                    _debounceTimer.Stop();
                                    _debounceTimer.Start();
                                }
                            }
                        }
                    }
                }
            }
            else if (e.Key == Key.Delete)
            {
                var para = CaretPosition?.Paragraph;
                if (para != null && para.Parent is ListItem)
                {
                    if (IsAtListItemEnd())
                    {
                        _isUpdating = true;
                        try
                        {
                            if (TryDeleteMergeAtEnd(para))
                            {
                                e.Handled = true;
                                return;
                            }
                        }
                        finally
                        {
                            _isUpdating = false;
                            _debounceTimer.Stop();
                            _debounceTimer.Start();
                        }
                    }
                }
            }
            else if (e.Key == Key.Tab)
            {
                var para = CaretPosition?.Paragraph;
                if (para != null && TryChangeListIndent(para, Keyboard.Modifiers == ModifierKeys.Shift))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private bool TryContinueList(Paragraph currentPara)
        {
            if (currentPara.Parent is ListItem li && li.Parent is List list)
            {
                _isUpdating = true;
                try
                {
                    var caret = CaretPosition ?? currentPara.ContentEnd;
                    // If current list item is empty, convert to normal paragraph
                    var currentText = new TextRange(currentPara.ContentStart, currentPara.ContentEnd).Text;
                    if (string.IsNullOrWhiteSpace(currentText))
                    {
                        ConvertListItemToParagraph(currentPara, mergeWithPreviousIfParagraph: false);
                        return true;
                    }

                    // Create the new list item/paragraph first so we can paste content into it
                    var newPara = new Paragraph();
                    var newLi = new ListItem();
                    newLi.Blocks.Add(newPara);

                    // If caret is not at end, move trailing content (preserve formatting)
                    if (caret.CompareTo(currentPara.ContentEnd) < 0)
                    {
                        var trailing = new TextRange(caret, currentPara.ContentEnd);
                        if (!string.IsNullOrEmpty(trailing.Text))
                        {
                            using (var ms = new MemoryStream())
                            {
                                try
                                {
                                    trailing.Save(ms, DataFormats.XamlPackage);
                                    ms.Position = 0;
                                    var dest = new TextRange(newPara.ContentStart, newPara.ContentEnd);
                                    dest.Load(ms, DataFormats.XamlPackage);
                                }
                                catch
                                {
                                    // Fallback: plain text copy if rich copy fails
                                    newPara.Inlines.Add(new Run(trailing.Text));
                                }
                            }
                            // Remove trailing content from the current paragraph
                            try { trailing.Text = string.Empty; } catch { }
                        }
                    }

                    // Insert the new list item after the current one
                    int index = GetListItemIndex(list, li);
                    InsertListItemAt(list, index + 1, newLi);
                    CaretPosition = newPara.ContentStart;
                }
                finally
                {
                    _isUpdating = false;
                    _debounceTimer.Stop();
                    _debounceTimer.Start();
                }
                return true;
            }
            return false;
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
            if (para.Parent is ListItem li && li.Parent is List list)
            {
                _isUpdating = true;
                try
                {
                    int index = GetListItemIndex(list, li);
                    list.ListItems.Remove(li);
                    if (index > 0 && index - 1 < GetListItemCount(list))
                    {
                        var prev = GetListItemAt(list, index - 1);
                        var lastBlock = prev.Blocks.LastBlock as Paragraph;
                        if (lastBlock != null)
                        {
                            CaretPosition = lastBlock.ContentEnd;
                        }
                    }
                    else
                    {
                        CaretPosition = Document.ContentEnd;
                    }
                }
                finally
                {
                    _isUpdating = false;
                    _debounceTimer.Stop();
                    _debounceTimer.Start();
                }
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
            try
            {
                var range = new TextRange(para.ContentStart, CaretPosition);
                return string.IsNullOrEmpty(range.Text);
            }
            catch { return false; }
        }

        private bool IsAtListItemEnd()
        {
            var para = CaretPosition?.Paragraph;
            if (para?.Parent is not ListItem) return false;
            try
            {
                return CaretPosition?.CompareTo(para.ContentEnd) == 0;
            }
            catch { return false; }
        }

        private void ConvertListItemToParagraph(Paragraph para, bool mergeWithPreviousIfParagraph)
        {
            if (para?.Parent is not ListItem li || li.Parent is not List list) return;

            var parentBlocks = (list.Parent as FlowDocument)?.Blocks
                ?? (list.Parent as Section)?.Blocks
                ?? (list.Parent as ListItem)?.Blocks
                ?? Document.Blocks;

            int index = GetListItemIndex(list, li);
            int count = GetListItemCount(list);

            if (mergeWithPreviousIfParagraph && index == 0)
            {
                Block prevBlock = null;
                try { prevBlock = list.PreviousBlock; } catch { prevBlock = null; }
                if (prevBlock is Paragraph prevPara)
                {
                    MergeParagraphInto(para, prevPara);
                    try { list.ListItems.Remove(li); } catch { }
                    try { if (GetListItemCount(list) == 0) parentBlocks.Remove(list); } catch { }
                    try { CaretPosition = prevPara.ContentEnd; } catch { }
                    return;
                }
            }

            var newPara = new Paragraph();
            try
            {
                if (para.Inlines != null)
                {
                    var inlines = para.Inlines.ToList();
                    foreach (var inline in inlines)
                    {
                        try { para.Inlines.Remove(inline); } catch { }
                        newPara.Inlines.Add(inline);
                    }
                }
            }
            catch { }

            if (index <= 0)
            {
                try { parentBlocks.InsertBefore(list, newPara); }
                catch { try { parentBlocks.Add(newPara); } catch { } }
                try { list.ListItems.Remove(li); } catch { }
                try { if (GetListItemCount(list) == 0) parentBlocks.Remove(list); } catch { }
                try { CaretPosition = newPara.ContentStart; } catch { }
                return;
            }
            else if (index >= count - 1)
            {
                try { parentBlocks.InsertAfter(list, newPara); }
                catch { try { parentBlocks.Add(newPara); } catch { } }
                try { list.ListItems.Remove(li); } catch { }
                try { if (GetListItemCount(list) == 0) parentBlocks.Remove(list); } catch { }
                try { CaretPosition = newPara.ContentStart; } catch { }
                return;
            }
            else
            {
                var tail = new List { MarkerStyle = list.MarkerStyle, Margin = list.Margin };
                var itemsToMove = new List<ListItem>();
                int i = 0;
                foreach (ListItem it in list.ListItems)
                {
                    if (i > index) itemsToMove.Add(it);
                    i++;
                }
                foreach (var it in itemsToMove)
                {
                    try { list.ListItems.Remove(it); } catch { }
                    try { tail.ListItems.Add(it); } catch { }
                }

                try { parentBlocks.InsertAfter(list, newPara); }
                catch { try { parentBlocks.Add(newPara); } catch { } }
                try { parentBlocks.InsertAfter(newPara, tail); } catch { try { parentBlocks.Add(tail); } catch { } }
                try { list.ListItems.Remove(li); } catch { }
                try { if (GetListItemCount(list) == 0) parentBlocks.Remove(list); } catch { }
                try { CaretPosition = newPara.ContentStart; } catch { }
                return;
            }
        }

        private void MergeParagraphInto(Paragraph from, Paragraph to)
        {
            if (from == null || to == null) return;
            try
            {
                var inlines = from.Inlines?.ToList();
                if (inlines != null)
                {
                    foreach (var inline in inlines)
                    {
                        try { from.Inlines.Remove(inline); } catch { }
                        to.Inlines.Add(inline);
                    }
                }
            }
            catch { }
        }

        private bool TryDeleteMergeAtEnd(Paragraph para)
        {
            if (para?.Parent is not ListItem li || li.Parent is not List list) return false;

            int index = GetListItemIndex(list, li);
            int count = GetListItemCount(list);
            if (index >= 0 && index < count - 1)
            {
                var nextLi = GetListItemAt(list, index + 1);
                if (nextLi != null)
                {
                    var nextFirstPara = nextLi.Blocks.FirstBlock as Paragraph;
                    if (nextFirstPara != null)
                    {
                        MergeParagraphInto(nextFirstPara, para);
                    }
                    var remaining = nextLi.Blocks.ToList();
                    if (remaining.Count > 0 && remaining[0] is Paragraph) remaining.RemoveAt(0);
                    foreach (var b in remaining)
                    {
                        try { nextLi.Blocks.Remove(b); } catch { }
                        try { li.Blocks.Add(b); } catch { }
                    }
                    try { list.ListItems.Remove(nextLi); } catch { }
                    try { CaretPosition = para.ContentEnd; } catch { }
                    return true;
                }
            }

            var parentBlocks = (list.Parent as FlowDocument)?.Blocks
                ?? (list.Parent as Section)?.Blocks
                ?? (list.Parent as ListItem)?.Blocks
                ?? Document.Blocks;
            Block nextBlockOutsideList = null;
            try { nextBlockOutsideList = list.NextBlock; } catch { nextBlockOutsideList = null; }
            if (nextBlockOutsideList is Paragraph nextPara)
            {
                MergeParagraphInto(nextPara, para);
                try { parentBlocks.Remove(nextPara); } catch { }
                try { CaretPosition = para.ContentEnd; } catch { }
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


