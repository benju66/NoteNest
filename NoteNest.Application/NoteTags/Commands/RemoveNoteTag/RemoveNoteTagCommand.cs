using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.NoteTags.Commands.RemoveNoteTag;

/// <summary>
/// Command to remove tags from a note.
/// </summary>
public class RemoveNoteTagCommand : IRequest<Result<RemoveNoteTagResult>>
{
    public Guid NoteId { get; set; }
}

/// <summary>
/// Result of removing note tags.
/// </summary>
public class RemoveNoteTagResult
{
    public Guid NoteId { get; set; }
    public bool Success { get; set; }
}

