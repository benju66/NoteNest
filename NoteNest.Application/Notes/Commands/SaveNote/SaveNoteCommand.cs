using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Notes.Commands.SaveNote
{
    public class SaveNoteCommand : IRequest<Result<SaveNoteResult>>
    {
        public string NoteId { get; set; }
        public string Content { get; set; }
        public bool IsManualSave { get; set; } = true;
    }

    public class SaveNoteResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; }
        public DateTime SavedAt { get; set; }
    }
}
