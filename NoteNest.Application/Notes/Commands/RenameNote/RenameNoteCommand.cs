using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Notes.Commands.RenameNote
{
    public class RenameNoteCommand : IRequest<Result<RenameNoteResult>>
    {
        public string NoteId { get; set; }
        public string NewTitle { get; set; }
        public bool UpdateFilePath { get; set; } = true;
    }

    public class RenameNoteResult
    {
        public bool Success { get; set; }
        public string OldTitle { get; set; }
        public string NewTitle { get; set; }
        public string OldFilePath { get; set; }
        public string NewFilePath { get; set; }
    }
}
