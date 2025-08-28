using System;
using NoteNest.Core.Models;

namespace NoteNest.UI.Interfaces
{
	public enum EditorViewMode
	{
		PlainText,
		RichText
	}

	public interface ITextEditor : IDisposable
	{
		string PlainTextContent { get; set; }
		NoteFormat Format { get; set; }
		EditorViewMode ViewMode { get; set; }
		bool IsDirty { get; }
		bool IsReadOnly { get; set; }

		event EventHandler<TextChangedEventArgs> ContentChanged;
		event EventHandler<ViewModeChangedEventArgs> ViewModeChanged;

		void SetContent(string content, NoteFormat format);
		string GetContent();
		void Cut();
		void Copy();
		void Paste();
		void Undo();
		void Redo();
		bool CanUndo();
		bool CanRedo();
		void Focus();

		void ToggleBold();
		void ToggleItalic();
		void ToggleStrikethrough();
		void InsertHeader(int level);
		void InsertList(ListType listType);
		void ToggleTaskComplete();
	}

	public class TextChangedEventArgs : EventArgs
	{
		public string OldContent { get; set; }
		public string NewContent { get; set; }
	}

	public class ViewModeChangedEventArgs : EventArgs
	{
		public EditorViewMode OldMode { get; set; }
		public EditorViewMode NewMode { get; set; }
	}

	public enum ListType
	{
		Bullet,
		Numbered,
		Task
	}
}


