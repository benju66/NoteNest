using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// UI adapter for TreeController that handles ViewModel conversions and UI-specific concerns.
    /// Bridges between Core tree coordination and UI ViewModels.
    /// </summary>
    public class TreeControllerAdapter
    {
        private readonly ITreeController _treeController;
        private readonly TreeViewModelAdapter _viewModelAdapter;
        private readonly TreeStateAdapter _stateAdapter;
        private FileWatcherService _fileWatcher;
        private NoteNest.Core.Services.NoteMetadataManager _metadataManager;
        private readonly IStateManager _stateManager;
        private readonly IAppLogger _logger;
        
        public event EventHandler<TreeUIChangedEventArgs> TreeUIChanged;

        public TreeControllerAdapter(
            ITreeController treeController,
            TreeViewModelAdapter viewModelAdapter,
            TreeStateAdapter stateAdapter,
            FileWatcherService fileWatcher,
            NoteNest.Core.Services.NoteMetadataManager metadataManager,
            IStateManager stateManager,
            IAppLogger logger)
        {
            _treeController = treeController ?? throw new ArgumentNullException(nameof(treeController));
            _viewModelAdapter = viewModelAdapter ?? throw new ArgumentNullException(nameof(viewModelAdapter));
            _stateAdapter = stateAdapter ?? throw new ArgumentNullException(nameof(stateAdapter));
            _fileWatcher = fileWatcher; // Can be null, will be set later
            _metadataManager = metadataManager; // Can be null, will be set later
            _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Subscribe to core tree changes
            _treeController.TreeChanged += OnCoreTreeChanged;
        }

        /// <summary>
        /// Sets the file watcher after lazy initialization
        /// </summary>
        public void SetFileWatcher(FileWatcherService fileWatcher)
        {
            _fileWatcher = fileWatcher;
        }

        /// <summary>
        /// Sets the metadata manager after lazy initialization
        /// </summary>
        public void SetMetadataManager(NoteNest.Core.Services.NoteMetadataManager metadataManager)
        {
            _metadataManager = metadataManager;
        }

        /// <summary>
        /// Loads tree data and converts to UI ViewModels with full integration setup
        /// </summary>
        public async Task<TreeUILoadResult> LoadTreeUIAsync()
        {
            try
            {
                _stateManager.BeginOperation("Loading categories...");

                // Stop existing watchers before setting up new ones (if file watcher is available)
                if (_fileWatcher != null)
                {
                    _fileWatcher.StopAllWatchers();
                }

                // Load tree data from core controller
                var treeData = await _treeController.LoadTreeDataAsync();
                
                if (!treeData.Success)
                {
                    _stateManager.EndOperation("Error loading categories");
                    return new TreeUILoadResult
                    {
                        Success = false,
                        ErrorMessage = treeData.ErrorMessage
                    };
                }

                // Convert to UI ViewModels
                var uiCollections = _viewModelAdapter.ConvertToUICollections(treeData);

                _stateManager.EndOperation($"Loaded {treeData.TotalCategoriesLoaded} categories");

                var result = new TreeUILoadResult
                {
                    UICollections = uiCollections,
                    Success = true,
                    TotalCategoriesLoaded = treeData.TotalCategoriesLoaded
                };

                // Notify UI of successful load
                TreeUIChanged?.Invoke(this, new TreeUIChangedEventArgs
                {
                    ChangeType = TreeUIChangeType.Loaded,
                    UICollections = uiCollections
                });

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "TreeControllerAdapter: Failed to load tree UI");
                _stateManager.EndOperation("Error loading categories");
                
                return new TreeUILoadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Restores expansion state to UI tree
        /// </summary>
        public async Task<bool> RestoreExpansionStateAsync(ObservableCollection<CategoryTreeItem> categories)
        {
            try
            {
                // First collapse everything
                void CollapseAll(ObservableCollection<CategoryTreeItem> items)
                {
                    foreach (var item in items)
                    {
                        item.IsExpanded = false;
                        CollapseAll(item.SubCategories);
                    }
                }
                CollapseAll(categories);

                // Restore expansion state
                var success = await _stateAdapter.LoadAndApplyExpansionStateAsync(categories);
                
                if (success)
                {
                    _logger.Debug("TreeControllerAdapter: Successfully restored expansion state");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.Warning($"TreeControllerAdapter: Expansion state restore failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Saves current expansion state from UI
        /// </summary>
        public async Task SaveExpansionStateAsync(ObservableCollection<CategoryTreeItem> categories)
        {
            try
            {
                var expandedIds = _stateAdapter.CollectExpandedCategoryIds(categories);
                await _treeController.SaveTreeStateAsync(expandedIds);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "TreeControllerAdapter: Failed to save expansion state");
            }
        }

        /// <summary>
        /// Tree helper methods adapted for UI ViewModels
        /// </summary>
        public CategoryTreeItem FindCategoryContainingNote(CategoryTreeItem category, NoteTreeItem note)
        {
            if (category.Notes.Contains(note))
                return category;

            foreach (var subCategory in category.SubCategories)
            {
                var found = FindCategoryContainingNote(subCategory, note);
                if (found != null) return found;
            }
            
            return null;
        }

        public NoteTreeItem FindNoteById(ObservableCollection<CategoryTreeItem> categories, string noteId)
        {
            foreach (var category in categories)
            {
                var found = FindNoteInCategory(category, noteId);
                if (found != null) return found;
            }
            return null;
        }

        public int CountAllCategories(ObservableCollection<CategoryTreeItem> nodes)
        {
            int count = nodes.Count;
            foreach (var node in nodes)
            {
                count += CountAllCategories(node.SubCategories);
            }
            return count;
        }

        public int CountAllNotes(CategoryTreeItem category)
        {
            int count = category.Notes.Count;
            foreach (var sub in category.SubCategories)
            {
                count += CountAllNotes(sub);
            }
            return count;
        }

        public List<CategoryModel> GetAllCategoriesFlat(ObservableCollection<CategoryTreeItem> categories)
        {
            var list = new List<CategoryModel>();
            void Walk(ObservableCollection<CategoryTreeItem> items)
            {
                foreach (var item in items)
                {
                    list.Add(item.Model);
                    Walk(item.SubCategories);
                }
            }
            Walk(categories);
            return list;
        }

        private NoteTreeItem FindNoteInCategory(CategoryTreeItem category, string noteId)
        {
            var note = category.Notes.FirstOrDefault(n => n.Model.Id == noteId);
            if (note != null) return note;

            foreach (var subCategory in category.SubCategories)
            {
                var found = FindNoteInCategory(subCategory, noteId);
                if (found != null) return found;
            }
            
            return null;
        }

        private void SetupFileWatcher()
        {
            try
            {
                // Note: File watcher events will be handled by the calling code
                // This just ensures the watcher is properly configured
                if (_fileWatcher != null)
                {
                    _fileWatcher.StartWatching(PathService.ProjectsPath, "*.*", includeSubdirectories: true);
                    _logger.Debug("TreeControllerAdapter: File watcher configured");
                }
                else
                {
                    _logger.Warning("TreeControllerAdapter: File watcher not initialized");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"TreeControllerAdapter: Failed to setup file watcher: {ex.Message}");
            }
        }

        private void OnCoreTreeChanged(object sender, TreeChangedEventArgs e)
        {
            try
            {
                // Translate core tree events to UI events if needed
                _logger.Debug($"TreeControllerAdapter: Core tree changed - {e.ChangeType}: {e.Details}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"TreeControllerAdapter: Error handling core tree change: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Result of UI tree loading operation
    /// </summary>
    public class TreeUILoadResult
    {
        public TreeUICollections UICollections { get; set; } = null!;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int TotalCategoriesLoaded { get; set; }
    }

    /// <summary>
    /// UI-specific tree change event arguments
    /// </summary>
    public class TreeUIChangedEventArgs : EventArgs
    {
        public TreeUIChangeType ChangeType { get; set; }
        public TreeUICollections? UICollections { get; set; }
        public string? Details { get; set; }
    }

    /// <summary>
    /// Types of UI tree changes
    /// </summary>
    public enum TreeUIChangeType
    {
        Loaded,
        Refreshed,
        StateRestored
    }
}
