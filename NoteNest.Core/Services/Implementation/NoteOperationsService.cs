using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Implementation
{
    public class NoteOperationsService : INoteOperationsService
    {
        private readonly NoteService _noteService;
        private readonly IServiceErrorHandler _errorHandler;
        private readonly IAppLogger _logger;
        private readonly IFileSystemProvider _fileSystem;
        private readonly ConfigurationService _configService;
        private readonly ContentCache _contentCache;
        
        // Store references to open notes for SaveAll functionality
        private readonly List<NoteModel> _openNotes = new List<NoteModel>();
        
        public NoteOperationsService(
            NoteService noteService,
            IServiceErrorHandler errorHandler,
            IAppLogger logger,
            IFileSystemProvider fileSystem,
            ConfigurationService configService,
            ContentCache contentCache)
        {
            _noteService = noteService ?? throw new ArgumentNullException(nameof(noteService));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _contentCache = contentCache ?? throw new ArgumentNullException(nameof(contentCache));
            
            _logger.Debug("NoteOperationsService initialized");
        }
        
        public async Task<NoteModel> CreateNoteAsync(CategoryModel category, string title, string content = "")
        {
            if (category == null)
                throw new ArgumentNullException(nameof(category));
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Note title cannot be empty.", nameof(title));
            
            return await _errorHandler.SafeExecuteAsync(async () =>
            {
                var note = await _noteService.CreateNoteAsync(category, title, content ?? string.Empty);
                
                _logger.Info($"Created new note: {title} in category: {category.Name}");
                return note;
            }, "Create Note");
        }
        
        public async Task SaveNoteAsync(NoteModel note)
        {
            if (note == null)
                throw new ArgumentNullException(nameof(note));
            
            await _errorHandler.SafeExecuteAsync(async () =>
            {
                // Perform the actual save operation
                await _noteService.SaveNoteAsync(note);
                
                // CRITICAL: Invalidate content cache after successful save to prevent stale data
                _contentCache.InvalidateEntry(note.FilePath);
                _logger.Debug($"Invalidated content cache for: {note.FilePath}");
                
                // Handle recent files with proper error handling (don't fail save for this)
                try
                {
                    _configService.AddRecentFile(note.FilePath);
                    await _configService.SaveSettingsAsync();
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to update recent files for: {note.Title}. Error: {ex.Message}");
                    // Continue - don't fail the save operation for recent files tracking
                }
                
                _logger.Info($"Successfully saved note: {note.Title}");
            }, "Save Note");
        }
        
        public async Task DeleteNoteAsync(NoteModel note)
        {
            if (note == null)
                throw new ArgumentNullException(nameof(note));
            
            await _errorHandler.SafeExecuteAsync(async () =>
            {
                await _noteService.DeleteNoteAsync(note);
                
                // Remove from open notes tracking
                _openNotes.Remove(note);
                
                _logger.Info($"Deleted note: {note.Title}");
            }, "Delete Note");
        }
        
        public async Task<bool> RenameNoteAsync(NoteModel note, string newName)
        {
            if (note == null)
                throw new ArgumentNullException(nameof(note));
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("New name cannot be empty.", nameof(newName));
            
            return await _errorHandler.SafeExecuteAsync(async () =>
            {
                var oldPath = note.FilePath;
                var directory = Path.GetDirectoryName(oldPath);
                var newFileName = PathService.SanitizeName(newName) + ".txt";
                var newPath = Path.Combine(directory, newFileName);
                
                // Check if file already exists
                if (await _fileSystem.ExistsAsync(newPath) && newPath != oldPath)
                {
                    _logger.Warning($"Cannot rename - file already exists: {newPath}");
                    return false;
                }
                
                // Rename physical file
                if (await _fileSystem.ExistsAsync(oldPath))
                {
                    // Use file system operations to rename
                    // Note: IFileSystemProvider might need a MoveAsync method
                    if (File.Exists(oldPath))
                    {
                        File.Move(oldPath, newPath);
                    }
                }
                
                // Update note model
                note.Title = newName;
                note.FilePath = newPath;
                
                _logger.Info($"Renamed note from '{Path.GetFileName(oldPath)}' to '{newFileName}'");
                return true;
            }, "Rename Note");
        }
        
        public async Task<bool> MoveNoteAsync(NoteModel note, CategoryModel targetCategory)
        {
            if (note == null)
                throw new ArgumentNullException(nameof(note));
            if (targetCategory == null)
                throw new ArgumentNullException(nameof(targetCategory));
            
            return await _errorHandler.SafeExecuteAsync(async () =>
            {
                var success = await _noteService.MoveNoteAsync(note, targetCategory);
                
                if (success)
                {
                    _logger.Info($"Moved note '{note.Title}' to category '{targetCategory.Name}'");
                }
                
                return success;
            }, "Move Note");
        }
        
        public async Task SaveAllNotesAsync()
        {
            await _errorHandler.SafeExecuteAsync(async () =>
            {
                var saveCount = 0;
                var errorCount = 0;
                var notes = _openNotes.ToList(); // Create a copy to avoid modification during iteration
                
                foreach (var note in notes)
                {
                    if (note.IsDirty)
                    {
                        try
                        {
                            await _noteService.SaveNoteAsync(note);
                            
                            // CRITICAL: Invalidate content cache after successful save
                            _contentCache.InvalidateEntry(note.FilePath);
                            
                            saveCount++;
                            _logger.Debug($"Saved and invalidated cache for: {note.Title}");
                        }
                        catch (Exception ex)
                        {
                            errorCount++;
                            _logger.Error(ex, $"Failed to save note during SaveAll: {note.Title}");
                            // Continue with other notes
                        }
                    }
                }
                
                if (saveCount > 0)
                {
                    try
                    {
                        await _configService.SaveSettingsAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to save configuration after SaveAll. Error: {ex.Message}");
                    }
                    
                    if (errorCount > 0)
                    {
                        _logger.Warning($"SaveAll completed with {saveCount} successes and {errorCount} errors");
                    }
                    else
                    {
                        _logger.Info($"Successfully saved all {saveCount} notes");
                    }
                }
            }, "Save All Notes");
        }
        
        // Helper methods for tracking open notes
        public void TrackOpenNote(NoteModel note)
        {
            if (note != null && !_openNotes.Contains(note))
            {
                _openNotes.Add(note);
            }
        }
        
        public void UntrackOpenNote(NoteModel note)
        {
            if (note != null)
            {
                _openNotes.Remove(note);
            }
        }
        
        public void ClearTrackedNotes()
        {
            _openNotes.Clear();
        }
    }
}