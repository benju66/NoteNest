using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Domain.Common;
using NoteNest.Domain.Notes;
using NoteNest.Application.Common.Interfaces;

namespace NoteNest.Application.Notes.Commands.RenameNote
{
    public class RenameNoteHandler : IRequestHandler<RenameNoteCommand, Result<RenameNoteResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly INoteRepository _noteRepository;
        private readonly IFileService _fileService;

        public RenameNoteHandler(
            IEventStore eventStore,
            INoteRepository noteRepository,
            IFileService fileService)
        {
            _eventStore = eventStore;
            _noteRepository = noteRepository;
            _fileService = fileService;
        }

        public async Task<Result<RenameNoteResult>> Handle(RenameNoteCommand request, CancellationToken cancellationToken)
        {
            // Load note aggregate from event store (for business logic)
            var noteId = NoteId.From(request.NoteId);
            var noteGuid = Guid.Parse(noteId.Value);
            var note = await _eventStore.LoadAsync<Note>(noteGuid);
            if (note == null)
                return Result.Fail<RenameNoteResult>("Note not found");

            // CQRS Pattern: Get FilePath from projection (infrastructure detail)
            var noteProjection = await _noteRepository.GetByIdAsync(noteId);
            if (noteProjection == null || string.IsNullOrEmpty(noteProjection.FilePath))
                return Result.Fail<RenameNoteResult>("Note file path not found in projection");

            var oldTitle = note.Title;
            var oldFilePath = noteProjection.FilePath;

            // Rename note (domain logic handles validation - prepares event but doesn't persist)
            var renameResult = note.Rename(request.NewTitle);
            if (renameResult.IsFailure)
                return Result.Fail<RenameNoteResult>(renameResult.Error);

            // Generate new file path if needed
            string newFilePath = oldFilePath;
            if (request.UpdateFilePath && !string.IsNullOrEmpty(oldFilePath))
            {
                var directory = System.IO.Path.GetDirectoryName(oldFilePath);
                newFilePath = _fileService.GenerateNoteFilePath(directory, request.NewTitle);
            }

            // CRITICAL: Move file BEFORE persisting event (prevents split-brain state)
            if (request.UpdateFilePath && oldFilePath != newFilePath)
            {
                // Verify source file exists
                if (!await _fileService.FileExistsAsync(oldFilePath))
                {
                    return Result.Fail<RenameNoteResult>($"Source file not found: {oldFilePath}");
                }
                
                try
                {
                    await _fileService.MoveFileAsync(oldFilePath, newFilePath);
                    // Update FilePath only after successful file move
                    note.SetFilePath(newFilePath);
                }
                catch (System.Exception ex)
                {
                    // File move failed - event NOT persisted, no split-brain state
                    return Result.Fail<RenameNoteResult>($"Failed to rename file: {ex.Message}");
                }
            }

            // Save to event store ONLY if file operation succeeded (atomic consistency)
            await _eventStore.SaveAsync(note);

            // Events automatically published to projections and UI

            return Result.Ok(new RenameNoteResult
            {
                Success = true,
                OldTitle = oldTitle,
                NewTitle = note.Title,
                OldFilePath = oldFilePath,
                NewFilePath = newFilePath
            });
        }
    }
}
