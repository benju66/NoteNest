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
using NoteNest.Core.Services.Safety;
using NoteNest.Core.Services.Notes;
using NoteNest.Core.Events;
using NoteNest.Core.Interfaces.Services;

namespace NoteNest.Core.Services
{
    public partial class NoteService
    {
        private readonly IFileSystemProvider _fileSystem;
        private readonly ConfigurationService _configService;
        private readonly IAppLogger _logger;
        private readonly FileFormatService _formatService;
        internal readonly IEventBus? _eventBus;
        private readonly IMarkdownService _markdownService;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
        private readonly SafeFileService? _safeFileService;
        private readonly INoteStorageService? _noteStorage;
        private readonly IUserNotificationService? _notifications;
        private readonly NoteMetadataManager? _metadataManager;

        public NoteService(
            IFileSystemProvider fileSystem,
            ConfigurationService configService,
            IAppLogger? logger = null,
            IEventBus? eventBus = null,
            IMarkdownService? markdownService = null,
            SafeFileService? safeFileService = null,
            INoteStorageService? noteStorage = null,
            IUserNotificationService? notifications = null,
            NoteMetadataManager? metadataManager = null)
        {
            _fileSystem = fileSystem ?? new DefaultFileSystemProvider();
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = logger ?? AppLogger.Instance;
            _eventBus = eventBus;
            _formatService = new FileFormatService(_logger);
            _markdownService = markdownService ?? new MarkdownService(_logger);
            _safeFileService = safeFileService;
            _noteStorage = noteStorage;
            _notifications = notifications;
            _metadataManager = metadataManager;
            
            _jsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
            
            _logger.Debug("NoteService initialized");
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

                if (_noteStorage != null)
                {
                    return await _noteStorage.LoadAsync(filePath);
                }

                var content = _safeFileService != null
                    ? await _safeFileService.ReadTextSafelyAsync(filePath)
                    : await _fileSystem.ReadTextAsync(filePath);
                var fileInfo = await _fileSystem.GetFileInfoAsync(filePath);
                
                var note = new NoteModel
                {
                    Title = Path.GetFileNameWithoutExtension(filePath),
                    FilePath = filePath,
                    Content = content,
                    LastModified = fileInfo.LastWriteTime,
                    IsDirty = false
                };
                // Ensure stable Note Id via metadata sidecar
                try { if (_metadataManager != null) { await _metadataManager.GetOrCreateNoteIdAsync(note); } }
                catch { }
                // Detect and set format
                try
                {
                    note.Format = _formatService.DetectFormatFromPath(filePath);
                    if ((_configService.Settings?.AutoDetectFormat ?? true) && note.Format == NoteFormat.PlainText && !string.IsNullOrEmpty(content))
                    {
                        note.Format = _markdownService.DetectFormatFromContent(content);
                    }
                }
                catch { }
                
                _logger.Debug($"Loaded note: {note.Title}");
                return note;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to load note from: {filePath}");
                _notifications?.ShowErrorAsync($"Failed to load note: {Path.GetFileName(filePath)}", ex);
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

                // Optional conversion and sanitization
                var originalPath = note.FilePath;
                if ((_configService.Settings?.ConvertTxtToMdOnSave ?? false) && note.Format == NoteFormat.PlainText)
                {
                    note.Content = _markdownService.ConvertToMarkdown(note.Content ?? string.Empty);
                    note.Format = NoteFormat.Markdown;
                    var newPath = _markdownService.UpdateFileExtension(note.FilePath, NoteFormat.Markdown);
                    if (!string.Equals(newPath, note.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        note.FilePath = newPath;
                    }
                }

                if (note.Format == NoteFormat.Markdown && !string.IsNullOrEmpty(note.Content))
                {
                    note.Content = _markdownService.SanitizeMarkdown(note.Content);
                }

                if (_noteStorage != null)
                {
                    await _noteStorage.SaveAsync(note);
                }
                else
                {
                    await WriteNoteFileAsync(note);
                }
                
                // Update note state
                var previousModified = note.LastModified;
                note.LastModified = DateTime.Now;
                note.MarkClean();

                // If we changed the file path due to conversion, remove old file if it still exists
                try
                {
                    if (!string.Equals(originalPath, note.FilePath, StringComparison.OrdinalIgnoreCase) && await _fileSystem.ExistsAsync(originalPath))
                    {
                        await _fileSystem.DeleteAsync(originalPath);
                        // Move metadata sidecar if path changed
                        try { if (_metadataManager != null) await _metadataManager.MoveMetadataAsync(originalPath, note.FilePath); } catch { }
                    }
                }
                catch { }

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
                _notifications?.ShowErrorAsync($"Access denied when saving note '{note.Title}'", ex);
                throw new InvalidOperationException($"Access denied when saving note '{note.Title}'. Check file permissions.", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.Error(ex, $"Directory not found when saving note '{note.Title}' to: {note.FilePath}");
                _notifications?.ShowErrorAsync($"Directory not found when saving note '{note.Title}'", ex);
                throw new InvalidOperationException($"Directory not found when saving note '{note.Title}'. Path may be invalid.", ex);
            }
            catch (IOException ex)
            {
                _logger.Error(ex, $"I/O error when saving note '{note.Title}' to: {note.FilePath}");
                _notifications?.ShowErrorAsync($"I/O error when saving note '{note.Title}'", ex);
                throw new InvalidOperationException($"I/O error when saving note '{note.Title}'. File may be locked or disk full.", ex);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Unexpected error saving note '{note.Title}' to: {note.FilePath}");
                _notifications?.ShowErrorAsync($"Failed to save note '{note.Title}'", ex);
                throw new InvalidOperationException($"Failed to save note '{note.Title}': {ex.Message}", ex);
            }
        }

        

        

        

        public enum ExportFormat
        {
            Text,
            Markdown,
            Html
        }
    }
}