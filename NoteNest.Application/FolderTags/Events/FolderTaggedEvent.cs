using NoteNest.Domain.Common;

namespace NoteNest.Application.FolderTags.Events;

/// <summary>
/// Domain event raised when tags are set on a folder.
/// </summary>
public class FolderTaggedEvent : IDomainEvent
{
    public Guid FolderId { get; }
    public List<string> Tags { get; }
    public bool ApplyToExistingItems { get; }
    public DateTime OccurredAt { get; }

    public FolderTaggedEvent(Guid folderId, List<string> tags, bool applyToExistingItems = false)
    {
        FolderId = folderId;
        Tags = tags ?? new List<string>();
        ApplyToExistingItems = applyToExistingItems;
        OccurredAt = DateTime.UtcNow;
    }
}

