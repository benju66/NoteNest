using System;
using System.IO;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Models;

namespace NoteNest.Core.Services
{
	public class FormatMarkerService
	{
		private const string MarkerFileName = ".notenest-format";
		private readonly IFileSystemProvider _fileSystem;

		public FormatMarkerService(IFileSystemProvider fileSystem)
		{
			_fileSystem = fileSystem;
		}

		public async Task SetFolderFormat(string folderPath, NoteFormat format)
		{
			if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentException("folderPath is required", nameof(folderPath));
			var markerPath = Path.Combine(folderPath, MarkerFileName);
			if (!await _fileSystem.ExistsAsync(folderPath))
			{
				await _fileSystem.CreateDirectoryAsync(folderPath);
			}
			await _fileSystem.WriteTextAsync(markerPath, format.ToString());
		}

		public async Task<NoteFormat?> GetFolderFormat(string folderPath)
		{
			if (string.IsNullOrWhiteSpace(folderPath)) return null;
			var markerPath = Path.Combine(folderPath, MarkerFileName);
			if (!await _fileSystem.ExistsAsync(markerPath)) return null;
			var content = await _fileSystem.ReadTextAsync(markerPath);
			if (Enum.TryParse<NoteFormat>(content, ignoreCase: true, out var format))
			{
				return format;
			}
			return null;
		}
	}
}


