using System;
using System.IO;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Services.Safety;

namespace NoteNest.Core.Services.Notes
{
	public interface INoteStorageService
	{
		Task<NoteModel> LoadAsync(string filePath);
		Task SaveAsync(NoteModel note);
		Task DeleteAsync(string filePath);
		Task<bool> ExistsAsync(string filePath);
	}

	public class NoteStorageService : INoteStorageService
	{
		private readonly IFileSystemProvider _fileSystem;
		private readonly SafeFileService? _safeFileService;
		private readonly IAppLogger? _logger;

		public NoteStorageService(
			IFileSystemProvider fileSystem,
			SafeFileService? safeFileService = null,
			IAppLogger? logger = null)
		{
			_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
			_safeFileService = safeFileService;
			_logger = logger ?? AppLogger.Instance;
		}

		public async Task<NoteModel> LoadAsync(string filePath)
		{
			if (!await _fileSystem.ExistsAsync(filePath))
				throw new FileNotFoundException($"Note file not found: {filePath}");

			var content = _safeFileService != null
				? await _safeFileService.ReadTextSafelyAsync(filePath)
				: await _fileSystem.ReadTextAsync(filePath);
			var fileInfo = await _fileSystem.GetFileInfoAsync(filePath);

			return new NoteModel
			{
				Title = Path.GetFileNameWithoutExtension(filePath),
				FilePath = filePath,
				Content = content,
				LastModified = fileInfo.LastWriteTime,
				IsDirty = false
			};
		}

		public async Task SaveAsync(NoteModel note)
		{
			if (note == null) throw new ArgumentNullException(nameof(note));
			var directory = Path.GetDirectoryName(note.FilePath) ?? string.Empty;
			if (!await _fileSystem.ExistsAsync(directory))
			{
				await _fileSystem.CreateDirectoryAsync(directory);
			}

			if (_safeFileService != null)
			{
				await _safeFileService.WriteTextSafelyAsync(note.FilePath, note.Content ?? string.Empty);
			}
			else
			{
				await _fileSystem.WriteTextAsync(note.FilePath, note.Content ?? string.Empty);
			}

			note.LastModified = DateTime.Now;
			_logger?.Info($"Note saved: {note.FilePath}");
		}

		public async Task DeleteAsync(string filePath)
		{
			if (await _fileSystem.ExistsAsync(filePath))
			{
				await _fileSystem.DeleteAsync(filePath);
				_logger?.Info($"Deleted note file: {filePath}");
			}
		}

		public async Task<bool> ExistsAsync(string filePath)
		{
			return await _fileSystem.ExistsAsync(filePath);
		}
	}
}


