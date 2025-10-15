using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.FolderTags.Commands.RemoveFolderTag;

/// <summary>
/// Command to remove tags from a folder.
/// Existing items keep their tags (tags are not retroactively removed).
/// </summary>
public class RemoveFolderTagCommand : IRequest<Result<RemoveFolderTagResult>>
{
    public Guid FolderId { get; set; }
}

/// <summary>
/// Result of removing folder tags.
/// </summary>
public class RemoveFolderTagResult
{
    public Guid FolderId { get; set; }
    public bool Success { get; set; }
}

