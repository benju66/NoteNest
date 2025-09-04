using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Models;

namespace NoteNest.Core.Services
{
	public partial class NoteService
	{
		public async Task<bool> MoveNoteAsync(NoteModel note, CategoryModel targetCategory)
		{
			if (note == null || targetCategory == null)
				return false;

			try
			{
				var oldPath = note.FilePath;
				var fileName = Path.GetFileName(oldPath);
				var newPath = Path.Combine(targetCategory.Path, fileName);
				// Ensure target path remains within root
				var normalized = PathService.NormalizeAbsolutePath(newPath) ?? newPath;
				if (!PathService.IsUnderRoot(normalized))
				{
					_logger.Warning($"Attempt to move note outside root: {normalized}");
					return false;
				}

				// Ensure unique filename in target directory
				int counter = 1;
				var baseName = Path.GetFileNameWithoutExtension(fileName);
				var extension = Path.GetExtension(fileName);
				
				while (await _fileSystem.ExistsAsync(newPath))
				{
					fileName = $"{baseName}_{counter++}{extension}";
					newPath = Path.Combine(targetCategory.Path, fileName);
				}

				// Ensure target directory exists
				if (!await _fileSystem.ExistsAsync(targetCategory.Path))
				{
					await _fileSystem.CreateDirectoryAsync(targetCategory.Path);
				}

				// Move the file
				if (await _fileSystem.ExistsAsync(oldPath))
				{
					var content = await ReadFileTextAsync(oldPath);
					await WriteFileTextAsync(newPath, content);
					await _fileSystem.DeleteAsync(oldPath);
					
					// Update note model
					note.FilePath = newPath;
					note.CategoryId = targetCategory.Id;
					note.Title = Path.GetFileNameWithoutExtension(fileName);
					// Move metadata sidecar
					try { if (_metadataManager != null) await _metadataManager.MoveMetadataAsync(oldPath, newPath); } catch { }
					
					_logger.Info($"Moved note from {oldPath} to {newPath}");
					return true;
				}
				
				_logger.Warning($"Source file not found when moving note: {oldPath}");
				return false;
			}
			catch (Exception ex)
			{
				_logger.Error(ex, $"Failed to move note from {note.FilePath} to category {targetCategory.Name}");
				return false;
			}
		}

		public async Task<bool> ExportNoteAsync(NoteModel note, string exportPath, ExportFormat format = ExportFormat.Text)
		{
			if (note == null || string.IsNullOrEmpty(exportPath))
				return false;

			try
			{
				var content = note.Content ?? string.Empty;
				
				switch (format)
				{
					case ExportFormat.Text:
						await WriteFileTextAsync(exportPath, content);
						break;
					
					case ExportFormat.Markdown:
						var markdown = $"# {note.Title}\n\n*Last Modified: {note.LastModified:yyyy-MM-dd HH:mm}*\n\n{content}";
						await WriteFileTextAsync(exportPath, markdown);
						break;
					
					case ExportFormat.Html:
						var html = $@"<!DOCTYPE html>
<html>
<head>
    <title>{System.Web.HttpUtility.HtmlEncode(note.Title)}</title>
    <meta charset=""utf-8"">
</head>
<body>
    <h1>{System.Web.HttpUtility.HtmlEncode(note.Title)}</h1>
    <p><em>Last Modified: {note.LastModified:yyyy-MM-dd HH:mm}</em></p>
    <pre>{System.Web.HttpUtility.HtmlEncode(content)}</pre>
</body>
</html>";
						await WriteFileTextAsync(exportPath, html);
						break;
				}
				
				_logger.Info($"Exported note '{note.Title}' to {exportPath} as {format}");
				return true;
			}
			catch (Exception ex)
			{
				_logger.Error(ex, $"Failed to export note '{note.Title}' to {exportPath}");
				return false;
			}
		}
	}
}


