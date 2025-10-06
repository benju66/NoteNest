using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Notes.Commands.DeleteNote
{
    public class DeleteNoteCommand : IRequest<Result<DeleteNoteResult>>
    {
        public string NoteId { get; set; }
        public bool DeleteFile { get; set; } = true;
    }

    public class DeleteNoteResult
    {
        public bool Success { get; set; }
        public string DeletedNoteTitle { get; set; }
        public string DeletedFilePath { get; set; }
        public string Warning { get; set; } // For file deletion failures
    }
}
