using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Domain.Common;
using NoteNest.Domain.Notes;
using NoteNest.Application.Common.Interfaces;

namespace NoteNest.Application.Notes.Commands.SaveNote
{
    public class SaveNoteHandler : IRequestHandler<SaveNoteCommand, Result<SaveNoteResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly IFileService _fileService;

        public SaveNoteHandler(
            IEventStore eventStore,
            IFileService fileService)
        {
            _eventStore = eventStore;
            _fileService = fileService;
        }

        public async Task<Result<SaveNoteResult>> Handle(SaveNoteCommand request, CancellationToken cancellationToken)
        {
            // Load note from event store
            var noteId = NoteId.From(request.NoteId);
            var noteGuid = Guid.Parse(noteId.Value);
            var note = await _eventStore.LoadAsync<Note>(noteGuid);
            if (note == null)
                return Result.Fail<SaveNoteResult>("Note not found");

            // Update content
            var updateResult = note.UpdateContent(request.Content);
            if (updateResult.IsFailure)
                return Result.Fail<SaveNoteResult>(updateResult.Error);

            // Save to event store (persists events + updates projections)
            await _eventStore.SaveAsync(note);

            // Write to file system (RTF files remain source of truth)
            await _fileService.WriteNoteAsync(note.FilePath, request.Content);

            // Events automatically published for UI updates and projections

            return Result.Ok(new SaveNoteResult
            {
                Success = true,
                FilePath = note.FilePath,
                SavedAt = DateTime.UtcNow
            });
        }
    }
}
