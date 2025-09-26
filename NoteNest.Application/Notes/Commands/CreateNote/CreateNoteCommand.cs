using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Notes.Commands.CreateNote
{
    public class CreateNoteCommand : IRequest<Result<CreateNoteResult>>
    {
        public string CategoryId { get; set; }
        public string Title { get; set; }
        public string InitialContent { get; set; }
        public bool OpenInEditor { get; set; } = true;
    }

    public class CreateNoteResult
    {
        public string NoteId { get; set; }
        public string FilePath { get; set; }
        public string Title { get; set; }
    }
}
