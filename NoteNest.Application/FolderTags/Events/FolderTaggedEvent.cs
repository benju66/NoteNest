using NoteNest.Domain.Common;

namespace NoteNest.Application.FolderTags.Events;

/// <summary>
/// Domain event raised when tags are set on a folder.
/// Tags apply to NEW items only via natural inheritance.
/// </summary>
public class FolderTaggedEvent : IDomainEvent
{
    public Guid FolderId { get; }
    public List<string> Tags { get; }
    public DateTime OccurredAt { get; }

    public FolderTaggedEvent(Guid folderId, List<string> tags)
    {
        FolderId = folderId;
        Tags = tags ?? new List<string>();
        OccurredAt = DateTime.UtcNow;
    }
}

