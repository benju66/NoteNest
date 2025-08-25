using System;
using System.IO;
using System.Threading.Tasks;
using NoteNest.Core.Models;

namespace NoteNest.Core.Services
{
    public partial class NoteService
    {
        public async Task<NoteModel> CreateNoteAsync(CategoryModel category, string title, string content)
        {
            if (category == null)
                throw new ArgumentNullException(nameof(category));
            if (string.IsNullOrWhiteSpace(title) || !PathService.IsValidFileName(title))
                throw new ArgumentException("Invalid note title.", nameof(title));

            try
            {
                NoteFormat format = _configService.Settings?.DefaultNoteFormat ?? NoteFormat.Markdown;

                var extension = _formatService.GetExtensionForFormat(format);
                var fileName = SanitizeFileName(title) + extension;
                var filePath = Path.Combine(category.Path, fileName);
                var normalized = PathService.NormalizeAbsolutePath(filePath) ?? filePath;
                if (!PathService.IsUnderRoot(normalized))
                {
                    _logger.Warning($"Attempt to create note outside root: {normalized}");
                    throw new InvalidOperationException("Cannot create note outside of workspace root.");
                }
                int counter = 1;
                while (await _fileSystem.ExistsAsync(filePath))
                {
                    fileName = $"{SanitizeFileName(title)}_{counter++}{extension}";
                    filePath = Path.Combine(category.Path, fileName);
                }

                var note = new NoteModel
                {
                    Title = title,
                    FilePath = filePath,
                    Content = content,
                    CategoryId = category.Id,
                    LastModified = DateTime.Now
                };

                await SaveNoteAsync(note);
                _logger.Info($"Created new note: {title} at {filePath}");
                return note;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create note: {title} in category: {category.Name}");
                _notifications?.ShowErrorAsync($"Failed to create note '{title}'", ex);
                throw new InvalidOperationException($"Failed to create note: {ex.Message}", ex);
            }
        }
    }
}


