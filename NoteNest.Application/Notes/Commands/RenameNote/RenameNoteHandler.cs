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
        private readonly INoteRepository _noteRepository;
        private readonly IFileService _fileService;
        private readonly IEventBus _eventBus;

        public RenameNoteHandler(
            INoteRepository noteRepository,
            IFileService fileService,
            IEventBus eventBus)
        {
            _noteRepository = noteRepository;
            _fileService = fileService;
            _eventBus = eventBus;
        }

        public async Task<Result<RenameNoteResult>> Handle(RenameNoteCommand request, CancellationToken cancellationToken)
        {
            // Get note from repository
            var noteId = NoteId.From(request.NoteId);
            var note = await _noteRepository.GetByIdAsync(noteId);
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

            // Update repository
            var updateResult = await _noteRepository.UpdateAsync(note);
            if (updateResult.IsFailure)
                return Result.Fail<RenameNoteResult>(updateResult.Error);

            // Move file if path changed
            if (request.UpdateFilePath && oldFilePath != newFilePath)
            {
                try
                {
                    await _fileService.MoveFileAsync(oldFilePath, newFilePath);
                }
                catch (System.Exception ex)
                {
                    // Rollback the note rename if file move fails
                    note.Rename(oldTitle);
                    await _noteRepository.UpdateAsync(note);
                    return Result.Fail<RenameNoteResult>($"Failed to rename file: {ex.Message}");
                }
            }

            // Publish domain events
            foreach (var domainEvent in note.DomainEvents)
            {
                await _eventBus.PublishAsync(domainEvent);
            }
            note.ClearDomainEvents();

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
