using System;
using System.Collections.Generic;
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
    }
}

