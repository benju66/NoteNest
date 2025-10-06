using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using MediatR;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Categories;
using NoteNest.UI.ViewModels.Common;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.ViewModels.Categories
{
    public class CategoryTreeViewModel : ViewModelBase, IDisposable
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly INoteRepository _noteRepository;
        private readonly ITreeRepository _treeRepository;
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

        public CategoryTreeViewModel(
            ICategoryRepository categoryRepository, 
            INoteRepository noteRepository,
            ITreeRepository treeRepository,
            IAppLogger logger)
        {
            _categoryRepository = categoryRepository;
            _noteRepository = noteRepository;
            _treeRepository = treeRepository;
            _logger = logger;
            
            Categories = new ObservableCollection<CategoryViewModel>();
            
            // Initialize debounce timer for expanded state persistence
            _expandedStateTimer = new DispatcherTimer 
            { 
                Interval = TimeSpan.FromMilliseconds(500) 
            };
            _expandedStateTimer.Tick += OnExpandedStateTimer_Tick;
            
            _ = LoadCategoriesAsync();
        }

        public ObservableCollection<CategoryViewModel> Categories { get; }
        
        // Alias for XAML binding compatibility
        public ObservableCollection<CategoryViewModel> RootCategories => Categories;

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
            
            // ✨ CRITICAL FIX: Force cache invalidation BEFORE loading
            // This ensures deleted items are actually removed from the tree
            // Without this, GetAllAsync() returns cached data that includes deleted items
            try
            {
                await _categoryRepository.InvalidateCacheAsync();
                _logger.Debug("Cache invalidated before refresh");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to invalidate cache: {ex.Message}, but continuing with refresh");
            }
            
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
                _logger.Info("Loading categories from repository...");
                
                // Retry mechanism for database initialization timing issues
                var maxRetries = 3;
                var retryDelay = TimeSpan.FromSeconds(2);
                Exception lastException = null;
                
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        var allCategories = await _categoryRepository.GetAllAsync();
                        var rootCategories = await _categoryRepository.GetRootCategoriesAsync();
                        
                        // If we get here successfully, continue with normal processing
                        await ProcessLoadedCategories(allCategories, rootCategories);
                        return; // Success - exit retry loop
                    }
                    catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("no such table"))
                    {
                        lastException = ex;
                        _logger.Warning($"Database not ready on attempt {attempt}/{maxRetries}, retrying in {retryDelay.TotalSeconds}s...");
                        
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
                _logger.Error(ex, "Failed to load categories");
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
                Categories.Clear();
                
                // Create CategoryViewModels for root categories with dependency injection
                foreach (var category in rootCategories)
                {
                    var categoryViewModel = new CategoryViewModel(category, _noteRepository, this, _logger);
                    
                    // Wire up note events to bubble up
                    categoryViewModel.NoteOpenRequested += OnNoteOpenRequested;
                    categoryViewModel.NoteSelectionRequested += OnNoteSelectionRequested;
                    
                    await LoadChildrenAsync(categoryViewModel, allCategories);
                    Categories.Add(categoryViewModel);
                    
                    // ⭐ Load initial expanded state from database (after children loaded)
                    // This doesn't trigger persistence because _isLoadingFromDatabase = true
                    await LoadExpandedStateFromDatabase(categoryViewModel, category);
                }
                
                _logger.Info($"Loaded {Categories.Count} root categories");
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
                var childViewModel = new CategoryViewModel(child, _noteRepository, this, _logger);
                
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
                    var node = await _treeRepository.GetNodeByIdAsync(guid);
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
            await FlushExpandedStateChanges();
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
                    await _treeRepository.BatchUpdateExpandedStatesAsync(guidChanges);
                    _logger.Debug($"✅ Persisted expanded state for {guidChanges.Count} categories");
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
                
                _logger.Debug("Flushed expanded state and disposed timer on dispose");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to flush expanded state on dispose: {ex.Message}");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}
