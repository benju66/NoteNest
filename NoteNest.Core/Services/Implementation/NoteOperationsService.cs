using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
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
                // Perform the actual save operation (event-driven cache/config will react)
                await _noteService.SaveNoteAsync(note);
                
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
                var result = await _noteService.RenameNoteAsync(note, newName);
                if (result)
                {
                    _logger.Info($"Renamed note to '{newName}'");
                }
                return result;
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
        // Removed SaveAllNotesAsync: batch saving now handled by WorkspaceService or WorkspaceStateService
        
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