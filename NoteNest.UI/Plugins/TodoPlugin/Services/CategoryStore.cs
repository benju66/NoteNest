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
        private readonly ICategoryPersistenceService _persistenceService;
        private readonly IAppLogger _logger;
        private bool _isInitialized;

        public CategoryStore(
            ICategorySyncService syncService,
            ICategoryPersistenceService persistenceService,
            IAppLogger logger)
        {
            _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
            _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _categories = new SmartObservableCollection<Category>();
        }
        
        /// <summary>
        /// Initialize store by loading previously selected categories from database.
        /// Categories persist across app restarts via user_preferences table.
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
                _logger.Info("[CategoryStore] Loading saved categories from database...");
                
                // Load categories from user_preferences table
                var savedCategories = await _persistenceService.LoadCategoriesAsync();
                
                if (savedCategories.Count > 0)
                {
                    using (_categories.BatchUpdate())
                    {
                        _categories.Clear();
                        _categories.AddRange(savedCategories);
                    }
                    
                    _logger.Info($"[CategoryStore] Restored {savedCategories.Count} categories from database");
                }
                else
                {
                    _logger.Info("[CategoryStore] No saved categories found - starting empty");
                }
                
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryStore] Failed to initialize");
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
            
            _logger.Info($"[CategoryStore] ADDING category: Name='{category.Name}', Id={category.Id}, ParentId={category.ParentId}");
            
            _categories.Add(category);
            
            // Auto-save to database
            _ = SaveToDatabaseAsync();
            
            _logger.Info($"[CategoryStore] ✅ Category added: {category.Name} (Count: {_categories.Count})");
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
                
                // Auto-save to database
                _ = SaveToDatabaseAsync();
                
                _logger.Info($"[CategoryStore] Deleted category: {category.Name}");
            }
        }
        
        /// <summary>
        /// Save categories to database (debounced to avoid excessive writes)
        /// </summary>
        private async Task SaveToDatabaseAsync()
        {
            try
            {
                await _persistenceService.SaveCategoriesAsync(_categories);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryStore] Failed to save categories to database");
                // Don't throw - persistence failure shouldn't crash app
            }
        }
    }
}
