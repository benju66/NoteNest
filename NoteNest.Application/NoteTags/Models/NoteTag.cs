namespace NoteNest.Application.NoteTags.Models;

/// <summary>
/// Represents a tag assigned to a note in the tree.
/// </summary>
public class NoteTag
{
    /// <summary>
    /// The note (TreeNode) ID that this tag is assigned to.
    /// </summary>
    public Guid NoteId { get; set; }
    
    /// <summary>
    /// The tag name (e.g., "meeting", "draft", "important").
    /// </summary>
    public string Tag { get; set; } = string.Empty;
    
    /// <summary>
    /// True if this tag was auto-suggested by the system, false if manually added.
    /// </summary>
    public bool IsAuto { get; set; }
    
    /// <summary>
    /// When the tag was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

