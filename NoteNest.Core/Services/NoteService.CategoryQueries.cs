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
        public async Task<List<NoteModel>> GetNotesInCategoryAsync(CategoryModel category)
        {
            if (category == null)
                throw new ArgumentNullException(nameof(category));

            try
            {
                if (!await _fileSystem.ExistsAsync(category.Path))
                {
                    _logger.Warning($"Category path does not exist: {category.Path}");
                    return new List<NoteModel>();
                }

                var notes = new List<NoteModel>();
                
                // Use dynamic file discovery - future-proof for any supported format
                var files = await _formatService.GetAllNoteFilesAsync(_fileSystem, category.Path);

                foreach (var file in files)
                {
                    try
                    {
                        var note = await LoadNoteAsync(file);
                        note.Format = _formatService.DetectFormatFromPath(file);
                        note.CategoryId = category.Id;
                        notes.Add(note);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Error loading note {file}: {ex.Message}");
                    }
                }

                _logger.Debug($"Loaded {notes.Count} notes from category: {category.Name}");
                return notes.OrderBy(n => n.Title).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get notes for category: {category.Name}");
                _notifications?.ShowErrorAsync($"Failed to load notes for category '{category.Name}'", ex);
                throw new InvalidOperationException($"Failed to load notes from category: {ex.Message}", ex);
            }
        }
    }
}


