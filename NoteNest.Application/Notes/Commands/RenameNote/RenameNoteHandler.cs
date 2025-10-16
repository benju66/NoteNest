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
        private readonly IFileService _fileService;

        public RenameNoteHandler(
            IEventStore eventStore,
            IFileService fileService)
        {
            _eventStore = eventStore;
            _fileService = fileService;
        }

        public async Task<Result<RenameNoteResult>> Handle(RenameNoteCommand request, CancellationToken cancellationToken)
        {
            // Load note from event store
            var noteId = NoteId.From(request.NoteId);
            var noteGuid = Guid.Parse(noteId.Value);
            var note = await _eventStore.LoadAsync<Note>(noteGuid);
            if (note == null)
                return Result.Fail<RenameNoteResult>("Note not found");

            var oldTitle = note.Title;
            var oldFilePath = note.FilePath;

            // Rename note (domain logic handles validation)
            var renameResult = note.Rename(request.NewTitle);
            if (renameResult.IsFailure)
                return Result.Fail<RenameNoteResult>(renameResult.Error);

            // Generate new file path if needed
            string newFilePath = oldFilePath;
            if (request.UpdateFilePath && !string.IsNullOrEmpty(oldFilePath))
            {
                var directory = System.IO.Path.GetDirectoryName(oldFilePath);
                newFilePath = _fileService.GenerateNoteFilePath(directory, request.NewTitle);
                note.SetFilePath(newFilePath);
            }

            // Save to event store (persists rename event)
            await _eventStore.SaveAsync(note);

            // Move file if path changed
            if (request.UpdateFilePath && oldFilePath != newFilePath)
            {
                try
                {
                    await _fileService.MoveFileAsync(oldFilePath, newFilePath);
                }
                catch (System.Exception ex)
                {
                    // File move failed but event is persisted
                    // TODO: Implement compensating event or manual file sync
                    return Result.Fail<RenameNoteResult>($"Failed to rename file: {ex.Message}");
                }
            }

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
