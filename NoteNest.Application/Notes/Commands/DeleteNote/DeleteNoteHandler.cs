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
        private readonly INoteRepository _noteRepository;
        private readonly IFileService _fileService;
        private readonly IEventBus _eventBus;

        public DeleteNoteHandler(
            INoteRepository noteRepository,
            IFileService fileService,
            IEventBus eventBus)
        {
            _noteRepository = noteRepository;
            _fileService = fileService;
            _eventBus = eventBus;
        }

        public async Task<Result<DeleteNoteResult>> Handle(DeleteNoteCommand request, CancellationToken cancellationToken)
        {
            // Get note from repository
            var noteId = NoteId.From(request.NoteId);
            var note = await _noteRepository.GetByIdAsync(noteId);
            if (note == null)
                return Result.Fail<DeleteNoteResult>("Note not found");

            var noteTitle = note.Title;
            var filePath = note.FilePath;

            // Delete from repository
            var deleteResult = await _noteRepository.DeleteAsync(noteId);
            if (deleteResult.IsFailure)
                return Result.Fail<DeleteNoteResult>(deleteResult.Error);

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
                    // Log but don't fail - metadata is already deleted
                    // Could implement a cleanup service for orphaned files
                    System.Diagnostics.Debug.WriteLine($"Failed to delete file {filePath}: {ex.Message}");
                    fileDeleteWarning = $"Note removed from database but file could not be deleted: {ex.Message}. You may need to manually delete the file.";
                }
            }

            // Publish domain events
            foreach (var domainEvent in note.DomainEvents)
            {
                await _eventBus.PublishAsync(domainEvent);
            }

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
