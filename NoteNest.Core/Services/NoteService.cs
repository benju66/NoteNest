using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Events;

namespace NoteNest.Core.Services
{
    public class NoteService
    {
        private readonly IFileSystemProvider _fileSystem;
        private readonly ConfigurationService _configService;
        private readonly IAppLogger _logger;
        private readonly IEventBus? _eventBus;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();

        public NoteService(IFileSystemProvider fileSystem, ConfigurationService configService, IAppLogger? logger = null, IEventBus? eventBus = null)
        {
            _fileSystem = fileSystem ?? new DefaultFileSystemProvider();
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = logger ?? AppLogger.Instance;
            _eventBus = eventBus;
            
            _jsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
            
            _logger.Debug("NoteService initialized");
        }

        public async Task<NoteModel> CreateNoteAsync(CategoryModel category, string title, string content)
        {
            if (category == null)
                throw new ArgumentNullException(nameof(category));

            try
            {
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
                _logger.Info($"Created new note: {title} at {filePath}");
                
                return note;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create note: {title} in category: {category.Name}");
                throw new InvalidOperationException($"Failed to create note: {ex.Message}", ex);
            }
        }

        public async Task<NoteModel> LoadNoteAsync(string filePath)
        {
            try
            {
                if (!await _fileSystem.ExistsAsync(filePath))
                {
                    _logger.Warning($"Note file not found: {filePath}");
                    throw new FileNotFoundException($"Note file not found: {filePath}");
                }

                var content = await _fileSystem.ReadTextAsync(filePath);
                var fileInfo = await _fileSystem.GetFileInfoAsync(filePath);
                
                var note = new NoteModel
                {
                    Title = Path.GetFileNameWithoutExtension(filePath),
                    FilePath = filePath,
                    Content = content,
                    LastModified = fileInfo.LastWriteTime,
                    IsDirty = false
                };
                
                _logger.Debug($"Loaded note: {note.Title}");
                return note;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to load note from: {filePath}");
                throw new InvalidOperationException($"Failed to load note: {ex.Message}", ex);
            }
        }

        public async Task SaveNoteAsync(NoteModel note)
        {
            if (note == null)
                throw new ArgumentNullException(nameof(note));

            try
            {
                var contentLength = note.Content?.Length ?? 0;
                _logger.Debug($"Saving note '{note.Title}' ({contentLength} characters) to: {note.FilePath}");

                // Ensure directory exists
                var directory = Path.GetDirectoryName(note.FilePath) ?? string.Empty;
                if (!await _fileSystem.ExistsAsync(directory))
                {
                    await _fileSystem.CreateDirectoryAsync(directory);
                    _logger.Info($"Created directory for note save: {directory}");
                }

                // Atomic write with per-file lock
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
                
                // Update note state
                var previousModified = note.LastModified;
                note.LastModified = DateTime.Now;
                note.MarkClean();

                // Publish event for listeners (e.g., cache invalidation, recent files)
                if (_eventBus != null)
                {
                    try
                    {
                        await _eventBus.PublishAsync(new NoteSavedEvent { FilePath = note.FilePath, SavedAt = note.LastModified });
                    }
                    catch { }
                }
                
                _logger.Info($"Successfully saved note '{note.Title}' ({contentLength} chars) at {note.LastModified:yyyy-MM-dd HH:mm:ss}");
                if (previousModified != default)
                {
                    _logger.Debug($"Note last modified changed from {previousModified:yyyy-MM-dd HH:mm:ss} to {note.LastModified:yyyy-MM-dd HH:mm:ss}");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Error(ex, $"Access denied when saving note '{note.Title}' to: {note.FilePath}");
                throw new InvalidOperationException($"Access denied when saving note '{note.Title}'. Check file permissions.", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.Error(ex, $"Directory not found when saving note '{note.Title}' to: {note.FilePath}");
                throw new InvalidOperationException($"Directory not found when saving note '{note.Title}'. Path may be invalid.", ex);
            }
            catch (IOException ex)
            {
                _logger.Error(ex, $"I/O error when saving note '{note.Title}' to: {note.FilePath}");
                throw new InvalidOperationException($"I/O error when saving note '{note.Title}'. File may be locked or disk full.", ex);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Unexpected error saving note '{note.Title}' to: {note.FilePath}");
                throw new InvalidOperationException($"Failed to save note '{note.Title}': {ex.Message}", ex);
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

        public async Task<List<CategoryModel>> LoadCategoriesAsync(string metadataPath)
        {
            try
            {
                var categoriesFile = PathService.CategoriesPath;
                
                if (!await _fileSystem.ExistsAsync(categoriesFile))
                {
                    _logger.Info("Categories file not found, returning empty list");
                    return new List<CategoryModel>();
                }

                var json = await _fileSystem.ReadTextAsync(categoriesFile);
                var wrapper = JsonSerializer.Deserialize<CategoryWrapper>(json, _jsonOptions);
                
                if (wrapper?.Categories != null)
                {
                    // Convert stored relative paths to absolute paths
                    foreach (var category in wrapper.Categories)
                    {
                        var originalPath = category.Path;
                        category.Path = PathService.ToAbsolutePath(category.Path);
                        _logger.Debug($"Loaded category '{category.Name}': {originalPath} -> {category.Path}");
                    }
                    
                    _logger.Info($"Loaded {wrapper.Categories.Count} categories from disk");
                    return wrapper.Categories;
                }
                
                _logger.Warning("Categories file exists but contains no categories");
                return new List<CategoryModel>();
            }
            catch (JsonException jex)
            {
                _logger.Error(jex, "Failed to parse categories JSON file");
                throw new InvalidOperationException("Categories file is corrupted or invalid format", jex);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load categories from disk");
                throw new InvalidOperationException("Failed to load categories. Check log for details.", ex);
            }
        }

        public async Task SaveCategoriesAsync(string metadataPath, List<CategoryModel> categories)
        {
            try
            {
                var categoriesFile = PathService.CategoriesPath;
                
                // Ensure directory exists
                var dir = Path.GetDirectoryName(categoriesFile) ?? string.Empty;
                if (!await _fileSystem.ExistsAsync(dir))
                {
                    await _fileSystem.CreateDirectoryAsync(dir);
                    _logger.Debug($"Created metadata directory: {dir}");
                }

                // Convert absolute paths to relative paths for storage
                var categoriesForStorage = categories.Select(c => new CategoryModel
                {
                    Id = c.Id,
                    ParentId = c.ParentId,
                    Name = c.Name,
                    Path = PathService.ToRelativePath(c.Path), // Store as relative
                    Pinned = c.Pinned,
                    Tags = c.Tags ?? new List<string>(),
                    Level = c.Level
                }).ToList();

                var wrapper = new CategoryWrapper 
                { 
                    Categories = categoriesForStorage, 
                    Version = "2.0" 
                };
                
                var json = JsonSerializer.Serialize(wrapper, _jsonOptions);
                await _fileSystem.WriteTextAsync(categoriesFile, json);
                
                _logger.Info($"Saved {categories.Count} categories to disk");
                
                // Log each category for debugging
                foreach (var cat in categoriesForStorage)
                {
                    _logger.Debug($"Saved category '{cat.Name}' with path: {cat.Path}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save categories to disk");
                throw new InvalidOperationException("Failed to save categories. Check log for details.", ex);
            }
        }

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
                        _logger.Warning($"Error loading note {file}: {ex.Message}");
                    }
                }

                _logger.Debug($"Loaded {notes.Count} notes from category: {category.Name}");
                return notes.OrderBy(n => n.Title).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get notes for category: {category.Name}");
                throw new InvalidOperationException($"Failed to load notes from category: {ex.Message}", ex);
            }
        }

        public async Task<bool> MoveNoteAsync(NoteModel note, CategoryModel targetCategory)
        {
            if (note == null || targetCategory == null)
                return false;

            try
            {
                var oldPath = note.FilePath;
                var fileName = Path.GetFileName(oldPath);
                var newPath = Path.Combine(targetCategory.Path, fileName);

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
                    var content = await _fileSystem.ReadTextAsync(oldPath);
                    await _fileSystem.WriteTextAsync(newPath, content);
                    await _fileSystem.DeleteAsync(oldPath);
                    
                    // Update note model
                    note.FilePath = newPath;
                    note.CategoryId = targetCategory.Id;
                    note.Title = Path.GetFileNameWithoutExtension(fileName);
                    
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
                        await _fileSystem.WriteTextAsync(exportPath, content);
                        break;
                    
                    case ExportFormat.Markdown:
                        var markdown = $"# {note.Title}\n\n*Last Modified: {note.LastModified:yyyy-MM-dd HH:mm}*\n\n{content}";
                        await _fileSystem.WriteTextAsync(exportPath, markdown);
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
                        await _fileSystem.WriteTextAsync(exportPath, html);
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

        private string SanitizeFileName(string fileName)
        {
            return PathService.SanitizeName(fileName);
        }

        private class CategoryWrapper
        {
            public List<CategoryModel> Categories { get; set; } = new();
            public string Version { get; set; } = "2.0";
            public AppSettings? Settings { get; set; }
        }

        public enum ExportFormat
        {
            Text,
            Markdown,
            Html
        }
    }
}