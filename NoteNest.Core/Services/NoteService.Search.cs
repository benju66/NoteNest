using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Models;

namespace NoteNest.Core.Services
{
	public partial class NoteService
	{
		public async Task<List<NoteModel>> SearchNotesAsync(string query)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(query))
					return new List<NoteModel>();

				var allNotes = await GetAllNotesAsync();
				return allNotes
					.Where(n =>
						(n.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
						(n.Content?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
					.OrderByDescending(n => n.LastModified)
					.ToList();
			}
			catch (Exception ex)
			{
				_logger?.Error(ex, $"Search failed for query: {query}");
				return new List<NoteModel>();
			}
		}

		private async Task<List<NoteModel>> GetAllNotesAsync()
		{
			var notes = new List<NoteModel>();
			var notesRoot = _configService.Settings?.DefaultNotePath;
			if (string.IsNullOrWhiteSpace(notesRoot))
			{
				notesRoot = PathService.RootPath;
			}

			if (!await _fileSystem.ExistsAsync(notesRoot))
				return notes;

			var mdFiles = await _fileSystem.GetFilesAsync(notesRoot, "*.md");
			var txtFiles = await _fileSystem.GetFilesAsync(notesRoot, "*.txt");
			var files = mdFiles.Concat(txtFiles);

			foreach (var file in files)
			{
				try
				{
					var note = await LoadNoteAsync(file);
					if (note != null)
						notes.Add(note);
				}
				catch
				{
					// Skip individual failures but continue loading others
				}
			}

			return notes;
		}
	}
}


