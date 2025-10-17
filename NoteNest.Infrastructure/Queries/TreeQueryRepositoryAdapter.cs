using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Application.Queries;
using NoteNest.Domain.Trees;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.Queries
{
    /// <summary>
    /// Implements ITreeRepository using event-sourced projections via ITreeQueryService.
    /// Replaces TreeRepositoryAdapter which used legacy tree.db.
    /// Read-only adapter for tree-wide operations (descendants, validation).
    /// Write operations throw NotSupportedException (use CQRS commands instead).
    /// </summary>
    public class TreeQueryRepositoryAdapter : ITreeRepository
    {
        private readonly ITreeQueryService _treeQueryService;
        private readonly IAppLogger _logger;

        public TreeQueryRepositoryAdapter(ITreeQueryService treeQueryService, IAppLogger logger)
        {
            _treeQueryService = treeQueryService ?? throw new ArgumentNullException(nameof(treeQueryService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<TreeNode> GetNodeByIdAsync(Guid nodeId)
        {
            return await _treeQueryService.GetByIdAsync(nodeId);
        }

        public async Task<List<TreeNode>> GetNodeDescendantsAsync(Guid nodeId)
        {
            return await _treeQueryService.GetNodeDescendantsAsync(nodeId);
        }

        // Write operations not supported - projections are read-only (CQRS pattern)
        // Tree state is managed through domain events and projection updates
        
        public Task<bool> UpdateNodeAsync(TreeNode node)
        {
            throw new NotSupportedException("Update operations not supported in query repository. Tree state is managed through projections.");
        }

        public Task<bool> DeleteNodeAsync(Guid nodeId, bool softDelete = true)
        {
            throw new NotSupportedException("Delete operations not supported in query repository. Use DeleteCategoryCommand instead.");
        }

        public Task<bool> BatchUpdateExpandedStatesAsync(Dictionary<Guid, bool> expandedStates)
        {
            // Expanded state is UI-only concern and not persisted in projections
            // CategoryTreeViewModel manages expanded state separately (debounced persistence)
            // Return success to satisfy interface contract
            _logger.Debug($"Expanded state update requested for {expandedStates?.Count ?? 0} nodes (not persisted in projections)");
            return Task.FromResult(true);
        }
    }
}

