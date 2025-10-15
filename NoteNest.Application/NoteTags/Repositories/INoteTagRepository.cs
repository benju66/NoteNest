using NoteNest.Application.NoteTags.Models;

namespace NoteNest.Application.NoteTags.Repositories;

/// <summary>
/// Repository for managing note tags in the tree database.
/// </summary>
public interface INoteTagRepository
{
    /// <summary>
    /// Get all tags assigned to a specific note.
    /// </summary>
    Task<List<NoteTag>> GetNoteTagsAsync(Guid noteId);
    
    /// <summary>
    /// Set tags for a note (replaces existing tags).
    /// </summary>
    Task SetNoteTagsAsync(Guid noteId, List<string> tags, bool isAuto = false);
    
    /// <summary>
    /// Add a single tag to a note.
    /// </summary>
    Task AddNoteTagAsync(Guid noteId, string tag, bool isAuto = false);
    
    /// <summary>
    /// Remove all tags from a note.
    /// </summary>
    Task RemoveNoteTagsAsync(Guid noteId);
    
    /// <summary>
    /// Remove a specific tag from a note.
    /// </summary>
    Task RemoveNoteTagAsync(Guid noteId, string tag);
    
    /// <summary>
    /// Check if note has any tags.
    /// </summary>
    Task<bool> HasTagsAsync(Guid noteId);
}

