using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Application.Queries;
using NoteNest.UI.Plugins.TodoPlugin.Models;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Trees;

namespace NoteNest.UI.Plugins.TodoPlugin.Services
{
    /// <summary>
    /// Synchronizes TodoPlugin categories with the main app's note tree structure.
    /// Categories are queried from tree_view projection (event-sourced) with intelligent caching.
    /// Follows TreeCacheService pattern with 5-minute expiration and event-driven invalidation.
    /// </summary>
    public interface ICategorySyncService
    {
        Task<List<Category>> GetAllCategoriesAsync();
        Task<Category?> GetCategoryByIdAsync(Guid categoryId);
        Task<List<Category>> GetRootCategoriesAsync();
        Task<List<Category>> GetChildCategoriesAsync(Guid parentId);
        Task<bool> IsCategoryInTreeAsync(Guid categoryId);
        void InvalidateCache();
    }
    
    public class CategorySyncService : ICategorySyncService
    {
        private readonly ITreeQueryService _treeQueryService;
        private readonly IAppLogger _logger;
        
        // Cache with 5-minute expiration (matches TreeCacheService pattern)
        private List<Category>? _cachedCategories;
        private DateTime? _cacheTime;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);
        private readonly object _cacheLock = new object();
        
        public CategorySyncService(
            ITreeQueryService treeQueryService,
            IAppLogger logger)
        {
            _treeQueryService = treeQueryService ?? throw new ArgumentNullException(nameof(treeQueryService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Get all categories from note tree database with caching.
        /// Cache expires after 5 minutes or can be invalidated manually.
        /// </summary>
        public async Task<List<Category>> GetAllCategoriesAsync()
        {
            lock (_cacheLock)
            {
                // Return cached if valid
                if (_cachedCategories != null && _cacheTime.HasValue)
                {
                    var age = DateTime.UtcNow - _cacheTime.Value;
                    if (age < _cacheExpiration)
                    {
                        _logger.Debug($"[CategorySync] Returning cached categories (age: {age.TotalSeconds:F1}s)");
                        return _cachedCategories;
                    }
                }
            }
            
            try
            {
                _logger.Info("[CategorySync] ========== QUERYING TREE_VIEW ==========");
                _logger.Debug("[CategorySync] Cache expired or empty, querying tree_view projection...");
                
                // Query tree_view projection (event-sourced) where node_type = 'category'
                var treeNodes = await _treeQueryService.GetAllNodesAsync(includeDeleted: false);
                
                _logger.Info($"[CategorySync] ✅ tree_view returned {treeNodes.Count} total nodes");
                
                var categories = treeNodes
                    .Where(n => n.NodeType == TreeNodeType.Category)
                    .Select(n => new Category
                    {
                        Id = n.Id,
                        ParentId = n.ParentId,
                        Name = n.Name,
                        Order = n.SortOrder,
                        CreatedDate = n.CreatedAt,
                        ModifiedDate = n.ModifiedAt
                    })
                    .ToList();
                
                _logger.Info($"[CategorySync] ✅ Filtered to {categories.Count} categories (NodeType == TreeNodeType.Category)");
                
                // Log first 10 categories for diagnostics
                var sampleCount = Math.Min(categories.Count, 10);
                for (int i = 0; i < sampleCount; i++)
                {
                    var cat = categories[i];
                    _logger.Info($"[CategorySync]   [{i+1}] {cat.Name} (ID: {cat.Id})");
                }
                
                if (categories.Count > 10)
                {
                    _logger.Info($"[CategorySync]   ... and {categories.Count - 10} more");
                }
                
                lock (_cacheLock)
                {
                    _cachedCategories = categories;
                    _cacheTime = DateTime.UtcNow;
                }
                
                _logger.Info($"[CategorySync] Categories cached for {_cacheExpiration.TotalMinutes} minutes");
                return categories;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategorySync] Failed to load categories from tree_view projection");
                return new List<Category>();
            }
        }
        
        /// <summary>
        /// Get single category by ID (uses cache when possible)
        /// </summary>
        public async Task<Category?> GetCategoryByIdAsync(Guid categoryId)
        {
            try
            {
                _logger.Info($"[CategorySync] >>> Looking for category ID: {categoryId}");
                
                // Try cache first
                var allCategories = await GetAllCategoriesAsync();
                
                _logger.Info($"[CategorySync] >>> Cache has {allCategories.Count} categories total");
                
                var cached = allCategories.FirstOrDefault(c => c.Id == categoryId);
                if (cached != null)
                {
                    _logger.Info($"[CategorySync] >>> ✅ FOUND in cache: {cached.Name}");
                    return cached;
                }
                
                _logger.Info($"[CategorySync] >>> NOT in cache, trying direct query...");
                
                // Fallback to direct query if not in cache (shouldn't happen often)
                var treeNode = await _treeQueryService.GetByIdAsync(categoryId);
                
                if (treeNode == null)
                {
                    _logger.Warning($"[CategorySync] >>> ❌ NOT FOUND in tree_view (null)");
                    return null;
                }
                
                if (treeNode.NodeType != TreeNodeType.Category)
                {
                    _logger.Warning($"[CategorySync] >>> ❌ Found but wrong type: {treeNode.NodeType} (expected Category)");
                    return null;
                }
                
                _logger.Info($"[CategorySync] >>> ✅ FOUND via direct query: {treeNode.Name}");
                
                return new Category
                {
                    Id = treeNode.Id,
                    ParentId = treeNode.ParentId,
                    Name = treeNode.Name,
                    Order = treeNode.SortOrder,
                    CreatedDate = treeNode.CreatedAt,
                    ModifiedDate = treeNode.ModifiedAt
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[CategorySync] Failed to get category: {categoryId}");
                return null;
            }
        }
        
        /// <summary>
        /// Get root categories (no parent)
        /// </summary>
        public async Task<List<Category>> GetRootCategoriesAsync()
        {
            try
            {
                var allCategories = await GetAllCategoriesAsync();
                return allCategories.Where(c => c.ParentId == null).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategorySync] Failed to get root categories");
                return new List<Category>();
            }
        }
        
        /// <summary>
        /// Get child categories of a parent
        /// </summary>
        public async Task<List<Category>> GetChildCategoriesAsync(Guid parentId)
        {
            try
            {
                var allCategories = await GetAllCategoriesAsync();
                return allCategories.Where(c => c.ParentId == parentId).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[CategorySync] Failed to get children: {parentId}");
                return new List<Category>();
            }
        }
        
        /// <summary>
        /// Check if category exists in tree (validation)
        /// </summary>
        public async Task<bool> IsCategoryInTreeAsync(Guid categoryId)
        {
            try
            {
                var category = await GetCategoryByIdAsync(categoryId);
                return category != null;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Invalidate cache to force refresh on next query.
        /// Called when categories are created/deleted/renamed in main app.
        /// </summary>
        public void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cachedCategories = null;
                _cacheTime = null;
            }
            _logger.Debug("[CategorySync] Cache invalidated");
        }
    }
}

