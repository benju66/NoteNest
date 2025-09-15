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
                // Get user's preferred format from settings
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

                // Create initial content based on format
                string initialContent = format switch
                {
                    NoteFormat.RTF => @"{\rtf1\ansi\deff0 {\fonttbl {\f0 Calibri;}}{\colortbl ;}{\*\generator NoteNest;}\f0\fs24\par}",
                    NoteFormat.PlainText => content ?? "",
                    _ => content ?? "" // Markdown starts with provided content or empty
                };

                var note = new NoteModel
                {
                    Title = title,
                    FilePath = filePath,
                    Content = initialContent,
                    CategoryId = category.Id,
                    LastModified = DateTime.Now,
                    Format = format // Set the format
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


