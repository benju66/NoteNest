using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Collections;
using NoteNest.UI.Plugins.TodoPlugin.Events;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Services
{
    /// <summary>
    /// Category store implementation that syncs with main app's tree database.
    /// Categories are loaded from tree_nodes on initialization and can be refreshed.
    /// Follows TodoStore pattern with SmartObservableCollection batch updates.
    /// Publishes events via EventBus for loose coupling with TodoStore.
    /// </summary>
    public class CategoryStore : ICategoryStore
    {
        private readonly SmartObservableCollection<Category> _categories;
        private readonly ICategorySyncService _syncService;
        private readonly ICategoryPersistenceService _persistenceService;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;
        private bool _isInitialized;

        public CategoryStore(
            ICategorySyncService syncService,
            ICategoryPersistenceService persistenceService,
            IEventBus eventBus,
            IAppLogger logger)
        {
            _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
            _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _categories = new SmartObservableCollection<Category>();
        }
        
        /// <summary>
        /// Initialize store by loading previously selected categories from database.
        /// Categories persist across app restarts via user_preferences table.
        /// Validates that loaded categories still exist in the tree.
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
                _logger.Info("[CategoryStore] ========== INITIALIZATION START ==========");
                _logger.Info("[CategoryStore] Loading saved categories from database...");
                
                // Load categories from user_preferences table
                var savedCategories = await _persistenceService.LoadCategoriesAsync();
                
                _logger.Info($"[CategoryStore] ✅ Loaded {savedCategories.Count} categories from user_preferences");
                
                if (savedCategories.Count > 0)
                {
                    _logger.Info($"[CategoryStore] Beginning validation of {savedCategories.Count} categories...");
                    
                    // Validate that categories still exist in the tree
                    var validCategories = new List<Category>();
                    var removedCount = 0;
                    
                    foreach (var category in savedCategories)
                    {
                        _logger.Info($"[CategoryStore] >>> Validating category: '{category.Name}' (ID: {category.Id})");
                        
                        // Check if category still exists in tree database
                        var stillExists = await _syncService.IsCategoryInTreeAsync(category.Id);
                        
                        _logger.Info($"[CategoryStore] >>> Validation result for '{category.Name}': {(stillExists ? "EXISTS ✅" : "NOT FOUND ❌")}");
                        
                        if (stillExists)
                        {
                            validCategories.Add(category);
                            _logger.Info($"[CategoryStore] >>> Category '{category.Name}' ADDED to valid list");
                        }
                        else
                        {
                            _logger.Warning($"[CategoryStore] >>> REMOVING orphaned category: {category.Name} (deleted from tree)");
                            removedCount++;
                        }
                    }
                    
                    // Load only valid categories
                    _logger.Info($"[CategoryStore] === VALIDATION COMPLETE ===");
                    _logger.Info($"[CategoryStore] Valid categories: {validCategories.Count}");
                    _logger.Info($"[CategoryStore] Removed categories: {removedCount}");
                    
                    if (validCategories.Count > 0)
                    {
                        using (_categories.BatchUpdate())
                        {
                            _categories.Clear();
                            _categories.AddRange(validCategories);
                        }
                        
                        _logger.Info($"[CategoryStore] ✅ Restored {validCategories.Count} valid categories to collection");
                        
                        foreach (var cat in validCategories)
                        {
                            _logger.Info($"[CategoryStore]   - {cat.Name} (ID: {cat.Id})");
                        }
                        
                        if (removedCount > 0)
                        {
                            _logger.Info($"[CategoryStore] Removed {removedCount} orphaned categories");
                            
                            // Save cleaned list back to database
                            await _persistenceService.SaveCategoriesAsync(validCategories);
                        }
                    }
                    else
                    {
                        _logger.Warning("[CategoryStore] ❌ NO VALID CATEGORIES after cleanup - starting empty");
                    }
                }
                else
                {
                    _logger.Info("[CategoryStore] No saved categories in user_preferences - starting empty");
                }
                
                _logger.Info("[CategoryStore] ========== INITIALIZATION COMPLETE ==========");
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

        public async Task AddAsync(Category category)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));
            
            _logger.Info($"[CategoryStore] ADDING category: Name='{category.Name}', Id={category.Id}, ParentId={category.ParentId}");
            
            _categories.Add(category);
            
            // Auto-save to database (properly awaited)
            await SaveToDatabaseAsync();
            
            // Publish event for loose coupling (properly awaited)
            await _eventBus.PublishAsync(new CategoryAddedEvent(category.Id, category.Name));
            
            _logger.Info($"[CategoryStore] ✅ Category added: {category.Name} (Count: {_categories.Count})");
        }
        
        // Legacy synchronous wrapper for backward compatibility
        public void Add(Category category)
        {
            _ = AddAsync(category);
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
            // Prevent deletion of system categories
            if (id == Guid.Empty)
            {
                _logger.Warning($"[CategoryStore] Cannot delete system category: Uncategorized");
                return;
            }
            
            var category = GetById(id);
            if (category != null)
            {
                var categoryName = category.Name; // Capture before removal
                
                _categories.Remove(category);
                
                // Auto-save to database
                _ = SaveToDatabaseAsync();
                
                // Publish event for TodoStore to clean up orphaned todos
                _ = _eventBus.PublishAsync(new CategoryDeletedEvent(id, categoryName));
                
                _logger.Info($"[CategoryStore] Deleted category: {categoryName}");
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
