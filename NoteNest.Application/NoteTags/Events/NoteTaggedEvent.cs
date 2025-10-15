using NoteNest.Domain.Common;

namespace NoteNest.Application.NoteTags.Events;

/// <summary>
/// Domain event raised when tags are set on a note.
/// </summary>
public class NoteTaggedEvent : IDomainEvent
{
    public Guid NoteId { get; }
    public List<string> Tags { get; }
    public DateTime OccurredAt { get; }

    public NoteTaggedEvent(Guid noteId, List<string> tags)
    {
        NoteId = noteId;
        Tags = tags ?? new List<string>();
        OccurredAt = DateTime.UtcNow;
    }
}

