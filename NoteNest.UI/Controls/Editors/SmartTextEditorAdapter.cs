using System;
using System.Windows;
using System.Windows.Controls;
using NoteNest.Core.Models;
using NoteNest.UI.Interfaces;

namespace NoteNest.UI.Controls.Editors
{
	public class SmartTextEditorAdapter : UserControl, ITextEditor
	{
		private readonly SmartTextEditor _innerEditor;
		private NoteFormat _format = NoteFormat.PlainText;
		private EditorViewMode _viewMode = EditorViewMode.PlainText;

		public SmartTextEditorAdapter()
		{
			_innerEditor = new SmartTextEditor();
			Content = _innerEditor;

			_innerEditor.TextChanged += (s, e) =>
			{
				ContentChanged?.Invoke(this, new NoteNest.UI.Interfaces.TextChangedEventArgs
				{
					OldContent = null,
					NewContent = _innerEditor.Text
				});
			};
		}

		public string PlainTextContent
		{
			get => _innerEditor.Text;
			set => _innerEditor.Text = value;
		}

		public NoteFormat Format
		{
			get => _format;
			set
			{
				_format = value;
				_innerEditor.UpdateFormatSettings(value);
			}
		}

		public EditorViewMode ViewMode
		{
			get => _viewMode;
			set
			{
				var old = _viewMode;
				_viewMode = value;
				ViewModeChanged?.Invoke(this, new ViewModeChangedEventArgs { OldMode = old, NewMode = value });
			}
		}

		public bool IsDirty => _innerEditor.IsModified;

		public bool IsReadOnly
		{
			get => _innerEditor.IsReadOnly;
			set => _innerEditor.IsReadOnly = value;
		}

		public event EventHandler<NoteNest.UI.Interfaces.TextChangedEventArgs> ContentChanged;
		public event EventHandler<ViewModeChangedEventArgs> ViewModeChanged;

		public void SetContent(string content, NoteFormat format)
		{
			Format = format;
			SetContentPreserveCaret(content ?? string.Empty);
		}

		private void SetContentPreserveCaret(string newText)
		{
			var oldText = _innerEditor.Text ?? string.Empty;
			if (string.Equals(oldText, newText, StringComparison.Ordinal)) return;
			var oldCaret = _innerEditor.CaretIndex;
			// Compute old line and column
			var prefix = oldText.AsSpan(0, Math.Min(oldCaret, oldText.Length));
			int oldLine = 0;
			for (int i = 0; i < prefix.Length; i++) if (prefix[i] == '\n') oldLine++;
			int oldLineStart = prefix.LastIndexOf('\n');
			if (oldLineStart < 0) oldLineStart = 0; else oldLineStart += 1;
			int oldColumn = oldCaret - oldLineStart;

			_innerEditor.Text = newText;

			// Find start of same line number in new text
			int lineStart = 0;
			int lineCount = 0;
			for (int i = 0; i < _innerEditor.Text.Length && lineCount < oldLine; i++)
			{
				if (_innerEditor.Text[i] == '\n')
				{
					lineStart = i + 1;
					lineCount++;
				}
			}
			int lineEnd = _innerEditor.Text.IndexOf('\n', lineStart);
			if (lineEnd < 0) lineEnd = _innerEditor.Text.Length;
			int target = Math.Min(lineStart + Math.Max(0, oldColumn), lineEnd);
			_innerEditor.CaretIndex = Math.Min(target, _innerEditor.Text.Length);
		}

		public string GetContent() => PlainTextContent;

		public void Cut() => _innerEditor.Cut();
		public void Copy() => _innerEditor.Copy();
		public void Paste() => _innerEditor.Paste();
		public void Undo() => _innerEditor.Undo();
		public void Redo() => _innerEditor.Redo();
		public bool CanUndo() => _innerEditor.CanUndo;
		public bool CanRedo() => _innerEditor.CanRedo;
		public new void Focus() => _innerEditor.Focus();

		public void ToggleBold() { }
		public void ToggleItalic() { }
		public void ToggleStrikethrough() { }
		public void InsertHeader(int level) { }
		public void InsertList(ListType listType)
		{
			switch (listType)
			{
				case ListType.Bullet:
					_innerEditor.ConvertSelectionToBullets();
					break;
				case ListType.Numbered:
					_innerEditor.ConvertSelectionToNumbers();
					break;
				case ListType.Task:
					_innerEditor.ConvertSelectionToTasks();
					break;
			}
		}
		public void ToggleTaskComplete() => _innerEditor.ToggleTaskComplete();

		public void Dispose()
		{
			// no-op; relying on GC; event handlers are anonymous and tied to lifetime of this control
		}
	}
}


