using NoteNest.Core.Models;
using NoteNest.UI.Controls.Editors;
using NoteNest.UI.Interfaces;

namespace NoteNest.UI.Services
{
	public interface IEditorFactory
	{
		ITextEditor CreateEditor(NoteFormat format, EditorViewMode viewMode);
		bool SupportsRichView(NoteFormat format);
	}

	public class EditorFactory : IEditorFactory
	{
		public ITextEditor CreateEditor(NoteFormat format, EditorViewMode viewMode)
		{
			// Rich view will be introduced later; for now return SmartTextEditor adapter
			var editor = new SmartTextEditorAdapter();
			editor.Format = format;
			editor.ViewMode = viewMode;
			return editor;
		}

		public bool SupportsRichView(NoteFormat format)
		{
			return format == NoteFormat.Markdown;
		}
	}
}


