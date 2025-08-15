using System;
using System.Threading.Tasks;
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
        
        public NoteOperationsService(
            NoteService noteService,
            IServiceErrorHandler errorHandler,
            IAppLogger logger)
        {
            _noteService = noteService ?? throw new ArgumentNullException(nameof(noteService));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task<NoteModel> CreateNoteAsync(CategoryModel category, string title, string content = "")
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        public async Task SaveNoteAsync(NoteModel note)
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        public async Task DeleteNoteAsync(NoteModel note)
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        public async Task<bool> RenameNoteAsync(NoteModel note, string newName)
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        public async Task<bool> MoveNoteAsync(NoteModel note, CategoryModel targetCategory)
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        public async Task SaveAllNotesAsync()
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
    }
}