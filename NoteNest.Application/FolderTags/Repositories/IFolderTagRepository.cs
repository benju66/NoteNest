using NoteNest.Application.FolderTags.Models;

namespace NoteNest.Application.FolderTags.Repositories;

/// <summary>
/// Repository for managing folder tags in the tree database.
/// </summary>
public interface IFolderTagRepository
{
    /// <summary>
    /// Get all tags assigned to a specific folder (not including inherited tags).
    /// </summary>
    Task<List<FolderTag>> GetFolderTagsAsync(Guid folderId);
    
    /// <summary>
    /// Get inherited tags by walking up the tree.
    /// Returns tags from folder and all ancestors (if inherit_to_children = 1).
    /// </summary>
    Task<List<FolderTag>> GetInheritedTagsAsync(Guid folderId);
    
    /// <summary>
    /// Set tags for a folder (replaces existing tags).
    /// </summary>
    Task SetFolderTagsAsync(Guid folderId, List<string> tags, bool isAutoSuggested = false, bool inheritToChildren = true);
    
    /// <summary>
    /// Add a single tag to a folder.
    /// </summary>
    Task AddFolderTagAsync(Guid folderId, string tag, bool isAutoSuggested = false, bool inheritToChildren = true);
    
    /// <summary>
    /// Remove all tags from a folder.
    /// </summary>
    Task RemoveFolderTagsAsync(Guid folderId);
    
    /// <summary>
    /// Remove a specific tag from a folder.
    /// </summary>
    Task RemoveFolderTagAsync(Guid folderId, string tag);
    
    /// <summary>
    /// Get all folders that have tags (for bulk operations).
    /// </summary>
    Task<List<Guid>> GetTaggedFoldersAsync();
    
    /// <summary>
    /// Check if folder has any tags.
    /// </summary>
    Task<bool> HasTagsAsync(Guid folderId);
    
    /// <summary>
    /// Get all child folder IDs (recursive).
    /// </summary>
    Task<List<Guid>> GetChildFolderIdsAsync(Guid folderId);
}

