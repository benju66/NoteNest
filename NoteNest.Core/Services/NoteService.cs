using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Models;

namespace NoteNest.Core.Services
{
    public class NoteService
    {
        private readonly IFileSystemProvider _fileSystem;
        private readonly ConfigurationService _configService;
        private readonly JsonSerializerOptions _jsonOptions;

        public NoteService(IFileSystemProvider fileSystem, ConfigurationService configService)
        {
            _fileSystem = fileSystem ?? new DefaultFileSystemProvider();
            _configService = configService;
            _jsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<NoteModel> CreateNoteAsync(CategoryModel category, string title, string content)
        {
            if (category == null)
                throw new ArgumentNullException(nameof(category));

            var fileName = SanitizeFileName(title) + ".txt";
            var filePath = Path.Combine(category.Path, fileName);
            
            // Ensure unique filename
            int counter = 1;
            while (await _fileSystem.ExistsAsync(filePath))
            {
                fileName = $"{SanitizeFileName(title)}_{counter++}.txt";
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
            return note;
        }

        public async Task<NoteModel> LoadNoteAsync(string filePath)
        {
            if (!await _fileSystem.ExistsAsync(filePath))
                throw new FileNotFoundException($"Note file not found: {filePath}");

            var content = await _fileSystem.ReadTextAsync(filePath);
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

        public async Task SaveNoteAsync(NoteModel note)
        {
            if (note == null)
                throw new ArgumentNullException(nameof(note));

            // Ensure directory exists
            var directory = Path.GetDirectoryName(note.FilePath);
            if (!await _fileSystem.ExistsAsync(directory))
            {
                await _fileSystem.CreateDirectoryAsync(directory);
            }

            await _fileSystem.WriteTextAsync(note.FilePath, note.Content ?? string.Empty);
            note.LastModified = DateTime.Now;
            note.MarkClean();
        }

        public async Task DeleteNoteAsync(NoteModel note)
        {
            if (note == null)
                throw new ArgumentNullException(nameof(note));

            if (await _fileSystem.ExistsAsync(note.FilePath))
            {
                await _fileSystem.DeleteAsync(note.FilePath);
            }
        }

        public async Task<List<CategoryModel>> LoadCategoriesAsync(string metadataPath)
        {
            var categoriesFile = Path.Combine(metadataPath, "categories.json");
            
            if (!await _fileSystem.ExistsAsync(categoriesFile))
                return new List<CategoryModel>();

            var json = await _fileSystem.ReadTextAsync(categoriesFile);
            var wrapper = JsonSerializer.Deserialize<CategoryWrapper>(json, _jsonOptions);
            return wrapper?.Categories ?? new List<CategoryModel>();
        }

        public async Task SaveCategoriesAsync(string metadataPath, List<CategoryModel> categories)
        {
            var categoriesFile = Path.Combine(metadataPath, "categories.json");
            
            // Ensure directory exists
            if (!await _fileSystem.ExistsAsync(metadataPath))
            {
                await _fileSystem.CreateDirectoryAsync(metadataPath);
            }

            var wrapper = new CategoryWrapper { Categories = categories };
            var json = JsonSerializer.Serialize(wrapper, _jsonOptions);
            await _fileSystem.WriteTextAsync(categoriesFile, json);
        }

        public async Task<List<NoteModel>> GetNotesInCategoryAsync(CategoryModel category)
        {
            if (!await _fileSystem.ExistsAsync(category.Path))
                return new List<NoteModel>();

            var notes = new List<NoteModel>();
            var files = await _fileSystem.GetFilesAsync(category.Path, "*.txt");
            
            foreach (var file in files)
            {
                try
                {
                    var note = await LoadNoteAsync(file);
                    note.CategoryId = category.Id;
                    notes.Add(note);
                }
                catch (Exception ex)
                {
                    // Log error but continue loading other notes
                    System.Diagnostics.Debug.WriteLine($"Error loading note {file}: {ex.Message}");
                }
            }

            return notes.OrderBy(n => n.Title).ToList();
        }

        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(fileName.Where(ch => !invalidChars.Contains(ch)).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized.Trim();
        }

        private class CategoryWrapper
        {
            public List<CategoryModel> Categories { get; set; }
            public AppSettings Settings { get; set; }
        }
    }
}
