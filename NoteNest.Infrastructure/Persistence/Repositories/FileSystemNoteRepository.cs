using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Common;
using NoteNest.Domain.Notes;
using NoteNest.Domain.Categories;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces;

namespace NoteNest.Infrastructure.Persistence.Repositories
{
    public class FileSystemNoteRepository : INoteRepository
    {
        private readonly IFileSystemProvider _fileSystem;
        private readonly IAppLogger _logger;
        private readonly string _metadataPath;

        public FileSystemNoteRepository(IFileSystemProvider fileSystem, IAppLogger logger, IConfiguration config)
        {
            _fileSystem = fileSystem;
            _logger = logger;
            _metadataPath = config.GetValue<string>("MetadataPath") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NoteNest", "Metadata");
            
            Directory.CreateDirectory(_metadataPath);
        }

        public async Task<Note> GetByIdAsync(NoteId id)
        {
            try
            {
                var metadataFile = Path.Combine(_metadataPath, $"{id.Value}.json");
                if (!File.Exists(metadataFile))
                    return null;

                var json = await File.ReadAllTextAsync(metadataFile);
                var metadata = JsonSerializer.Deserialize<NoteMetadata>(json);
                
                return MapToDomain(metadata);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get note {id.Value}");
                return null;
            }
        }

        public async Task<IReadOnlyList<Note>> GetByCategoryAsync(CategoryId categoryId)
        {
            try
            {
                var metadataFiles = Directory.GetFiles(_metadataPath, "*.json");
                var notes = new List<Note>();

                foreach (var file in metadataFiles)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var metadata = JsonSerializer.Deserialize<NoteMetadata>(json);
                        
                        if (metadata.CategoryId == categoryId.Value)
                        {
                            notes.Add(MapToDomain(metadata));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to read metadata file {file}: {ex.Message}");
                    }
                }

                return notes.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get notes for category {categoryId.Value}");
                return new List<Note>().AsReadOnly();
            }
        }

        public async Task<IReadOnlyList<Note>> GetPinnedAsync()
        {
            try
            {
                var metadataFiles = Directory.GetFiles(_metadataPath, "*.json");
                var pinnedNotes = new List<Note>();

                foreach (var file in metadataFiles)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var metadata = JsonSerializer.Deserialize<NoteMetadata>(json);
                        
                        if (metadata.IsPinned)
                        {
                            pinnedNotes.Add(MapToDomain(metadata));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to read metadata file {file}: {ex.Message}");
                    }
                }

                return pinnedNotes.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get pinned notes");
                return new List<Note>().AsReadOnly();
            }
        }

        public async Task<Result> CreateAsync(Note note)
        {
            try
            {
                var metadata = MapToMetadata(note);
                var metadataFile = Path.Combine(_metadataPath, $"{note.Id.Value}.json");
                
                var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(metadataFile, json);
                
                _logger.Debug($"Created note metadata: {note.Id.Value}");
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create note {note.Id.Value}");
                return Result.Fail($"Failed to create note: {ex.Message}");
            }
        }

        public async Task<Result> UpdateAsync(Note note)
        {
            try
            {
                var metadata = MapToMetadata(note);
                var metadataFile = Path.Combine(_metadataPath, $"{note.Id.Value}.json");
                
                var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(metadataFile, json);
                
                _logger.Debug($"Updated note metadata: {note.Id.Value}");
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to update note {note.Id.Value}");
                return Result.Fail($"Failed to update note: {ex.Message}");
            }
        }

        public async Task<Result> DeleteAsync(NoteId id)
        {
            try
            {
                var metadataFile = Path.Combine(_metadataPath, $"{id.Value}.json");
                
                if (File.Exists(metadataFile))
                {
                    File.Delete(metadataFile);
                    _logger.Debug($"Deleted note metadata: {id.Value}");
                }
                
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to delete note {id.Value}");
                return Result.Fail($"Failed to delete note: {ex.Message}");
            }
        }

        public async Task<bool> ExistsAsync(NoteId id)
        {
            var metadataFile = Path.Combine(_metadataPath, $"{id.Value}.json");
            return File.Exists(metadataFile);
        }

        public async Task<bool> TitleExistsInCategoryAsync(CategoryId categoryId, string title, NoteId excludeId = null)
        {
            var notesInCategory = await GetByCategoryAsync(categoryId);
            return notesInCategory.Any(n => 
                n.Title.Equals(title, StringComparison.OrdinalIgnoreCase) && 
                (excludeId == null || !n.Id.Equals(excludeId)));
        }

        private Note MapToDomain(NoteMetadata metadata)
        {
            var note = new Note(CategoryId.From(metadata.CategoryId), metadata.Title, metadata.Content);
            
            // Use reflection to set private fields (temporary approach)
            var idField = typeof(Note).GetField("<Id>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            idField?.SetValue(note, NoteId.From(metadata.Id));
            
            note.SetFilePath(metadata.FilePath);
            note.SetPosition(metadata.Position);
            
            if (metadata.IsPinned)
                note.Pin();
                
            return note;
        }

        private NoteMetadata MapToMetadata(Note note)
        {
            return new NoteMetadata
            {
                Id = note.Id.Value,
                CategoryId = note.CategoryId.Value,
                Title = note.Title,
                Content = note.Content,
                FilePath = note.FilePath,
                IsPinned = note.IsPinned,
                Position = note.Position,
                CreatedAt = note.CreatedAt,
                UpdatedAt = note.UpdatedAt
            };
        }
    }

    public class NoteMetadata
    {
        public string Id { get; set; }
        public string CategoryId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string FilePath { get; set; }
        public bool IsPinned { get; set; }
        public int Position { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
