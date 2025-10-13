using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MediatR;
using NoteNest.Core.Commands;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Collections;
using NoteNest.UI.Plugins.TodoPlugin.Models;
using NoteNest.UI.Plugins.TodoPlugin.Services;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.UI.ViewModels
{
    /// <summary>
    /// View model for the category tree view.
    /// Implements IDisposable for proper event unsubscription and memory leak prevention.
    /// </summary>
    public class CategoryTreeViewModel : ViewModelBase, IDisposable
    {
        private readonly ICategoryStore _categoryStore;
        private readonly ITodoStore _todoStore;
        private readonly IAppLogger _logger;
        private bool _disposed = false;
        
        // Expose for UI operations (delete key, etc.)
        public ICategoryStore CategoryStore => _categoryStore;
        
        private SmartObservableCollection<CategoryNodeViewModel> _categories;
        private CategoryNodeViewModel? _selectedCategory;
        private SmartListNodeViewModel? _selectedSmartList;
        private ObservableCollection<SmartListNodeViewModel> _smartLists;

        public CategoryTreeViewModel(
            ICategoryStore categoryStore,
            ITodoStore todoStore,
            IAppLogger logger)
        {
            _categoryStore = categoryStore ?? throw new ArgumentNullException(nameof(categoryStore));
            _todoStore = todoStore ?? throw new ArgumentNullException(nameof(todoStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _categories = new SmartObservableCollection<CategoryNodeViewModel>();
            _smartLists = new ObservableCollection<SmartListNodeViewModel>();
            
            InitializeCommands();
            InitializeSmartLists();
            
            // Subscribe to CategoryStore changes to refresh tree when categories added
            // Use Dispatcher to ensure UI thread execution
            _categoryStore.Categories.CollectionChanged += OnCategoryStoreChanged;
            
            // Subscribe to TodoStore changes to refresh tree when todos added/updated/removed
            _todoStore.AllTodos.CollectionChanged += OnTodoStoreChanged;
            
            _ = LoadCategoriesAsync();
        }

        #region Properties

        public SmartObservableCollection<CategoryNodeViewModel> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        public ObservableCollection<SmartListNodeViewModel> SmartLists
        {
            get => _smartLists;
            set => SetProperty(ref _smartLists, value);
        }

        public CategoryNodeViewModel? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    if (value != null)
                    {
                        // Clear smart list selection
                        SelectedSmartList = null;
                        CategorySelected?.Invoke(this, value.CategoryId);
                    }
                }
            }
        }

        public SmartListNodeViewModel? SelectedSmartList
        {
            get => _selectedSmartList;
            set
            {
                if (SetProperty(ref _selectedSmartList, value))
                {
                    // Update IsSelected state for all smart lists
                    foreach (var smartList in SmartLists)
                    {
                        smartList.IsSelected = (smartList == value);
                    }
                    
                    if (value != null)
                    {
                        // Clear category selection
                        SelectedCategory = null;
                        SmartListSelected?.Invoke(this, value.ListType);
                    }
                }
            }
        }

        #endregion

        #region Events

        public event EventHandler<Guid>? CategorySelected;
        public event EventHandler<SmartListType>? SmartListSelected;

        #endregion

        #region Commands

        public ICommand CreateCategoryCommand { get; private set; }
        public ICommand RenameCategoryCommand { get; private set; }
        public ICommand DeleteCategoryCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }

        private void InitializeCommands()
        {
            CreateCategoryCommand = new AsyncRelayCommand<CategoryNodeViewModel?>(ExecuteCreateCategory);
            RenameCategoryCommand = new AsyncRelayCommand<CategoryNodeViewModel>(ExecuteRenameCategory);
            DeleteCategoryCommand = new AsyncRelayCommand<CategoryNodeViewModel>(ExecuteDeleteCategory);
            RefreshCommand = new AsyncRelayCommand(LoadCategoriesAsync);
        }

        private async Task ExecuteCreateCategory(CategoryNodeViewModel? parent)
        {
            try
            {
                // TODO: Show input dialog for category name
                var categoryName = "New Category"; // Placeholder
                
                var category = new Category
                {
                    Name = categoryName,
                    ParentId = parent?.CategoryId
                };
                
                _categoryStore.Add(category);
                await LoadCategoriesAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating category");
            }
        }

        private async Task ExecuteRenameCategory(CategoryNodeViewModel? categoryVm)
        {
            if (categoryVm == null) return;

            try
            {
                // TODO: Show input dialog for new name
                var newName = "Renamed Category"; // Placeholder
                
                var category = _categoryStore.GetById(categoryVm.CategoryId);
                if (category != null)
                {
                    category.Name = newName;
                    _categoryStore.Update(category);
                    categoryVm.Name = newName;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error renaming category");
            }
        }

        private async Task ExecuteDeleteCategory(CategoryNodeViewModel? categoryVm)
        {
            if (categoryVm == null) return;

            try
            {
                _categoryStore.Delete(categoryVm.CategoryId);
                await LoadCategoriesAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting category");
            }
        }

        #endregion

        #region Methods

        private void InitializeSmartLists()
        {
            SmartLists.Add(new SmartListNodeViewModel("Today", SmartListType.Today, "\uE916")); // Calendar icon
            SmartLists.Add(new SmartListNodeViewModel("Scheduled", SmartListType.Scheduled, "\uE787")); // Clock icon
            SmartLists.Add(new SmartListNodeViewModel("High Priority", SmartListType.HighPriority, "\uE735")); // Flag icon
            SmartLists.Add(new SmartListNodeViewModel("Favorites", SmartListType.Favorites, "\uE734")); // Star icon
            SmartLists.Add(new SmartListNodeViewModel("All", SmartListType.All, "\uE8FD")); // List icon
            SmartLists.Add(new SmartListNodeViewModel("Completed", SmartListType.Completed, "\uE73E")); // Checkmark icon
            
            // Wire click events for selection
            foreach (var smartList in SmartLists)
            {
                var list = smartList; // Capture for closure
                smartList.SelectCommand = new Core.Commands.RelayCommand(() => SelectedSmartList = list);
            }
            
            // Select Today by default
            SelectedSmartList = SmartLists.FirstOrDefault();
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                // CRITICAL: Ensure TodoStore initialized before querying (lazy, thread-safe)
                await _todoStore.EnsureInitializedAsync();
                
                _logger.Info("[CategoryTree] LoadCategoriesAsync started");
                
                // STEP 0: Save expanded state before rebuild (preserves user's expanded folders)
                var expandedIds = new HashSet<Guid>();
                SaveExpandedState(Categories, expandedIds);
                _logger.Debug($"[CategoryTree] Saved {expandedIds.Count} expanded category IDs");
                
                // Get all categories from store
                var allCategories = _categoryStore.Categories;
                _logger.Info($"[CategoryTree] CategoryStore contains {allCategories.Count} categories");
                
                // Log each category for diagnostics
                foreach (var cat in allCategories)
                {
                    _logger.Debug($"[CategoryTree] Category in store: Id={cat.Id}, Name='{cat.Name}', ParentId={cat.ParentId}, DisplayPath='{cat.DisplayPath}'");
                }
                
                // Build tree structure with BatchUpdate for smooth, flicker-free UI
                using (Categories.BatchUpdate())
                {
                Categories.Clear();
                    
                    // STEP 1: Add "Uncategorized" virtual category at the top
                    var uncategorizedNode = CreateUncategorizedNode();
                    Categories.Add(uncategorizedNode);
                    _logger.Info($"[CategoryTree] Added 'Uncategorized' virtual category with {uncategorizedNode.Todos.Count} todos");
                    
                    // STEP 2: Add regular categories
                    var rootCategories = allCategories.Where(c => c.ParentId == null).ToList();
                    _logger.Info($"[CategoryTree] Found {rootCategories.Count} root categories (ParentId == null)");
                
                foreach (var category in rootCategories)
                {
                        _logger.Debug($"[CategoryTree] Building tree for root category: {category.Name}");
                    var nodeVm = BuildCategoryNode(category, allCategories);
                    Categories.Add(nodeVm);
                        _logger.Debug($"[CategoryTree] Added CategoryNodeViewModel: DisplayPath='{nodeVm.DisplayPath}'");
                    }
                }
                
                // STEP 3: Restore expanded state after rebuild
                RestoreExpandedState(Categories, expandedIds);
                _logger.Debug($"[CategoryTree] Restored expanded state for {expandedIds.Count} categories");
                
                _logger.Info($"[CategoryTree] ✅ LoadCategoriesAsync complete - Categories.Count = {Categories.Count} (including Uncategorized)");
                
                // Note: OnPropertyChanged not needed - ObservableCollection already notifies
                // Keeping for diagnostic purposes
                OnPropertyChanged(nameof(Categories));
                _logger.Debug("[CategoryTree] OnPropertyChanged(Categories) raised");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryTree] ❌ Error loading categories");
            }
            
            await Task.CompletedTask;
        }

        private CategoryNodeViewModel BuildCategoryNode(Category category, ObservableCollection<Category> allCategories, HashSet<Guid>? visited = null, int depth = 0)
        {
            // Circular reference protection (matches main app pattern)
            const int maxDepth = 10;
            if (depth >= maxDepth)
            {
                _logger.Warning($"[CategoryTree] Max depth ({maxDepth}) reached for category: {category.Name} - possible circular reference");
                return new CategoryNodeViewModel(category);
            }
            
            visited ??= new HashSet<Guid>();
            if (!visited.Add(category.Id))
            {
                _logger.Warning($"[CategoryTree] Circular reference detected: {category.Name} already visited");
                return new CategoryNodeViewModel(category);
            }
            
            _logger.Debug($"[CategoryTree] BuildCategoryNode for: '{category.Name}', DisplayPath='{category.DisplayPath}' [Depth: {depth}]");
            
            var nodeVm = new CategoryNodeViewModel(category);
            _logger.Debug($"[CategoryTree] CategoryNodeViewModel created: DisplayPath='{nodeVm.DisplayPath}', Name='{nodeVm.Name}'");
            
            // Find child categories
            var children = allCategories.Where(c => c.ParentId == category.Id).ToList();
            if (children.Any())
            {
                _logger.Debug($"[CategoryTree] Found {children.Count} children for {category.Name}");
            }
            
            foreach (var child in children)
            {
                var childNode = BuildCategoryNode(child, allCategories, visited, depth + 1);
                nodeVm.Children.Add(childNode);
            }
            
            // Load todos for this category
            var categoryTodos = _todoStore.GetByCategory(category.Id);
            if (categoryTodos != null && categoryTodos.Count > 0)
            {
                _logger.Debug($"[CategoryTree] Loading {categoryTodos.Count} todos for category: {category.Name}");
                
                foreach (var todo in categoryTodos)
                {
                    var todoVm = new TodoItemViewModel(todo, _todoStore, _logger);
                    nodeVm.Todos.Add(todoVm);
                }
            }
            
            return nodeVm;
        }
        
        /// <summary>
        /// Creates the "Uncategorized" virtual category node.
        /// This is a special system category (Guid.Empty) that shows orphaned todos.
        /// Orphaned todos = todos with category_id = NULL or category_id not in CategoryStore.
        /// </summary>
        private CategoryNodeViewModel CreateUncategorizedNode()
        {
            // Create virtual category with special ID
            var uncategorizedCategory = new Category
            {
                Id = Guid.Empty, // Special ID for system category
                Name = "Uncategorized",
                DisplayPath = "Uncategorized",
                ParentId = null
            };
            
            var nodeVm = new CategoryNodeViewModel(uncategorizedCategory);
            
            // Query uncategorized todos (all sources):
            // 1. category_id = NULL (never categorized)
            // 2. IsOrphaned = true (source deleted, user soft-deleted)
            // 3. category_id not in CategoryStore (category removed from todo panel)
            // Exclude completed todos (they go to "Completed" smart list)
            var allTodos = _todoStore.AllTodos;
            var categoryIds = _categoryStore.Categories.Select(c => c.Id).ToHashSet();
            
            var uncategorizedTodos = allTodos
                .Where(t => (t.CategoryId == null || 
                             t.IsOrphaned || 
                             !categoryIds.Contains(t.CategoryId.Value)) &&
                            !t.IsCompleted)
                .ToList();
            
            _logger.Debug($"[CategoryTree] Found {uncategorizedTodos.Count} uncategorized/orphaned todos");
            
            // Add uncategorized todos to the node
            foreach (var todo in uncategorizedTodos)
            {
                var todoVm = new TodoItemViewModel(todo, _todoStore, _logger);
                nodeVm.Todos.Add(todoVm);
            }
            
            return nodeVm;
        }
        
        /// <summary>
        /// Recursively saves expanded state of all categories before tree rebuild.
        /// Matches main app CategoryTreeViewModel.SaveExpandedState() pattern.
        /// </summary>
        private void SaveExpandedState(IEnumerable<CategoryNodeViewModel> categories, HashSet<Guid> expandedIds)
        {
            foreach (var category in categories)
            {
                if (category.IsExpanded)
                {
                    expandedIds.Add(category.CategoryId);
                }
                
                // Recurse through children
                if (category.Children.Any())
                {
                    SaveExpandedState(category.Children, expandedIds);
                }
            }
        }
        
        /// <summary>
        /// Recursively restores expanded state of all categories after tree rebuild.
        /// Matches main app CategoryTreeViewModel.RestoreExpandedState() pattern.
        /// </summary>
        private void RestoreExpandedState(IEnumerable<CategoryNodeViewModel> categories, HashSet<Guid> expandedIds)
        {
            foreach (var category in categories)
            {
                if (expandedIds.Contains(category.CategoryId))
                {
                    category.IsExpanded = true;
                }
                
                // Recurse through children
                if (category.Children.Any())
                {
                    RestoreExpandedState(category.Children, expandedIds);
                }
            }
        }
        
        /// <summary>
        /// Event handler for CategoryStore changes - refreshes tree on UI thread.
        /// </summary>
        private void OnCategoryStoreChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_disposed) return;
            
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                _logger.Debug("[CategoryTree] CategoryStore changed, refreshing tree");
                await LoadCategoriesAsync();
            });
        }
        
        /// <summary>
        /// Event handler for TodoStore changes - refreshes tree on UI thread.
        /// </summary>
        private void OnTodoStoreChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_disposed) return;
            
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                _logger.Debug("[CategoryTree] TodoStore changed, refreshing tree");
                await LoadCategoriesAsync();
            });
        }
        
        /// <summary>
        /// Dispose resources and unsubscribe from events to prevent memory leaks.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            
            try
            {
                // Unsubscribe from events
                _categoryStore.Categories.CollectionChanged -= OnCategoryStoreChanged;
                _todoStore.AllTodos.CollectionChanged -= OnTodoStoreChanged;
                
                _disposed = true;
                _logger.Debug("[CategoryTree] Disposed successfully - events unsubscribed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryTree] Error during disposal");
            }
        }

        #endregion
    }

    /// <summary>
    /// View model for category tree nodes.
    /// Follows main app CategoryViewModel pattern with Children + Todos + AllItems composite.
    /// </summary>
    public class CategoryNodeViewModel : ViewModelBase
    {
        private string _name;
        private bool _isExpanded;
        private bool _isSelected;
        private string _displayPath;

        public CategoryNodeViewModel(Category category)
        {
            CategoryId = category.Id;
            _name = category.Name;
            
            // Ensure DisplayPath is never null or empty for UI display
            if (string.IsNullOrWhiteSpace(category.DisplayPath))
            {
                _displayPath = category.Name; // Fallback to Name
            }
            else
            {
                _displayPath = category.DisplayPath;
            }
            
            Children = new ObservableCollection<CategoryNodeViewModel>();
            Todos = new ObservableCollection<TodoItemViewModel>();
            AllItems = new ObservableCollection<object>();
            
            // Subscribe to collection changes to auto-update AllItems
            Children.CollectionChanged += (s, e) => UpdateAllItems();
            Todos.CollectionChanged += (s, e) => UpdateAllItems();
            
            // Initialize commands
            ToggleExpandCommand = new RelayCommand(ToggleExpand);
        }
        
        public ICommand ToggleExpandCommand { get; private set; }
        
        private void ToggleExpand()
        {
            IsExpanded = !IsExpanded;
        }

        public Guid CategoryId { get; }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        
        public string DisplayPath
        {
            get => _displayPath;
            set => SetProperty(ref _displayPath, value);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public ObservableCollection<CategoryNodeViewModel> Children { get; }
        public ObservableCollection<TodoItemViewModel> Todos { get; }
        public ObservableCollection<object> AllItems { get; } // Composite for TreeView binding
        
        /// <summary>
        /// Combines Children and Todos into AllItems for TreeView display.
        /// Follows main app CategoryViewModel.UpdateTreeItems() pattern.
        /// </summary>
        private void UpdateAllItems()
        {
            // Use regular Clear/Add since we don't have BatchUpdate on ObservableCollection
            AllItems.Clear();
            
            // Add child categories first
            foreach (var child in Children)
            {
                AllItems.Add(child);
            }
            
            // Add todos second
            foreach (var todo in Todos)
            {
                AllItems.Add(todo);
            }
        }
        
        public bool HasChildren => Children.Any();
        public bool HasTodos => Todos.Any();
        public bool HasContent => HasChildren || HasTodos;
        public bool HasPotentialContent => HasChildren || HasTodos;  // For expander visibility
    }

    /// <summary>
    /// View model for smart list nodes.
    /// </summary>
    public class SmartListNodeViewModel : ViewModelBase
    {
        private bool _isSelected;

        public SmartListNodeViewModel(string name, SmartListType listType, string iconGlyph)
        {
            Name = name;
            ListType = listType;
            IconGlyph = iconGlyph;
        }

        public string Name { get; }
        public SmartListType ListType { get; }
        public string IconGlyph { get; }
        public ICommand SelectCommand { get; set; } // Set after construction

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
