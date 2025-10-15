using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.NoteTags.Commands.SetNoteTag;

/// <summary>
/// Command to set tags on a note.
/// Tags are user-managed, not automatically inherited.
/// </summary>
public class SetNoteTagCommand : IRequest<Result<SetNoteTagResult>>
{
    public Guid NoteId { get; set; }
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Result of setting note tags.
/// </summary>
public class SetNoteTagResult
{
    public Guid NoteId { get; set; }
    public List<string> AppliedTags { get; set; } = new();
    public bool Success { get; set; }
}

