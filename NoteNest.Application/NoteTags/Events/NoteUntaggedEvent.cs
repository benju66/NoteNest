using NoteNest.Domain.Common;

namespace NoteNest.Application.NoteTags.Events;

/// <summary>
/// Domain event raised when tags are removed from a note.
/// </summary>
public class NoteUntaggedEvent : IDomainEvent
{
    public Guid NoteId { get; }
    public List<string> RemovedTags { get; }
    public DateTime OccurredAt { get; }

    public NoteUntaggedEvent(Guid noteId, List<string> removedTags)
    {
        NoteId = noteId;
        RemovedTags = removedTags ?? new List<string>();
        OccurredAt = DateTime.UtcNow;
    }
}

