using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Domain.Common;
using NoteNest.Domain.Notes;
using NoteNest.Application.Common.Interfaces;

namespace NoteNest.Application.Notes.Commands.DeleteNote
{
    public class DeleteNoteHandler : IRequestHandler<DeleteNoteCommand, Result<DeleteNoteResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly IFileService _fileService;

        public DeleteNoteHandler(
            IEventStore eventStore,
            IFileService fileService)
        {
            _eventStore = eventStore;
            _fileService = fileService;
        }

        public async Task<Result<DeleteNoteResult>> Handle(DeleteNoteCommand request, CancellationToken cancellationToken)
        {
            // Load note from event store
            var noteId = NoteId.From(request.NoteId);
            var noteGuid = Guid.Parse(noteId.Value);
            var note = await _eventStore.LoadAsync<Note>(noteGuid);
            if (note == null)
                return Result.Fail<DeleteNoteResult>("Note not found");

            var noteTitle = note.Title;
            var filePath = note.FilePath;

            // Raise deletion event
            note.AddDomainEvent(new NoteNest.Domain.Notes.Events.NoteDeletedEvent(noteId, note.CategoryId));
            
            // Save to event store (persists deletion event)
            await _eventStore.SaveAsync(note);

            // Delete file if requested
            string fileDeleteWarning = null;
            if (request.DeleteFile && !string.IsNullOrEmpty(filePath))
            {
                try
                {
                    await _fileService.DeleteFileAsync(filePath);
                }
                catch (System.Exception ex)
                {
                    // Note deleted from event store but file deletion failed
                    System.Diagnostics.Debug.WriteLine($"Failed to delete file {filePath}: {ex.Message}");
                    fileDeleteWarning = $"Note removed but file could not be deleted: {ex.Message}. You may need to manually delete the file.";
                }
            }

            // Events automatically published to projections

            return Result.Ok(new DeleteNoteResult
            {
                Success = true,
                DeletedNoteTitle = noteTitle,
                DeletedFilePath = filePath,
                Warning = fileDeleteWarning
            });
        }
    }
}
