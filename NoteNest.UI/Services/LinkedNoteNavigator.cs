using System;
using System.Threading.Tasks;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Interfaces.Services;

namespace NoteNest.UI.Services
{
	public class LinkedNoteNavigator
	{
		private readonly IWorkspaceService _workspaceService;
		private readonly NoteService _noteService;
		private readonly IWorkspaceStateService _stateService;

		public LinkedNoteNavigator(
			IWorkspaceService workspaceService,
			NoteService noteService,
			IWorkspaceStateService stateService)
		{
			_workspaceService = workspaceService;
			_noteService = noteService;
			_stateService = stateService;
		}

		public async Task<bool> OpenByIdOrPathAsync(string noteId, string fallbackFilePath, int? lineNumber = null)
		{
			try
			{
				NoteModel note = null;
				if (!string.IsNullOrWhiteSpace(noteId))
				{
					if (_stateService.OpenNotes.TryGetValue(noteId, out var wn) && wn?.Model != null)
					{
						note = wn.Model;
					}
				}

				if (note == null && !string.IsNullOrWhiteSpace(fallbackFilePath))
				{
					note = await _noteService.LoadNoteAsync(fallbackFilePath);
				}

				if (note == null) return false;

				await _workspaceService.OpenNoteAsync(note);
				// Positioning can be handled by UI subscribers later if needed
				return true;
			}
			catch
			{
				return false;
			}
		}
	}
}


