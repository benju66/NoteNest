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

		public LinkedNoteNavigator(
			IWorkspaceService workspaceService,
			NoteService noteService,
			object stateService = null) // Legacy parameter for compatibility
		{
			_workspaceService = workspaceService;
			_noteService = noteService;
		}

		public async Task<bool> OpenByIdOrPathAsync(string noteId, string fallbackFilePath, int? lineNumber = null)
		{
			try
			{
				NoteModel note = null;
				// Try to find already open tab by file path first
				if (!string.IsNullOrWhiteSpace(fallbackFilePath))
				{
					var existingTab = _workspaceService.FindTabByPath(fallbackFilePath);
					if (existingTab != null)
					{
						note = existingTab.Note;
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


