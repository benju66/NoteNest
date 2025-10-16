using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using MediatR;
using NoteNest.Application.Queries;
using NoteNest.Domain.Categories;
using NoteNest.Domain.Trees;
using NoteNest.UI.ViewModels.Common;
using NoteNest.UI.Controls;
using NoteNest.UI.Collections;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.ViewModels.Categories
{
    /// <summary>
    /// Category tree view model - Event-sourced version.
    /// Uses ITreeQueryService to read from projections.
    /// </summary>
    public class CategoryTreeViewModel : ViewModelBase, IDisposable
    {
        private readonly ITreeQueryService _treeQueryService;
        private readonly IAppLogger _logger;
        private CategoryViewModel _selectedCategory;
        private NoteItemViewModel _selectedNote;
        private object _selectedItem;
        private bool _isLoading;
        
        // Expanded state persistence with debouncing
        private readonly Dictionary<string, bool> _pendingExpandedChanges = new();
        private readonly object _expandedChangesLock = new object();
        private DispatcherTimer _expandedStateTimer;
        private bool _isLoadingFromDatabase = false;
        private bool _disposed = false;
        
        // Drag & drop support
        private TreeViewDragHandler _dragHandler;

        public CategoryTreeViewModel(
            ITreeQueryService treeQueryService,
            IAppLogger logger)
        {
            _treeQueryService = treeQueryService;
            _logger = logger;
            
            Categories = new SmartObservableCollection<CategoryViewModel>();
            
            // Initialize debounce timer for expanded state persistence
            _expandedStateTimer = new DispatcherTimer 
            { 
                Interval = TimeSpan.FromMilliseconds(500) 
            };
            _expandedStateTimer.Tick += OnExpandedStateTimer_Tick;
            
            _ = LoadCategoriesAsync();
        }

        public SmartObservableCollection<CategoryViewModel> Categories { get; }
        
        // Alias for XAML binding compatibility
        public SmartObservableCollection<CategoryViewModel> RootCategories => Categories;

        /// <summary>
        /// Currently selected item in the TreeView (can be CategoryViewModel or NoteItemViewModel)
        /// </summary>
        public object SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    // Update typed properties based on selection
                    if (value is CategoryViewModel category)
                    {
                        SelectedCategory = category;
                        SelectedNote = null;
                    }
                    else if (value is NoteItemViewModel note)
                    {
                        SelectedNote = note;
                        SelectedCategory = null;
                    }
                    else
                    {
                        SelectedCategory = null;
                        SelectedNote = null;
                    }
                    
                    // Fire unified selection event
                    SelectionChanged?.Invoke(value);
                }
            }
        }

        /// <summary>
        /// Currently selected category (null if a note is selected)
        /// </summary>
        public CategoryViewModel SelectedCategory
        {
            get => _selectedCategory;
            private set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    CategorySelected?.Invoke(value);
                }
            }
        }

        /// <summary>
        /// Currently selected note (null if a category is selected)
        /// </summary>
        public NoteItemViewModel SelectedNote
        {
            get => _selectedNote;
            private set
            {
                if (SetProperty(ref _selectedNote, value))
                {
                    NoteSelected?.Invoke(value);
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        // Selection events
        public event Action<object> SelectionChanged;
        public event Action<CategoryViewModel> CategorySelected;
        public event Action<NoteItemViewModel> NoteSelected;
        public event Action<NoteItemViewModel> NoteOpenRequested;

        public async Task RefreshAsync()
        {
            // Save expanded state and selection before refresh
            var expandedIds = new HashSet<string>();
            SaveExpandedState(Categories, expandedIds);
            
            // Save current selection (could be category or note)
            var selectedItemId = SelectedNote?.Id ?? SelectedCategory?.Id;
            var isNoteSelected = SelectedNote != null;
            
            // ✨ Cache invalidation for projection queries
            _treeQueryService.InvalidateCache();
            _logger.Debug("Cache invalidated before refresh");
            
            await LoadCategoriesAsync();
            
            // ✨ FIX FOR NOTE DELETION: Refresh notes in expanded categories
            // This ensures deleted notes are removed from the UI
            // Must happen BEFORE restoring expanded state to avoid double loading
            await RefreshNotesInExpandedCategoriesAsync(Categories);
            _logger.Debug("Refreshed notes in expanded categories");
            
            // Restore expanded state after refresh
            RestoreExpandedState(Categories, expandedIds);
            
            // Restore selection (handle both categories and notes)
            if (!string.IsNullOrEmpty(selectedItemId))
            {
                if (isNoteSelected)
                {
                    // Find and restore note selection
                    var note = FindNoteById(Categories, selectedItemId);
                    if (note != null)
                    {
                        SelectedItem = note;
                    }
                }
                else
                {
                    // Find and restore category selection
                    var category = FindCategoryById(Categories, selectedItemId);
                    if (category != null)
                    {
                        SelectedItem = category;
                    }
                }
            }
        }
        
        private void SaveExpandedState(ObservableCollection<CategoryViewModel> categories, HashSet<string> expandedIds)
        {
            foreach (var category in categories)
            {
                if (category.IsExpanded)
                {
                    expandedIds.Add(category.Id);
                }
                
                if (category.Children.Any())
                {
                    SaveExpandedState(category.Children, expandedIds);
                }
            }
        }
        
        private void RestoreExpandedState(ObservableCollection<CategoryViewModel> categories, HashSet<string> expandedIds)
        {
            foreach (var category in categories)
            {
                if (expandedIds.Contains(category.Id))
                {
                    category.IsExpanded = true;
                }
                
                if (category.Children.Any())
                {
                    RestoreExpandedState(category.Children, expandedIds);
                }
            }
        }
        
        private CategoryViewModel FindCategoryById(ObservableCollection<CategoryViewModel> categories, string id)
        {
            foreach (var category in categories)
            {
                if (category.Id == id)
                    return category;
                
                var found = FindCategoryById(category.Children, id);
                if (found != null)
                    return found;
            }
            return null;
        }
        
        private NoteItemViewModel FindNoteById(ObservableCollection<CategoryViewModel> categories, string id)
        {
            foreach (var category in categories)
            {
                // Search in this category's notes
                var note = category.Notes.FirstOrDefault(n => n.Id == id);
                if (note != null)
                    return note;
                
                // Recursively search in child categories
                var found = FindNoteById(category.Children, id);
                if (found != null)
                    return found;
            }
            return null;
        }
        
        /// <summary>
        /// Refreshes notes in all expanded categories to ensure deleted notes are removed from UI
        /// This is necessary because CategoryViewModel caches notes and doesn't automatically refresh
        /// </summary>
        private async Task RefreshNotesInExpandedCategoriesAsync(ObservableCollection<CategoryViewModel> categories)
        {
            foreach (var category in categories)
            {
                // Only refresh notes for expanded categories (performance optimization)
                if (category.IsExpanded)
                {
                    await category.RefreshNotesAsync(); // This clears cached notes and reloads if expanded
                    _logger.Debug($"Refreshed notes for expanded category: {category.Name}");
                }
                
                // Recursively refresh child categories
                if (category.Children.Any())
                {
                    await RefreshNotesInExpandedCategoriesAsync(category.Children);
                }
            }
        }

        public void SelectNote(NoteItemViewModel note)
        {
            if (note != null)
            {
                NoteSelected?.Invoke(note);
                _logger.Debug($"Note selected: {note.Title}");
            }
        }

        public void OpenNote(NoteItemViewModel note)
        {
            if (note != null)
            {
                NoteOpenRequested?.Invoke(note);
                _logger.Info($"Note open requested: {note.Title}");
            }
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                IsLoading = true;
                _logger.Info("Loading categories from projection...");
                
                // Retry mechanism for database initialization timing issues
                var maxRetries = 3;
                var retryDelay = TimeSpan.FromSeconds(2);
                Exception lastException = null;
                
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        // Load from event-sourced projection
                        var allNodes = await _treeQueryService.GetAllNodesAsync();
                        var categoryNodes = allNodes.Where(n => n.NodeType == TreeNodeType.Category).ToList();
                        var rootNodes = await _treeQueryService.GetRootNodesAsync();
                        var rootCategories = rootNodes.Where(n => n.NodeType == TreeNodeType.Category).ToList();
                        
                        // Convert TreeNodes to Category domain objects
                        var allCategories = categoryNodes.Select(ConvertTreeNodeToCategory).Where(c => c != null).ToList();
                        var rootCategoryList = rootCategories.Select(ConvertTreeNodeToCategory).Where(c => c != null).ToList();
                        
                        // Process with existing logic
                        await ProcessLoadedCategories(allCategories, rootCategoryList);
                        return; // Success - exit retry loop
                    }
                    catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("no such table"))
                    {
                        lastException = ex;
                        _logger.Warning($"Projection database not ready on attempt {attempt}/{maxRetries}, retrying in {retryDelay.TotalSeconds}s...");
                        
                        if (attempt < maxRetries)
                        {
                            await Task.Delay(retryDelay);
                        }
                    }
                }
                
                // If all retries failed, throw the last exception
                throw lastException;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load categories from projection");
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        private async Task ProcessLoadedCategories(IReadOnlyList<Domain.Categories.Category> allCategories, IReadOnlyList<Domain.Categories.Category> rootCategories)
        {
            _isLoadingFromDatabase = true;  // Prevent expand events during initial load
            
            try
            {
                // ✨ SMOOTH UX: Batch update eliminates tree flickering
                using (Categories.BatchUpdate())
                {
                    Categories.Clear();
                    
                    // Create all CategoryViewModels first (faster)
                    var categoryViewModels = new List<CategoryViewModel>();
                    
                    foreach (var category in rootCategories)
                    {
                        // Pass null for INoteRepository - note loading will be added later via query service
                        var categoryViewModel = new CategoryViewModel(category, null, this, _logger);
                        
                        // Wire up note events to bubble up
                        categoryViewModel.NoteOpenRequested += OnNoteOpenRequested;
                        categoryViewModel.NoteSelectionRequested += OnNoteSelectionRequested;
                        
                        await LoadChildrenAsync(categoryViewModel, allCategories);
                        categoryViewModels.Add(categoryViewModel);
                        
                        // ⭐ Load initial expanded state from database (after children loaded)
                        // This doesn't trigger persistence because _isLoadingFromDatabase = true
                        await LoadExpandedStateFromDatabase(categoryViewModel, category);
                    }
                    
                    // Add all at once - single UI update
                    Categories.AddRange(categoryViewModels);
                }
                
                _logger.Info($"Loaded {Categories.Count} root categories with smooth batching");
            }
            finally
            {
                _isLoadingFromDatabase = false;  // Re-enable expand event handling
            }
        }
        
        private async Task LoadChildrenAsync(CategoryViewModel parentViewModel, IReadOnlyList<Category> allCategories)
        {
            var children = allCategories.Where(c => c.ParentId?.Value == parentViewModel.Id).ToList();
            
            foreach (var child in children)
            {
                // Pass null for INoteRepository - note loading via query service will be added later
                var childViewModel = new CategoryViewModel(child, null, this, _logger);
                
                // Wire up note events for child categories too
                childViewModel.NoteOpenRequested += OnNoteOpenRequested;
                childViewModel.NoteSelectionRequested += OnNoteSelectionRequested;
                
                await LoadChildrenAsync(childViewModel, allCategories);
                parentViewModel.Children.Add(childViewModel);
                
                // ⭐ Load expanded state for child
                await LoadExpandedStateFromDatabase(childViewModel, child);
            }
        }
        
        private async Task LoadExpandedStateFromDatabase(CategoryViewModel viewModel, Category category)
        {
            try
            {
                // Get fresh node from database to read is_expanded
                if (Guid.TryParse(category.Id.Value, out var guid))
                {
                    var node = await _treeQueryService.GetByIdAsync(guid);
                    if (node != null && node.NodeType == Domain.Trees.TreeNodeType.Category)
                    {
                        // Set expanded state (won't trigger persistence because _isLoadingFromDatabase = true)
                        viewModel.IsExpanded = node.IsExpanded;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to load expanded state for {category.Name}: {ex.Message}");
                // Non-critical - defaults to collapsed
            }
        }

        // =============================================================================
        // NOTE EVENT HANDLERS - Forward to MainShellViewModel
        // =============================================================================

        private void OnNoteOpenRequested(NoteItemViewModel note)
        {
            NoteOpenRequested?.Invoke(note);
        }

        private void OnNoteSelectionRequested(NoteItemViewModel note)
        {
            // Update the unified selection
            SelectedItem = note;
        }

        // =============================================================================
        // EXPANDED STATE PERSISTENCE (Debounced)
        // =============================================================================

        /// <summary>
        /// Called by CategoryViewModel when user expands/collapses a folder.
        /// Queues the change for debounced batch persistence.
        /// </summary>
        public void OnCategoryExpandedChanged(string categoryId, bool isExpanded)
        {
            if (_isLoadingFromDatabase) return;  // Prevent circular updates during load
            
            lock (_expandedChangesLock)
            {
                _pendingExpandedChanges[categoryId] = isExpanded;
            }
            
            // Restart debounce timer
            _expandedStateTimer.Stop();
            _expandedStateTimer.Start();
        }

        private async void OnExpandedStateTimer_Tick(object sender, EventArgs e)
        {
            _expandedStateTimer.Stop();
            
            try
            {
                await FlushExpandedStateChanges();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to flush expanded state changes");
                // Non-critical failure - expanded state will be persisted on next change or dispose
            }
        }

        private async Task FlushExpandedStateChanges()
        {
            Dictionary<string, bool> changesToFlush;
            
            lock (_expandedChangesLock)
            {
                if (_pendingExpandedChanges.Count == 0) return;
                
                changesToFlush = new Dictionary<string, bool>(_pendingExpandedChanges);
                _pendingExpandedChanges.Clear();
            }
            
            try
            {
                var guidChanges = changesToFlush
                    .Where(kvp => Guid.TryParse(kvp.Key, out _))
                    .ToDictionary(
                        kvp => Guid.Parse(kvp.Key), 
                        kvp => kvp.Value);
                
                if (guidChanges.Count > 0)
                {
                    // TODO: Expanded state persistence via event sourcing
                    // For now, expanded state is not persisted (acceptable for MVP)
                    _logger.Debug($"📝 Expanded state changes detected for {guidChanges.Count} categories (persistence deferred)");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to persist expanded states");
                // Non-critical - UI state already changed, DB is just cache
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            try
            {
                // Immediately flush any pending changes on shutdown
                _expandedStateTimer?.Stop();
                FlushExpandedStateChanges().GetAwaiter().GetResult();
                
                // Properly dispose timer
                _expandedStateTimer = null;
                
                // Dispose drag handler
                _dragHandler?.Dispose();
                _dragHandler = null;
                
                _logger.Debug("Flushed expanded state and disposed resources");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to dispose resources: {ex.Message}");
            }
            finally
            {
                _disposed = true;
            }
        }

        // =============================================================================
        // DRAG & DROP SUPPORT
        // =============================================================================

        /// <summary>
        /// Enables drag & drop for the tree view.
        /// Should be called from code-behind when TreeView is loaded.
        /// </summary>
        public void EnableDragDrop(TreeView treeView, CategoryOperationsViewModel categoryOperations)
        {
            if (treeView == null || categoryOperations == null)
                return;

            // Create drag handler with validation and drop callbacks
            _dragHandler = new TreeViewDragHandler(
                treeView,
                canDropCallback: (source, target) => CanDrop(source, target),
                dropCallback: async (source, target) => await OnDrop(source, target, categoryOperations)
            );

            _logger.Info("Drag & drop enabled for tree view");
        }
        
        /// <summary>
        /// Moves a note to a different category in the tree WITHOUT full refresh.
        /// Provides smooth UX with no flickering using batched updates.
        /// </summary>
        public async Task MoveNoteInTreeAsync(string noteId, string sourceCategoryId, string targetCategoryId)
        {
            try
            {
                // Find source category
                var sourceCategory = FindCategoryById(sourceCategoryId);
                if (sourceCategory == null)
                {
                    _logger.Warning($"Source category not found: {sourceCategoryId}");
                    await RefreshAsync(); // Fallback to full refresh
                    return;
                }
                
                // Find target category
                var targetCategory = FindCategoryById(targetCategoryId);
                if (targetCategory == null)
                {
                    _logger.Warning($"Target category not found: {targetCategoryId}");
                    await RefreshAsync(); // Fallback to full refresh
                    return;
                }
                
                // Find the note in source category
                var noteViewModel = sourceCategory.Notes.FirstOrDefault(n => n.Id == noteId);
                if (noteViewModel == null)
                {
                    _logger.Warning($"Note not found in source category: {noteId}");
                    await RefreshAsync(); // Fallback to full refresh
                    return;
                }
                
                // ✨ SMOOTH UX: Batch the remove/add operations
                using (sourceCategory.Notes.BatchUpdate())
                using (targetCategory.Notes.BatchUpdate())
                {
                    // Remove from source
                    sourceCategory.Notes.Remove(noteViewModel);
                    
                    // Ensure target category has loaded its notes
                    if (!targetCategory.IsExpanded)
                    {
                        await targetCategory.ExpandAsync();
                    }
                    
                    // Reload notes in target to get the moved note from database
                    await targetCategory.RefreshNotesAsync();
                }
                
                _logger.Info($"Smoothly moved note {noteId} from {sourceCategoryId} to {targetCategoryId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to move note in tree");
                await RefreshAsync(); // Fallback to full refresh on error
            }
        }
        
        /// <summary>
        /// Moves a category to a new parent in the tree WITHOUT full refresh.
        /// Provides smooth UX with no flickering using batched updates.
        /// </summary>
        public async Task MoveCategoryInTreeAsync(string categoryId, string oldParentId, string newParentId)
        {
            try
            {
                // Find the category to move
                var categoryToMove = FindCategoryById(categoryId);
                if (categoryToMove == null)
                {
                    _logger.Warning($"Category to move not found: {categoryId}");
                    await RefreshAsync(); // Fallback to full refresh
                    return;
                }
                
                // Find old parent (or root)
                CategoryViewModel oldParent = string.IsNullOrEmpty(oldParentId) 
                    ? null 
                    : FindCategoryById(oldParentId);
                
                // Find new parent (or root)
                CategoryViewModel newParent = string.IsNullOrEmpty(newParentId) 
                    ? null 
                    : FindCategoryById(newParentId);
                
                // ✨ SMOOTH UX: Batch all collection updates
                var batches = new List<IDisposable>();
                try
                {
                    // Start batching on all affected collections
                    if (oldParent != null)
                        batches.Add(oldParent.Children.BatchUpdate());
                    else
                        batches.Add(Categories.BatchUpdate());
                        
                    if (newParent != null)
                        batches.Add(newParent.Children.BatchUpdate());
                    else if (oldParent != null) // Don't double-batch root
                        batches.Add(Categories.BatchUpdate());
                    
                    // Remove from old location
                    if (oldParent != null)
                    {
                        oldParent.Children.Remove(categoryToMove);
                    }
                    else
                    {
                        Categories.Remove(categoryToMove);
                    }
                    
                    // Add to new location
                    if (newParent != null)
                    {
                        // Ensure parent is expanded to show the moved category
                        if (!newParent.IsExpanded)
                        {
                            await newParent.ExpandAsync();
                        }
                        newParent.Children.Add(categoryToMove);
                    }
                    else
                    {
                        Categories.Add(categoryToMove);
                    }
                }
                finally
                {
                    // Dispose all batches - triggers single UI update
                    foreach (var batch in batches)
                        batch.Dispose();
                }
                
                _logger.Info($"Smoothly moved category {categoryId} from {oldParentId ?? "root"} to {newParentId ?? "root"}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to move category in tree");
                await RefreshAsync(); // Fallback to full refresh on error
            }
        }

        /// <summary>
        /// Validates whether a drop operation is allowed.
        /// </summary>
        private bool CanDrop(object source, object target)
        {
            // Can't drop on self
            if (source == target)
                return false;

            // CASE 1: Moving a note to a category
            if (source is NoteItemViewModel && target is CategoryViewModel)
                return true;

            // CASE 2: Moving a category to another category
            if (source is CategoryViewModel sourceCategory && target is CategoryViewModel targetCategory)
            {
                // Can't drop into own descendant (circular reference check)
                if (IsDescendant(targetCategory, sourceCategory))
                    return false;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Executes the drop operation.
        /// </summary>
        private async Task OnDrop(object source, object target, CategoryOperationsViewModel categoryOperations)
        {
            try
            {
                if (source is NoteItemViewModel note && target is CategoryViewModel targetCategory)
                {
                    // Move note to category
                    categoryOperations.MoveNoteCommand.Execute((note, targetCategory));
                }
                else if (source is CategoryViewModel sourceCategory && target is CategoryViewModel targetCat)
                {
                    // Move category to new parent
                    categoryOperations.MoveCategoryCommand.Execute((sourceCategory, targetCat));
                }
                
                // Give UI time to update
                await Task.Delay(10);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to execute drop operation");
            }
        }

        /// <summary>
        /// Checks if a category is a descendant of another category.
        /// Prevents circular reference moves.
        /// </summary>
        private bool IsDescendant(CategoryViewModel potential, CategoryViewModel ancestor)
        {
            var current = potential;
            var visited = new HashSet<string>();

            while (current != null)
            {
                if (current.Id == ancestor.Id)
                    return true;

                // Safety check: prevent infinite loops
                if (!visited.Add(current.Id))
                    break;

                // Find parent
                current = FindCategoryById(current.ParentId);
            }

            return false;
        }

        /// <summary>
        /// Finds a category by ID in the tree.
        /// </summary>
        private CategoryViewModel FindCategoryById(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId))
                return null;

            var queue = new Queue<CategoryViewModel>(Categories);
            while (queue.Count > 0)
            {
                var category = queue.Dequeue();
                if (category.Id == categoryId)
                    return category;

                foreach (var child in category.Children)
                    queue.Enqueue(child);
            }

            return null;
        }
        
        /// <summary>
        /// Convert TreeNode from projection to Category domain object.
        /// </summary>
        private Category ConvertTreeNodeToCategory(TreeNode treeNode)
        {
            if (treeNode?.NodeType != TreeNodeType.Category)
                return null;

            try
            {
                var categoryId = CategoryId.From(treeNode.Id.ToString());
                var parentId = treeNode.ParentId.HasValue 
                    ? CategoryId.From(treeNode.ParentId.Value.ToString())
                    : null;

                // Use the absolute path from tree node as the category path
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
