using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Trees;

namespace NoteNest.Infrastructure.Database.Adapters
{
    /// <summary>
    /// Adapter that exposes ITreeRepository interface for the Application layer.
    /// Wraps ITreeDatabaseRepository from the Infrastructure layer.
    /// </summary>
    public class TreeRepositoryAdapter : ITreeRepository
    {
        private readonly ITreeDatabaseRepository _treeRepository;

        public TreeRepositoryAdapter(ITreeDatabaseRepository treeRepository)
        {
            _treeRepository = treeRepository ?? throw new ArgumentNullException(nameof(treeRepository));
        }

        public async Task<TreeNode> GetNodeByIdAsync(Guid nodeId)
        {
            return await _treeRepository.GetNodeByIdAsync(nodeId);
        }

        public async Task<List<TreeNode>> GetNodeDescendantsAsync(Guid nodeId)
        {
            return await _treeRepository.GetNodeDescendantsAsync(nodeId);
        }

        public async Task<bool> UpdateNodeAsync(TreeNode node)
        {
            return await _treeRepository.UpdateNodeAsync(node);
        }

        public async Task<bool> DeleteNodeAsync(Guid nodeId, bool softDelete = true)
        {
            return await _treeRepository.DeleteNodeAsync(nodeId, softDelete);
        }

        public async Task<bool> BatchUpdateExpandedStatesAsync(Dictionary<Guid, bool> expandedStates)
        {
            if (expandedStates == null || expandedStates.Count == 0)
                return true;

            try
            {
                // Update each node's expanded state
                foreach (var kvp in expandedStates)
                {
                    var node = await _treeRepository.GetNodeByIdAsync(kvp.Key);
                    if (node == null) continue;

                    // Reconstruct TreeNode with updated IsExpanded (immutable pattern)
                    var updatedNode = TreeNode.CreateFromDatabase(
                        id: node.Id,
                        parentId: node.ParentId,
                        canonicalPath: node.CanonicalPath,
                        displayPath: node.DisplayPath,
                        absolutePath: node.AbsolutePath,
                        nodeType: node.NodeType,
                        name: node.Name,
                        fileExtension: node.FileExtension,
                        fileSize: node.FileSize,
                        createdAt: node.CreatedAt,
                        modifiedAt: node.ModifiedAt,
                        accessedAt: node.AccessedAt,
                        quickHash: node.QuickHash,
                        fullHash: node.FullHash,
                        hashAlgorithm: node.HashAlgorithm,
                        hashCalculatedAt: node.HashCalculatedAt,
                        isExpanded: kvp.Value,  // ⭐ ONLY CHANGE
                        isPinned: node.IsPinned,
                        isSelected: node.IsSelected,
                        sortOrder: node.SortOrder,
                        colorTag: node.ColorTag,
                        iconOverride: node.IconOverride,
                        isDeleted: node.IsDeleted,
                        deletedAt: node.DeletedAt,
                        metadataVersion: node.MetadataVersion,
                        customProperties: node.CustomProperties
                    );

                    await _treeRepository.UpdateNodeAsync(updatedNode);
                }

                return true;
            }
            catch
            {
                // Non-critical failure - UI state already changed
                return false;
            }
        }
    }
}

