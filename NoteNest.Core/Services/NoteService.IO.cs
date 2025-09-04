using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Models;

namespace NoteNest.Core.Services
{
	public partial class NoteService
	{
		private async Task<string> ReadFileTextAsync(string filePath)
		{
			if (_safeFileService != null)
			{
				return await _safeFileService.ReadTextSafelyAsync(filePath);
			}
			return await _fileSystem.ReadTextAsync(filePath);
		}

		private async Task WriteNoteFileAsync(NoteModel note)
		{
			// Ensure directory exists
			var directory = Path.GetDirectoryName(note.FilePath) ?? string.Empty;
			if (!await _fileSystem.ExistsAsync(directory))
			{
				await _fileSystem.CreateDirectoryAsync(directory);
				_logger.Info($"Created directory for note save: {directory}");
			}

			if (_safeFileService != null)
			{
				await _safeFileService.WriteTextSafelyAsync(note.FilePath, note.Content ?? string.Empty);
				return;
			}

			// Legacy atomic write with per-file lock
			var fileLock = _fileLocks.GetOrAdd(note.FilePath, _ => new SemaphoreSlim(1, 1));
			await fileLock.WaitAsync();
			try
			{
				var tempPath = note.FilePath + ".tmp";
				var backupPath = note.FilePath + ".bak";

				// Write to temp first
				await _fileSystem.WriteTextAsync(tempPath, note.Content ?? string.Empty);

				// Replace atomically when available
				try
				{
					await _fileSystem.ReplaceAsync(tempPath, note.FilePath, backupPath);
				}
				catch
				{
					// Fallback: delete then move
					if (await _fileSystem.ExistsAsync(note.FilePath))
					{
						await _fileSystem.DeleteAsync(note.FilePath);
					}
					await _fileSystem.MoveAsync(tempPath, note.FilePath, overwrite: false);
				}
			}
			finally
			{
				fileLock.Release();
			}
		}

		private async Task WriteFileTextAsync(string filePath, string content)
		{
			var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
			if (!await _fileSystem.ExistsAsync(directory))
			{
				await _fileSystem.CreateDirectoryAsync(directory);
			}

			if (_safeFileService != null)
			{
				await _safeFileService.WriteTextSafelyAsync(filePath, content ?? string.Empty);
			}
			else
			{
				await _fileSystem.WriteTextAsync(filePath, content ?? string.Empty);
			}
		}

		public async Task DeleteNoteAsync(NoteModel note)
		{
			if (note == null)
				throw new ArgumentNullException(nameof(note));

			try
			{
				if (await _fileSystem.ExistsAsync(note.FilePath))
				{
					await _fileSystem.DeleteAsync(note.FilePath);
					_logger.Info($"Deleted note: {note.Title} from {note.FilePath}");
					// Publish delete event
					if (_eventBus != null)
					{
						try
						{
							await _eventBus.PublishAsync(new NoteNest.Core.Events.NoteDeletedEvent
							{
								NoteId = note.Id,
								FilePath = note.FilePath,
								DeletedAt = DateTime.UtcNow
							});
						}
						catch { }
					}
				}
				else
				{
					_logger.Warning($"Attempted to delete non-existent note: {note.FilePath}");
				}
			}
			catch (Exception ex)
			{
				_logger.Error(ex, $"Failed to delete note: {note.Title}");
				throw new InvalidOperationException($"Failed to delete note: {ex.Message}", ex);
			}
		}
	}
}


