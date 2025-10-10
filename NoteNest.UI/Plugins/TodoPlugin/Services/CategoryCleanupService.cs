using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;

namespace NoteNest.UI.Plugins.TodoPlugin.Services
{
    /// <summary>
    /// Service for cleaning up orphaned category references in todos.
    /// Validates that todos reference categories that still exist in the tree.
    /// Runs periodically or on-demand to maintain data integrity.
    /// </summary>
    public interface ICategoryCleanupService
    {
        Task<List<Guid>> GetOrphanedCategoryIdsAsync();
        Task<int> CleanupOrphanedCategoriesAsync();
        Task<bool> ValidateTodoCategoryAsync(Guid categoryId);
    }
    
    public class CategoryCleanupService : ICategoryCleanupService
    {
        private readonly ITodoRepository _todoRepository;
        private readonly ICategorySyncService _categorySyncService;
        private readonly IAppLogger _logger;
        
        public CategoryCleanupService(
            ITodoRepository todoRepository,
            ICategorySyncService categorySyncService,
            IAppLogger logger)
        {
            _todoRepository = todoRepository ?? throw new ArgumentNullException(nameof(todoRepository));
            _categorySyncService = categorySyncService ?? throw new ArgumentNullException(nameof(categorySyncService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Get all category IDs referenced by todos that no longer exist in the tree.
        /// </summary>
        public async Task<List<Guid>> GetOrphanedCategoryIdsAsync()
        {
            try
            {
                _logger.Debug("[CategoryCleanup] Checking for orphaned categories...");
                
                // Get all todos
                var allTodos = await _todoRepository.GetAllAsync(includeCompleted: true);
                
                // Get distinct category IDs referenced by todos
                var referencedCategoryIds = allTodos
                    .Where(t => t.CategoryId.HasValue)
                    .Select(t => t.CategoryId.Value)
                    .Distinct()
                    .ToList();
                
                _logger.Debug($"[CategoryCleanup] Found {referencedCategoryIds.Count} distinct categories referenced by todos");
                
                // Check which ones still exist in tree
                var orphanedIds = new List<Guid>();
                foreach (var categoryId in referencedCategoryIds)
                {
                    var exists = await _categorySyncService.IsCategoryInTreeAsync(categoryId);
                    if (!exists)
                    {
                        orphanedIds.Add(categoryId);
                        _logger.Warning($"[CategoryCleanup] Orphaned category found: {categoryId}");
                    }
                }
                
                _logger.Info($"[CategoryCleanup] Found {orphanedIds.Count} orphaned categories out of {referencedCategoryIds.Count} total");
                return orphanedIds;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryCleanup] Failed to get orphaned category IDs");
                return new List<Guid>();
            }
        }
        
        /// <summary>
        /// Clean up orphaned categories by setting todos' CategoryId to null (uncategorized).
        /// Returns the number of todos that were updated.
        /// </summary>
        public async Task<int> CleanupOrphanedCategoriesAsync()
        {
            try
            {
                _logger.Info("[CategoryCleanup] Starting orphaned category cleanup...");
                
                var orphanedCategoryIds = await GetOrphanedCategoryIdsAsync();
                if (orphanedCategoryIds.Count == 0)
                {
                    _logger.Info("[CategoryCleanup] No orphaned categories found - cleanup not needed");
                    return 0;
                }
                
                int totalCleaned = 0;
                
                foreach (var categoryId in orphanedCategoryIds)
                {
                    // Get all todos in this orphaned category
                    var todos = await _todoRepository.GetByCategoryAsync(categoryId, includeCompleted: true);
                    
                    _logger.Info($"[CategoryCleanup] Found {todos.Count} todos in orphaned category {categoryId}");
                    
                    // Move todos to uncategorized
                    foreach (var todo in todos)
                    {
                        todo.CategoryId = null;
                        await _todoRepository.UpdateAsync(todo);
                        totalCleaned++;
                    }
                    
                    _logger.Info($"[CategoryCleanup] Moved {todos.Count} todos to uncategorized");
                }
                
                _logger.Info($"[CategoryCleanup] ✅ Cleanup complete: {totalCleaned} todos moved to uncategorized from {orphanedCategoryIds.Count} orphaned categories");
                return totalCleaned;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryCleanup] Failed to cleanup orphaned categories");
                return 0;
            }
        }
        
        /// <summary>
        /// Validate that a specific category ID still exists in the tree.
        /// </summary>
        public async Task<bool> ValidateTodoCategoryAsync(Guid categoryId)
        {
            try
            {
                return await _categorySyncService.IsCategoryInTreeAsync(categoryId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[CategoryCleanup] Failed to validate category: {categoryId}");
                return false;
            }
        }
    }
}

