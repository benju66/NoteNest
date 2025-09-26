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
        private readonly INoteRepository _noteRepository;
        private readonly IFileService _fileService;
        private readonly IEventBus _eventBus;

        public SaveNoteHandler(
            INoteRepository noteRepository,
            IFileService fileService,
            IEventBus eventBus)
        {
            _noteRepository = noteRepository;
            _fileService = fileService;
            _eventBus = eventBus;
        }

        public async Task<Result<SaveNoteResult>> Handle(SaveNoteCommand request, CancellationToken cancellationToken)
        {
            // Get note from repository
            var noteId = NoteId.From(request.NoteId);
            var note = await _noteRepository.GetByIdAsync(noteId);
            if (note == null)
                return Result.Fail<SaveNoteResult>("Note not found");

            // Update content
            var updateResult = note.UpdateContent(request.Content);
            if (updateResult.IsFailure)
                return Result.Fail<SaveNoteResult>(updateResult.Error);

            // Save to repository
            var saveResult = await _noteRepository.UpdateAsync(note);
            if (saveResult.IsFailure)
                return Result.Fail<SaveNoteResult>(saveResult.Error);

            // Write to file system
            await _fileService.WriteNoteAsync(note.FilePath, request.Content);

            // Publish domain events
            foreach (var domainEvent in note.DomainEvents)
            {
                await _eventBus.PublishAsync(domainEvent);
            }
            note.ClearDomainEvents();

            return Result.Ok(new SaveNoteResult
            {
                Success = true,
                FilePath = note.FilePath,
                SavedAt = DateTime.UtcNow
            });
        }
    }
}
