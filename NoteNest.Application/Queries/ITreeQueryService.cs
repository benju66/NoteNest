using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Domain.Trees;

namespace NoteNest.Application.Queries
{
    /// <summary>
    /// Query service for tree view projection.
    /// Replaces ITreeRepository and ICategoryRepository read operations.
    /// </summary>
    public interface ITreeQueryService
    {
        /// <summary>
        /// Get tree node by ID.
        /// </summary>
        Task<TreeNode> GetByIdAsync(Guid id);
        
        /// <summary>
        /// Get all nodes (cached for performance).
        /// </summary>
        Task<List<TreeNode>> GetAllNodesAsync(bool includeDeleted = false);
        
        /// <summary>
        /// Get children of a parent node.
        /// </summary>
        Task<List<TreeNode>> GetChildrenAsync(Guid? parentId);
        
        /// <summary>
        /// Get root nodes (no parent).
        /// </summary>
        Task<List<TreeNode>> GetRootNodesAsync();
        
        /// <summary>
        /// Get pinned nodes.
        /// </summary>
        Task<List<TreeNode>> GetPinnedAsync();
        
        /// <summary>
        /// Get node by canonical path.
        /// </summary>
        Task<TreeNode> GetByPathAsync(string canonicalPath);
        
        /// <summary>
        /// Invalidate cache (call after projection updates).
        /// </summary>
        void InvalidateCache();
    }
}

