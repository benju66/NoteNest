using NoteNest.Domain.Common;

namespace NoteNest.Application.FolderTags.Events;

/// <summary>
/// Domain event raised when tags are removed from a folder.
/// </summary>
public class FolderUntaggedEvent : IDomainEvent
{
    public Guid FolderId { get; }
    public List<string> RemovedTags { get; }
    public bool RemoveFromExistingItems { get; }
    public DateTime OccurredAt { get; }

    public FolderUntaggedEvent(Guid folderId, List<string> removedTags, bool removeFromExistingItems = false)
    {
        FolderId = folderId;
        RemovedTags = removedTags ?? new List<string>();
        RemoveFromExistingItems = removeFromExistingItems;
        OccurredAt = DateTime.UtcNow;
    }
}

