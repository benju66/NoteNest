using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.FolderTags.Commands.SetFolderTag;

/// <summary>
/// Command to set tags on a folder.
/// </summary>
public class SetFolderTagCommand : IRequest<Result<SetFolderTagResult>>
{
    public Guid FolderId { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool ApplyToExistingItems { get; set; } = false;
    public bool InheritToChildren { get; set; } = true;
    public bool IsAutoSuggested { get; set; } = false;
}

/// <summary>
/// Result of setting folder tags.
/// </summary>
public class SetFolderTagResult
{
    public Guid FolderId { get; set; }
    public List<string> AppliedTags { get; set; } = new();
    public int TodosUpdated { get; set; }
}

