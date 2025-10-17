using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Application.Queries;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Categories;
using NoteNest.Domain.Common;
using NoteNest.Domain.Trees;

namespace NoteNest.Infrastructure.Queries
{
    /// <summary>
    /// Read-only repository for Categories using ITreeQueryService projection.
    /// Provides Category aggregate data from the tree_view projection.
    /// Mirrors NoteQueryRepository pattern for consistency.
    /// </summary>
    public class CategoryQueryRepository : ICategoryRepository
    {
        private readonly ITreeQueryService _treeQueryService;
        private readonly IAppLogger _logger;

        public CategoryQueryRepository(ITreeQueryService treeQueryService, IAppLogger logger)
        {
            _treeQueryService = treeQueryService ?? throw new ArgumentNullException(nameof(treeQueryService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Category> GetByIdAsync(CategoryId id)
        {
            try
            {
                if (!Guid.TryParse(id.Value, out var guid))
                {
                    _logger.Warning($"Invalid CategoryId format: {id.Value}");
                    return null;
                }

                var node = await _treeQueryService.GetByIdAsync(guid);
                if (node == null || node.NodeType != TreeNodeType.Category)
                {
                    return null;
                }

                return ConvertTreeNodeToCategory(node);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get category by ID: {id}");
                return null;
            }
        }

        public async Task<IReadOnlyList<Category>> GetAllAsync()
        {
            try
            {
                var allNodes = await _treeQueryService.GetAllNodesAsync();
                var categories = allNodes
                    .Where(n => n.NodeType == TreeNodeType.Category)
                    .Select(ConvertTreeNodeToCategory)
                    .Where(c => c != null)
                    .ToList();

                _logger.Debug($"Loaded {categories.Count} categories from projection");
                return categories;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get all categories");
                return new List<Category>();
            }
        }

        public async Task<IReadOnlyList<Category>> GetRootCategoriesAsync()
        {
            try
            {
                var rootNodes = await _treeQueryService.GetRootNodesAsync();
                var categories = rootNodes
                    .Where(n => n.NodeType == TreeNodeType.Category)
                    .Select(ConvertTreeNodeToCategory)
                    .Where(c => c != null)
                    .ToList();

                _logger.Debug($"Loaded {categories.Count} root categories from projection");
                return categories;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get root categories");
                return new List<Category>();
            }
        }

        // Write operations are not supported in read-only query repository (CQRS pattern)
        public Task<Result> CreateAsync(Category category)
        {
            throw new NotSupportedException("Create operations not supported in query repository. Use CreateCategoryCommand instead.");
        }

        public Task<Result> UpdateAsync(Category category)
        {
            throw new NotSupportedException("Update operations not supported in query repository. Use RenameCategoryCommand instead.");
        }

        public Task<Result> DeleteAsync(CategoryId id)
        {
            throw new NotSupportedException("Delete operations not supported in query repository. Use DeleteCategoryCommand instead.");
        }

        public async Task<bool> ExistsAsync(CategoryId id)
        {
            var category = await GetByIdAsync(id);
            return category != null;
        }

        public Task InvalidateCacheAsync()
        {
            // TreeQueryService handles its own caching via IMemoryCache
            _treeQueryService.InvalidateCache();
            return Task.CompletedTask;
        }

        private Category ConvertTreeNodeToCategory(TreeNode treeNode)
        {
            try
            {
                if (treeNode.NodeType != TreeNodeType.Category)
                    return null;

                var categoryId = CategoryId.From(treeNode.Id.ToString());
                var parentId = treeNode.ParentId.HasValue 
                    ? CategoryId.From(treeNode.ParentId.Value.ToString())
                    : null;

                // Use AbsolutePath if available, fallback to DisplayPath
                // Both contain the full category path in the projection
                var path = treeNode.AbsolutePath ?? treeNode.DisplayPath;

                return new Category(categoryId, treeNode.Name, path, parentId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to convert TreeNode to Category: {treeNode.Name}");
                return null;
            }
        }
    }
}

