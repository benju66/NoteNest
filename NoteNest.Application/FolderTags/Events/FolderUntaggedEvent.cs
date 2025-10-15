using NoteNest.Domain.Common;

namespace NoteNest.Application.FolderTags.Events;

/// <summary>
/// Domain event raised when tags are removed from a folder.
/// Existing items keep their tags (not retroactively removed).
/// </summary>
public class FolderUntaggedEvent : IDomainEvent
{
    public Guid FolderId { get; }
    public List<string> RemovedTags { get; }
    public DateTime OccurredAt { get; }

    public FolderUntaggedEvent(Guid folderId, List<string> removedTags)
    {
        FolderId = folderId;
        RemovedTags = removedTags ?? new List<string>();
        OccurredAt = DateTime.UtcNow;
    }
}

