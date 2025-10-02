using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Domain.Trees;

namespace NoteNest.Application.Common.Interfaces
{
    /// <summary>
    /// Repository for tree-wide operations (descendants, bulk updates).
    /// Used by category operations that affect multiple tree nodes.
    /// </summary>
    public interface ITreeRepository
    {
        /// <summary>
        /// Get all descendants of a node (children, grandchildren, etc.)
        /// </summary>
        Task<List<TreeNode>> GetNodeDescendantsAsync(Guid nodeId);
        
        /// <summary>
        /// Update a tree node (used for bulk path updates during rename/move)
        /// </summary>
        Task<bool> UpdateNodeAsync(TreeNode node);
        
        /// <summary>
        /// Soft-delete a node (sets is_deleted = 1)
        /// </summary>
        Task<bool> DeleteNodeAsync(Guid nodeId, bool softDelete = true);
    }
}

