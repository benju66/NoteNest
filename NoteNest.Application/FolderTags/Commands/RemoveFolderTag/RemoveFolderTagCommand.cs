using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.FolderTags.Commands.RemoveFolderTag;

/// <summary>
/// Command to remove tags from a folder.
/// </summary>
public class RemoveFolderTagCommand : IRequest<Result<RemoveFolderTagResult>>
{
    public Guid FolderId { get; set; }
    public bool RemoveFromExistingItems { get; set; } = false;
}

/// <summary>
/// Result of removing folder tags.
/// </summary>
public class RemoveFolderTagResult
{
    public Guid FolderId { get; set; }
    public int TodosUpdated { get; set; }
}

