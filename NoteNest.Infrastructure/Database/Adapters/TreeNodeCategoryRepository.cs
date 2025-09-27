using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Common;
using NoteNest.Domain.Categories;
using NoteNest.Domain.Trees;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.Database.Adapters
{
    /// <summary>
    /// Adapter that bridges the legacy Category domain model with the new TreeNode database.
    /// Provides ICategoryRepository interface while using TreeDatabaseRepository underneath.
    /// </summary>
    public class TreeNodeCategoryRepository : ICategoryRepository
    {
        private readonly ITreeDatabaseRepository _treeRepository;
        private readonly IAppLogger _logger;

        public TreeNodeCategoryRepository(ITreeDatabaseRepository treeRepository, IAppLogger logger)
        {
            _treeRepository = treeRepository ?? throw new ArgumentNullException(nameof(treeRepository));
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

                var treeNode = await _treeRepository.GetNodeByIdAsync(guid);
                if (treeNode == null || treeNode.NodeType != TreeNodeType.Category)
                {
                    return null;
                }

                return ConvertTreeNodeToCategory(treeNode);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get category by id: {id.Value}");
                return null;
            }
        }

        public async Task<IReadOnlyList<Category>> GetAllAsync()
        {
            try
            {
                var allNodes = await _treeRepository.GetAllNodesAsync();
                var categoryNodes = allNodes.Where(n => n.NodeType == TreeNodeType.Category).ToList();
                
                var categories = categoryNodes
                    .Select(ConvertTreeNodeToCategory)
                    .Where(c => c != null)
                    .ToList();

                _logger.Info($"Loaded {categories.Count} categories from TreeNode database");
                return categories.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get all categories from TreeNode database");
                return new List<Category>().AsReadOnly();
            }
        }

        public async Task<IReadOnlyList<Category>> GetRootCategoriesAsync()
        {
            try
            {
                var rootNodes = await _treeRepository.GetRootNodesAsync();
                var categoryNodes = rootNodes.Where(n => n.NodeType == TreeNodeType.Category).ToList();
                
                var categories = categoryNodes
                    .Select(ConvertTreeNodeToCategory)
                    .Where(c => c != null)
                    .ToList();

                _logger.Info($"Loaded {categories.Count} root categories from TreeNode database");
                return categories.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get root categories from TreeNode database");
                return new List<Category>().AsReadOnly();
            }
        }

        public async Task<Result> CreateAsync(Category category)
        {
            try
            {
                var treeNode = ConvertCategoryToTreeNode(category);
                var success = await _treeRepository.InsertNodeAsync(treeNode);
                
                return success 
                    ? Result.Ok()
                    : Result.Fail("Failed to create category in database");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create category: {category.Name}");
                return Result.Fail($"Failed to create category: {ex.Message}");
            }
        }

        public async Task<Result> UpdateAsync(Category category)
        {
            try
            {
                var treeNode = ConvertCategoryToTreeNode(category);
                var success = await _treeRepository.UpdateNodeAsync(treeNode);
                
                return success 
                    ? Result.Ok()
                    : Result.Fail("Failed to update category in database");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to update category: {category.Name}");
                return Result.Fail($"Failed to update category: {ex.Message}");
            }
        }

        public async Task<Result> DeleteAsync(CategoryId id)
        {
            try
            {
                if (!Guid.TryParse(id.Value, out var guid))
                {
                    return Result.Fail($"Invalid CategoryId format: {id.Value}");
                }

                var success = await _treeRepository.DeleteNodeAsync(guid, softDelete: true);
                
                return success 
                    ? Result.Ok()
                    : Result.Fail("Failed to delete category from database");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to delete category: {id.Value}");
                return Result.Fail($"Failed to delete category: {ex.Message}");
            }
        }

        public async Task<bool> ExistsAsync(CategoryId id)
        {
            try
            {
                if (!Guid.TryParse(id.Value, out var guid))
                {
                    return false;
                }

                var treeNode = await _treeRepository.GetNodeByIdAsync(guid);
                return treeNode != null && treeNode.NodeType == TreeNodeType.Category;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to check category existence: {id.Value}");
                return false;
            }
        }

        // =============================================================================
        // PRIVATE CONVERSION METHODS
        // =============================================================================

        private Category ConvertTreeNodeToCategory(TreeNode treeNode)
        {
            if (treeNode.NodeType != TreeNodeType.Category)
            {
                return null;
            }

            try
            {
                var categoryId = CategoryId.From(treeNode.Id.ToString());
                var parentId = treeNode.ParentId.HasValue 
                    ? CategoryId.From(treeNode.ParentId.Value.ToString())
                    : null;

                return new Category(categoryId, treeNode.Name, treeNode.AbsolutePath, parentId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to convert TreeNode to Category: {treeNode.Name}");
                return null;
            }
        }

        private TreeNode ConvertCategoryToTreeNode(Category category)
        {
            var categoryGuid = Guid.TryParse(category.Id.Value, out var catGuid) ? catGuid : Guid.NewGuid();
            var parentId = category.ParentId != null && Guid.TryParse(category.ParentId.Value, out var parentGuid)
                ? (Guid?)parentGuid
                : null;

            return TreeNode.CreateFromDatabase(
                id: categoryGuid,
                parentId: parentId,
                canonicalPath: category.Path.ToLowerInvariant(),
                displayPath: category.Path,
                absolutePath: category.Path,
                nodeType: TreeNodeType.Category,
                name: category.Name,
                createdAt: DateTime.UtcNow,
                modifiedAt: DateTime.UtcNow
            );
        }
    }
}
