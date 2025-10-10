using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Collections;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Services
{
    /// <summary>
    /// Category store implementation that syncs with main app's tree database.
    /// Categories are loaded from tree_nodes on initialization and can be refreshed.
    /// Follows TodoStore pattern with SmartObservableCollection batch updates.
    /// </summary>
    public class CategoryStore : ICategoryStore
    {
        private readonly SmartObservableCollection<Category> _categories;
        private readonly ICategorySyncService _syncService;
        private readonly IAppLogger _logger;
        private bool _isInitialized;

        public CategoryStore(
            ICategorySyncService syncService,
            IAppLogger logger)
        {
            _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _categories = new SmartObservableCollection<Category>();
        }
        
        /// <summary>
        /// Initialize by loading categories from tree database.
        /// Uses SmartObservableCollection.BatchUpdate() to eliminate UI flickering.
        /// Call this once during plugin startup.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                _logger.Debug("[CategoryStore] Already initialized, skipping");
                return;
            }
                
            try
            {
                _logger.Info("[CategoryStore] Loading categories from note tree...");
                
                var categories = await _syncService.GetAllCategoriesAsync();
                
                // Use batch update for smooth UI transition (no flickering)
                using (_categories.BatchUpdate())
                {
                    _categories.Clear();
                    _categories.AddRange(categories);
                }
                
                _isInitialized = true;
                _logger.Info($"[CategoryStore] Loaded {categories.Count} categories from tree");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryStore] Failed to load categories - starting with empty collection");
                // Graceful degradation: mark as initialized anyway
                _isInitialized = true;
            }
        }
        
        /// <summary>
        /// Refresh categories from tree database.
        /// Call when tree structure changes (category created/deleted/renamed).
        /// Invalidates cache and reloads from database.
        /// </summary>
        public async Task RefreshAsync()
        {
            try
            {
                _logger.Debug("[CategoryStore] Refreshing categories from tree...");
                
                // Invalidate cache to force fresh query
                _syncService.InvalidateCache();
                
                var categories = await _syncService.GetAllCategoriesAsync();
                
                // Use batch update for smooth UI transition
                using (_categories.BatchUpdate())
                {
                    _categories.Clear();
                    _categories.AddRange(categories);
                }
                
                _logger.Info($"[CategoryStore] Refreshed {categories.Count} categories from tree");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryStore] Failed to refresh categories");
            }
        }

        public ObservableCollection<Category> Categories => _categories;

        public Category? GetById(Guid id)
        {
            return _categories.FirstOrDefault(c => c.Id == id);
        }

        public void Add(Category category)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));
            _categories.Add(category);
            _logger.Debug($"[CategoryStore] Added category: {category.Name}");
        }

        public void Update(Category category)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));
            
            var existing = GetById(category.Id);
            if (existing != null)
            {
                var index = _categories.IndexOf(existing);
                _categories[index] = category;
                _logger.Debug($"[CategoryStore] Updated category: {category.Name}");
            }
        }

        public void Delete(Guid id)
        {
            var category = GetById(id);
            if (category != null)
            {
                _categories.Remove(category);
                _logger.Debug($"[CategoryStore] Deleted category: {category.Name}");
            }
        }
    }
}
