using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Models;
using NoteNest.Core.Utils;

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
                
                // RTF-only: Get all .rtf files in the category
                var files = await _fileSystem.GetFilesAsync(category.Path, "*.rtf");

                foreach (var file in files)
                {
                    try
                    {
                        var note = await LoadNoteAsync(file);
                        note.Format = NoteFormat.RTF;  // RTF-only
                        note.CategoryId = category.Id;
                        notes.Add(note);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Error loading note {file}: {ex.Message}");
                    }
                }

                _logger.Debug($"Loaded {notes.Count} notes from category: {category.Name}");
                
                // Sort by position (with title fallback) for persistent ordering
                return await SortNotesByPositionAsync(notes);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get notes for category: {category.Name}");
                _notifications?.ShowErrorAsync($"Failed to load notes for category '{category.Name}'", ex);
                throw new InvalidOperationException($"Failed to load notes from category: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Sorts notes by position (if available) with title fallback
        /// Ensures consistent ordering while gracefully handling notes without positions
        /// </summary>
        private async Task<List<NoteModel>> SortNotesByPositionAsync(List<NoteModel> notes)
        {
            if (notes == null || notes.Count == 0)
                return notes ?? new List<NoteModel>();
                
            try
            {
                // If no metadata manager available, fall back to title sorting
                if (_metadataManager == null)
                {
                    _logger.Debug("No metadata manager available, sorting by title only");
                    return notes.OrderBy(n => n.Title).ToList();
                }
                
                // Load positions for all notes in parallel for performance
                var notesWithPositions = new List<(NoteModel note, int position)>();
                
                var positionTasks = notes.Select(async note =>
                {
                    var position = await note.GetPositionAsync(_metadataManager);
                    return (note, position);
                });
                
                var results = await Task.WhenAll(positionTasks);
                notesWithPositions.AddRange(results);
                
                // Sort by position first, then by title for consistent ordering
                // Notes with position 0 (no explicit position) will be sorted by title at the end
                var sortedNotes = notesWithPositions
                    .OrderBy(item => item.position == 0 ? int.MaxValue : item.position)  // Position 0 goes to end
                    .ThenBy(item => item.note.Title)  // Title fallback for same positions or no position
                    .Select(item => item.note)
                    .ToList();
                
                _logger.Debug($"Sorted {notes.Count} notes by position (metadata manager available)");
                return sortedNotes;
            }
            catch (Exception ex)
            {
                // If position sorting fails, fall back to title sorting to maintain functionality
                _logger.Warning($"Failed to sort notes by position, falling back to title sorting: {ex.Message}");
                return notes.OrderBy(n => n.Title).ToList();
            }
        }
    }
}


