namespace NoteNest.Application.FolderTags.Models;

/// <summary>
/// Represents a tag assigned to a folder in the tree.
/// </summary>
public class FolderTag
{
    /// <summary>
    /// The folder (TreeNode) ID that this tag is assigned to.
    /// </summary>
    public Guid FolderId { get; set; }
    
    /// <summary>
    /// The tag name (e.g., "25-117-OP-III", "25-117").
    /// </summary>
    public string Tag { get; set; } = string.Empty;
    
    /// <summary>
    /// True if this tag was auto-suggested by the system, false if manually added.
    /// </summary>
    public bool IsAutoSuggested { get; set; }
    
    /// <summary>
    /// True if this tag should be inherited by child folders and items.
    /// </summary>
    public bool InheritToChildren { get; set; } = true;
    
    /// <summary>
    /// When the tag was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Who created the tag ('user', 'system', 'import').
    /// </summary>
    public string CreatedBy { get; set; } = "user";
}

