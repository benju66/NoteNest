using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using NoteNest.UI.Services;
using System.Collections.Generic;
using System.Linq;

namespace NoteNest.UI.Controls
{
    public class FormattedTextEditor : RichTextBox
    {
        private bool _isUpdating;
        private readonly MarkdownFlowDocumentConverter _converter;
        private readonly DispatcherTimer _debounceTimer;

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
            Document = new FlowDocument();
            IsReadOnly = false;
            IsReadOnlyCaretVisible = true;
            Focusable = true;
            AcceptsReturn = true;
            AcceptsTab = true;
            FocusVisualStyle = null;

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
                if (para != null && IsEmptyListItem(para))
                {
                    e.Handled = true;
                    RemoveCurrentEmptyListItem(para);
                    return;
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
                    var newPara = new Paragraph();
                    var newLi = new ListItem();
                    newLi.Blocks.Add(newPara);
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
                    // Move this item after the parent list
                    var container = parentList.Parent as ListItem;
                    if (container?.Parent is List grandParentList)
                    {
                        parentList.ListItems.Remove(li);
                        int insertIndex = GetListItemIndex(grandParentList, container) + 1;
                        InsertListItemAt(grandParentList, insertIndex, li);
                        CaretPosition = (li.Blocks.FirstBlock as Paragraph)?.ContentStart ?? CaretPosition;
                        return true;
                    }
                }
                else
                {
                    // Indent: nest under previous sibling
                    int index = GetListItemIndex(parentList, li);
                    if (index > 0)
                    {
                        var prev = GetListItemAt(parentList, index - 1);
                        List nested;
                        if (prev.Blocks.LastBlock is List existing)
                        {
                            nested = existing;
                        }
                        else
                        {
                            nested = new List { MarkerStyle = parentList.MarkerStyle };
                            prev.Blocks.Add(nested);
                        }
                        RemoveListItemAt(parentList, index);
                        AddListItem(nested, li);
                        CaretPosition = (li.Blocks.FirstBlock as Paragraph)?.ContentStart ?? CaretPosition;
                        return true;
                    }
                }
            }
            finally
            {
                _isUpdating = false;
                _debounceTimer.Stop();
                _debounceTimer.Start();
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

                // Preserve caret and avoid replacing Document if possible
                var caret = CaretPosition;
                var flow = _converter.ConvertToFlowDocument(markdown, fontFamily, fontSize);
                Document = flow;
                try { CaretPosition = Document?.ContentEnd ?? caret; } catch { }
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
            }
            finally
            {
                _isUpdating = false;
            }
        }

        public void InsertBulletList()
        {
            WrapSelectionInList(TextMarkerStyle.Disc);
        }

        public void InsertNumberedList()
        {
            WrapSelectionInList(TextMarkerStyle.Decimal);
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

                var list = new List { MarkerStyle = markerStyle };
                foreach (var p in paragraphs)
                {
                    // Remove from its current parent collection
                    var pParentBlocks = (p.Parent as FlowDocument)?.Blocks
                        ?? (p.Parent as Section)?.Blocks
                        ?? (p.Parent as ListItem)?.Blocks
                        ?? Document.Blocks;
                    pParentBlocks.Remove(p);

                    var li = new ListItem();
                    li.Blocks.Add(p);
                    list.ListItems.Add(li);
                }

                parentCollection.InsertBefore(firstPara, list);
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


