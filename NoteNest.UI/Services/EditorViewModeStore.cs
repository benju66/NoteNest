using System.Collections.Concurrent;
using NoteNest.UI.Interfaces;

namespace NoteNest.UI.Services
{
	public static class EditorViewModeStore
	{
		private static readonly ConcurrentDictionary<string, EditorViewMode> _byNoteId = new();

		public static EditorViewMode GetForNote(string noteId, EditorViewMode fallback = EditorViewMode.PlainText)
		{
			if (string.IsNullOrEmpty(noteId)) return fallback;
			return _byNoteId.TryGetValue(noteId, out var mode) ? mode : fallback;
		}

		public static void SetForNote(string noteId, EditorViewMode mode)
		{
			if (string.IsNullOrEmpty(noteId)) return;
			_byNoteId[noteId] = mode;
		}
	}
}


