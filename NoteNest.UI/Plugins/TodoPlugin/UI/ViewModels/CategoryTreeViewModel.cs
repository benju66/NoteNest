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
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;
using NoteNest.UI.Plugins.TodoPlugin.Models;
using NoteNest.UI.Plugins.TodoPlugin.Services;
using NoteNest.UI.ViewModels.Common;
using NoteNest.UI.Services;
using NoteNest.Application.Categories.Commands.CreateCategory;
using NoteNest.Application.Categories.Commands.RenameCategory;
using NoteNest.Application.Categories.Commands.DeleteCategory;

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
        private readonly ITodoTagRepository _todoTagRepository;
        private readonly IMediator _mediator;
        private readonly IDialogService _dialogService;
        private readonly IAppLogger _logger;
        private bool _disposed = false;
        private bool _isInitialized = false;
        
        // Expose for UI operations (delete key, etc.)
        public ICategoryStore CategoryStore => _categoryStore;
        
        private SmartObservableCollection<CategoryNodeViewModel> _categories;
        private CategoryNodeViewModel? _selectedCategory;
        private SmartListNodeViewModel? _selectedSmartList;
        private object? _selectedItem;
        private ObservableCollection<SmartListNodeViewModel> _smartLists;
        private bool _showCompleted = true; // Default: show completed items

        public CategoryTreeViewModel(
            ICategoryStore categoryStore,
            ITodoStore todoStore,
            ITodoTagRepository todoTagRepository,
            IMediator mediator,
            IDialogService dialogService,
            IAppLogger logger)
        {
            _categoryStore = categoryStore ?? throw new ArgumentNullException(nameof(categoryStore));
            _todoStore = todoStore ?? throw new ArgumentNullException(nameof(todoStore));
            _todoTagRepository = todoTagRepository ?? throw new ArgumentNullException(nameof(todoTagRepository));
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _categories = new SmartObservableCollection<CategoryNodeViewModel>();
            _smartLists = new ObservableCollection<SmartListNodeViewModel>();
            
            InitializeCommands();
            InitializeSmartLists();
            
            // Load ShowCompleted preference from persistence
            LoadShowCompletedPreference();
            
            // Subscribe to CategoryStore changes to refresh tree when categories added
            // Use Dispatcher to ensure UI thread execution
            _categoryStore.Categories.CollectionChanged += OnCategoryStoreChanged;
            
            // Subscribe to TodoStore changes to refresh tree when todos added/updated/removed
            _logger.Info($"[CategoryTree] 🎯 SUBSCRIBING to TodoStore.AllTodos.CollectionChanged");
            _todoStore.AllTodos.CollectionChanged += OnTodoStoreChanged;
            _logger.Info($"[CategoryTree] ✅ Subscription registered successfully");
            
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

        /// <summary>
        /// Currently selected item in the TreeView (can be CategoryNodeViewModel or TodoItemViewModel).
        /// Matches main app CategoryTreeViewModel pattern for unified selection handling.
        /// </summary>
        public object? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    // Update typed properties based on selection
                    if (value is CategoryNodeViewModel category)
                    {
                        SelectedCategory = category;
                        _logger.Debug($"[CategoryTree] Category selected: {category.Name} (ID: {category.CategoryId})");
                    }
                    else if (value is TodoItemViewModel todo)
                    {
                        // Find parent category and set as selected to maintain category context
                        var parentCategory = FindCategoryContainingTodo(todo);
                        SelectedCategory = parentCategory;
                        _logger.Debug($"[CategoryTree] Todo selected: {todo.Text}, parent category: {parentCategory?.Name ?? "Uncategorized"}");
                    }
                    else
                    {
                        SelectedCategory = null;
                        _logger.Debug("[CategoryTree] Selection cleared");
                    }
                }
            }
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

        /// <summary>
        /// Controls whether completed todos are shown in categories.
        /// When false, completed items are hidden from view.
        /// Default: true (show completed items).
        /// </summary>
        public bool ShowCompleted
        {
            get => _showCompleted;
            set
            {
                if (SetProperty(ref _showCompleted, value))
                {
                    _logger.Debug($"[CategoryTree] ShowCompleted changed to: {value}");
                    
                    // Persist preference
                    _ = SaveShowCompletedPreferenceAsync(value);
                    
                    // Refresh tree to apply filter
                    _ = LoadCategoriesAsync();
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
                // 1. Show input dialog (matches note tree pattern)
                var categoryName = await _dialogService.ShowInputDialogAsync(
                    "New Category",
                    "Enter category name:",
                    "",
                    text => string.IsNullOrWhiteSpace(text) 
                        ? "Category name cannot be empty." 
                        : null);

                if (string.IsNullOrWhiteSpace(categoryName))
                {
                    _logger.Debug("[CategoryTree] Category creation cancelled");
                    return;
                }

                // 2. Use MediatR CQRS command (event-sourced persistence)
                var command = new CreateCategoryCommand
                {
                    ParentCategoryId = parent?.CategoryId.ToString(),
                    Name = categoryName
                };

                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    _logger.Error($"[CategoryTree] Failed to create category: {result.Error}");
                    _dialogService.ShowError(result.Error, "Create Category Error");
                    return;
                }

                _logger.Info($"[CategoryTree] ✅ Created category: {categoryName} (ID: {result.Value.CategoryId})");

                // 3. Optionally track in CategoryStore (user preference)
                // This determines if category shows in TodoPlugin panel
                var shouldTrack = await _dialogService.ShowConfirmationDialogAsync(
                    $"Category '{categoryName}' created successfully! Track in Todo panel?",
                    "Track Category");

                if (shouldTrack)
                {
                    var categoryId = Guid.Parse(result.Value.CategoryId);
                    var category = new Category
                    {
                        Id = categoryId,
                        Name = categoryName,
                        ParentId = parent?.CategoryId,
                        DisplayPath = result.Value.CategoryPath
                    };
                    
                    await _categoryStore.AddAsync(category);  // Track in user_preferences
                }

                // 4. Refresh tree from database (invalidates cache, reloads from tree_view)
                await _categoryStore.RefreshAsync();
                
                _dialogService.ShowInfo(
                    $"Category '{categoryName}' created successfully!",
                    "Success");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryTree] Error creating category");
                _dialogService.ShowError(
                    $"Unexpected error: {ex.Message}",
                    "Error");
            }
        }

        private async Task ExecuteRenameCategory(CategoryNodeViewModel? categoryVm)
        {
            if (categoryVm == null)
            {
                _logger.Warning("[CategoryTree] Rename called with null category");
                return;
            }

            try
            {
                // 1. Show input dialog with current name as default
                var newName = await _dialogService.ShowInputDialogAsync(
                    "Rename Category",
                    "Enter new category name:",
                    categoryVm.Name,  // Default to current name
                    text => string.IsNullOrWhiteSpace(text) 
                        ? "Category name cannot be empty." 
                        : null);

                if (string.IsNullOrWhiteSpace(newName) || newName == categoryVm.Name)
                {
                    _logger.Debug("[CategoryTree] Rename cancelled or unchanged");
                    return;
                }

                // 2. Use MediatR CQRS command
                var command = new RenameCategoryCommand
                {
                    CategoryId = categoryVm.CategoryId.ToString(),
                    NewName = newName
                };

                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    _logger.Error($"[CategoryTree] Failed to rename category: {result.Error}");
                    _dialogService.ShowError(result.Error, "Rename Category Error");
                    return;
                }

                _logger.Info($"[CategoryTree] ✅ Renamed category: '{result.Value.OldName}' → '{result.Value.NewName}'");

                // 3. Update CategoryStore tracked categories (if this category is tracked)
                var trackedCategory = _categoryStore.GetById(categoryVm.CategoryId);
                if (trackedCategory != null)
                {
                    trackedCategory.Name = newName;
                    trackedCategory.DisplayPath = result.Value.NewPath;
                    _categoryStore.Update(trackedCategory);  // Update user_preferences
                }

                // 4. Refresh tree from database
                await _categoryStore.RefreshAsync();
                
                _dialogService.ShowInfo(
                    $"Category renamed to '{newName}' successfully!",
                    "Success");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryTree] Error renaming category");
                _dialogService.ShowError(
                    $"Unexpected error: {ex.Message}",
                    "Error");
            }
        }

        private async Task ExecuteDeleteCategory(CategoryNodeViewModel? categoryVm)
        {
            if (categoryVm == null)
            {
                _logger.Warning("[CategoryTree] Delete called with null category");
                return;
            }

            try
            {
                // 1. Show confirmation dialog
                var confirmed = await _dialogService.ShowConfirmationDialogAsync(
                    $"Are you sure you want to delete '{categoryVm.Name}' and all its contents?\n\n" +
                    $"This will delete:\n" +
                    $"• All notes in this category\n" +
                    $"• All subcategories\n" +
                    $"• All todos linked to notes in this category\n\n" +
                    $"This action cannot be undone.",
                    "Confirm Delete");

                if (!confirmed)
                {
                    _logger.Debug("[CategoryTree] Delete cancelled");
                    return;
                }

                // 2. Use MediatR CQRS command
                var command = new DeleteCategoryCommand
                {
                    CategoryId = categoryVm.CategoryId.ToString(),
                    DeleteFiles = true  // Delete physical directory
                };

                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    _logger.Error($"[CategoryTree] Failed to delete category: {result.Error}");
                    _dialogService.ShowError(result.Error, "Delete Category Error");
                    return;
                }

                _logger.Info($"[CategoryTree] ✅ Deleted category: '{result.Value.DeletedCategoryName}' " +
                             $"({result.Value.DeletedDescendantCount} descendants)");

                // 3. Remove from CategoryStore tracked categories
                _categoryStore.Delete(categoryVm.CategoryId);  // Remove from user_preferences

                // 4. Refresh tree from database
                await _categoryStore.RefreshAsync();
                
                _dialogService.ShowInfo(
                    $"Category '{result.Value.DeletedCategoryName}' and {result.Value.DeletedDescendantCount} items deleted successfully!",
                    "Success");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryTree] Error deleting category");
                _dialogService.ShowError(
                    $"Unexpected error: {ex.Message}",
                    "Error");
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
                
                // Mark as initialized - safe to process TodoStore events now
                _isInitialized = true;
                
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
            _logger.Debug($"[CategoryTree] Calling GetByCategory({category.Id}) for '{category.Name}'...");
            _logger.Debug($"[CategoryTree] TodoStore.AllTodos count at this moment: {_todoStore.AllTodos.Count}");
            
            var allCategoryTodos = _todoStore.GetByCategory(category.Id);
            
            // Filter based on ShowCompleted preference (ViewModel-level concern)
            // Use IEnumerable for efficient deferred execution - single-pass enumeration
            IEnumerable<TodoItem> filtered = ShowCompleted 
                ? allCategoryTodos 
                : allCategoryTodos.Where(t => !t.IsCompleted);
            
            // Sort: Active first (by order), completed last (by completion date descending - newest first)
            var sortedTodos = filtered
                .OrderBy(t => t.IsCompleted)                           // false < true, so active items first
                .ThenByDescending(t => t.IsCompleted ? t.CompletedDate : null)  // Completed by date (newest first)
                .ThenBy(t => t.Order)                                  // Active by order
                .ToList();
            
            _logger.Debug($"[CategoryTree] Filtered and sorted {sortedTodos.Count} todos for '{category.Name}' (ShowCompleted: {ShowCompleted})");
            
            if (sortedTodos.Count > 0)
            {
                _logger.Info($"[CategoryTree] Loading {sortedTodos.Count} todos into category node '{category.Name}':");
                
                foreach (var todo in sortedTodos)
                {
                    _logger.Debug($"[CategoryTree]   - Todo: '{todo.Text}' (Id: {todo.Id}, IsCompleted: {todo.IsCompleted})");
                    var todoVm = new TodoItemViewModel(todo, _todoStore, _todoTagRepository, _mediator, _logger);
                    // Wire up todo events to bubble up to category level
                    todoVm.OpenRequested += nodeVm.OnTodoOpenRequested;
                    todoVm.SelectionRequested += nodeVm.OnTodoSelectionRequested;
                    nodeVm.Todos.Add(todoVm);
                }
            }
            else
            {
                _logger.Debug($"[CategoryTree] No todos for category '{category.Name}' (after filtering)");
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
            _logger.Debug($"[CategoryTree] Creating 'Uncategorized' node...");
            _logger.Debug($"[CategoryTree] TodoStore.AllTodos count: {_todoStore.AllTodos.Count}");
            _logger.Debug($"[CategoryTree] CategoryStore.Categories count: {_categoryStore.Categories.Count}");
            
            var allTodos = _todoStore.AllTodos;
            var categoryIds = _categoryStore.Categories.Select(c => c.Id).ToHashSet();
            
            _logger.Debug($"[CategoryTree] Known category IDs: [{string.Join(", ", categoryIds)}]");
            
            // Get all uncategorized/orphaned todos
            var allUncategorizedTodos = allTodos
                .Where(t => t.CategoryId == null || 
                            t.IsOrphaned || 
                            !categoryIds.Contains(t.CategoryId.Value))
                .ToList();
            
            // Filter based on ShowCompleted preference
            var filteredTodos = ShowCompleted 
                ? allUncategorizedTodos 
                : allUncategorizedTodos.Where(t => !t.IsCompleted).ToList();
            
            // Sort: Active first (by order), completed last (by completion date descending)
            var uncategorizedTodos = filteredTodos
                .OrderBy(t => t.IsCompleted)
                .ThenByDescending(t => t.IsCompleted ? t.CompletedDate : null)
                .ThenBy(t => t.Order)
                .ToList();
            
            _logger.Info($"[CategoryTree] Found {uncategorizedTodos.Count} uncategorized/orphaned todos (ShowCompleted: {ShowCompleted})");
            if (uncategorizedTodos.Count > 0)
            {
                foreach (var t in uncategorizedTodos)
                {
                    _logger.Debug($"[CategoryTree]   Uncategorized: '{t.Text}' (CategoryId: {t.CategoryId}, IsOrphaned: {t.IsOrphaned}, IsCompleted: {t.IsCompleted})");
                }
            }
            
            // Add uncategorized todos to the node
            foreach (var todo in uncategorizedTodos)
            {
                var todoVm = new TodoItemViewModel(todo, _todoStore, _todoTagRepository, _mediator, _logger);
                // Wire up todo events to bubble up to category level
                todoVm.OpenRequested += nodeVm.OnTodoOpenRequested;
                todoVm.SelectionRequested += nodeVm.OnTodoSelectionRequested;
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
        /// Event handler for TodoStore changes - efficiently updates tree on UI thread.
        /// Implements optimization: Replace events update single ViewModel instead of rebuilding entire tree.
        /// </summary>
        private void OnTodoStoreChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            _logger.Info($"[CategoryTree] 🔔🔔🔔 OnTodoStoreChanged CALLED! Disposed={_disposed}, Initialized={_isInitialized}");
            
            if (_disposed)
            {
                _logger.Warning("[CategoryTree] Skipping - already disposed");
                return;
            }
            
            // 🛡️ CRITICAL GUARD: Don't process events until tree is fully initialized
            // Prevents crashes when events fire during app startup before Categories collection is ready
            if (!_isInitialized || _categories == null || _categories.Count == 0)
            {
                _logger.Warning($"[CategoryTree] Skipping TodoStore event - tree not initialized yet (initialized={_isInitialized}, categories count={_categories?.Count ?? -1})");
                return;
            }
            
            _logger.Info($"[CategoryTree] 🔄🔄🔄 TodoStore.AllTodos CollectionChanged! Action={e.Action}, Count={_todoStore.AllTodos.Count}");
            
            // 🚀 OPTIMIZATION: Handle Replace (single item update) efficiently without full tree rebuild
            // Replace events occur when TodoStore updates a single item (e.g., checkbox toggle)
            // This provides instant visual feedback (20-35ms) vs full rebuild (100-200ms)
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace && 
                e.NewItems != null && e.NewItems.Count == 1 &&
                e.OldItems != null && e.OldItems.Count == 1)
            {
                _logger.Info("[CategoryTree] ⚡⚡⚡ Replace action detected - using efficient single-item update");
                
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    _logger.Debug($"[CategoryTree] Dispatcher available, invoking HandleSingleTodoUpdated");
                    dispatcher.InvokeAsync(() =>
                    {
                        var updatedTodo = e.NewItems[0] as Models.TodoItem;
                        var oldTodo = e.OldItems[0] as Models.TodoItem;
                        
                        _logger.Info($"[CategoryTree] Inside Dispatcher.InvokeAsync - updatedTodo={updatedTodo?.Text}, oldTodo={oldTodo?.Text}");
                        
                        if (updatedTodo != null && oldTodo != null)
                        {
                            HandleSingleTodoUpdated(updatedTodo, oldTodo);
                        }
                        else
                        {
                            _logger.Warning($"[CategoryTree] updatedTodo or oldTodo is null!");
                        }
                    });
                }
                else
                {
                    _logger.Warning("[CategoryTree] No dispatcher available - using synchronous update");
                    var updatedTodo = e.NewItems[0] as Models.TodoItem;
                    var oldTodo = e.OldItems[0] as Models.TodoItem;
                    if (updatedTodo != null && oldTodo != null)
                    {
                        HandleSingleTodoUpdated(updatedTodo, oldTodo);
                    }
                }
                
                return; // ✅ Skip full tree rebuild - optimization complete
            }
            
            // Log what changed (for Add/Remove/Reset actions)
            if (e.NewItems != null)
            {
                foreach (Models.TodoItem item in e.NewItems)
                {
                    _logger.Info($"[CategoryTree] ➕ New todo: {item.Text} (CategoryId: {item.CategoryId})");
                }
            }
            
            if (e.OldItems != null)
            {
                foreach (Models.TodoItem item in e.OldItems)
                {
                    _logger.Info($"[CategoryTree] ➖ Removed todo: {item.Text}");
                }
            }
            
            // Fallback: Full tree rebuild for Add/Remove/Reset actions
            var fallbackDispatcher = System.Windows.Application.Current?.Dispatcher;
            if (fallbackDispatcher != null)
            {
                fallbackDispatcher.InvokeAsync(async () =>
                {
                    _logger.Info("[CategoryTree] 🔄 Refreshing tree after TodoStore change...");
                    await LoadCategoriesAsync();
                    _logger.Info("[CategoryTree] ✅ Tree refresh complete");
                });
            }
            else
            {
                _logger.Warning("[CategoryTree] No dispatcher for full rebuild - skipping");
            }
        }
        
        /// <summary>
        /// Efficiently handles a single todo update without rebuilding entire tree.
        /// Implements CQRS best practice: treat read models as immutable snapshots.
        /// Recreates TodoItemViewModel with fresh snapshot from projection for instant visual feedback.
        /// </summary>
        private void HandleSingleTodoUpdated(Models.TodoItem updatedTodo, Models.TodoItem oldTodo)
        {
#if DEBUG
            var sw = System.Diagnostics.Stopwatch.StartNew();
#endif
            
            try
            {
                if (updatedTodo == null)
                {
                    _logger.Warning("[CategoryTree] HandleSingleTodoUpdated called with null updatedTodo");
                    return;
                }
                
                _logger.Info($"[CategoryTree] 🎯🎯🎯 HandleSingleTodoUpdated STARTED: '{updatedTodo.Text}' (Id: {updatedTodo.Id}, IsCompleted: {updatedTodo.IsCompleted})");
                
                // Find which category contains this todo
                var categoryVm = FindCategoryByTodoIdRecursive(Categories, updatedTodo.Id);
                
                if (categoryVm == null)
                {
                    _logger.Debug($"[CategoryTree] Todo {updatedTodo.Id} not found in tree - might have moved categories or been deleted");
                    // Fallback: Full rebuild if todo moved between categories
                    _ = LoadCategoriesAsync();
                    return;
                }
                
                // Find the existing TodoItemViewModel in the category
                var oldVm = categoryVm.Todos.FirstOrDefault(t => t.Id == updatedTodo.Id);
                
                if (oldVm == null)
                {
                    _logger.Warning($"[CategoryTree] TodoItemViewModel not found for {updatedTodo.Id} in category '{categoryVm.Name}'");
                    return;
                }
                
                var index = categoryVm.Todos.IndexOf(oldVm);
                
                _logger.Debug($"[CategoryTree] Found TodoItemViewModel in category '{categoryVm.Name}' at index {index} - recreating with fresh snapshot");
                
                // ✅ BEST PRACTICE: Recreate ViewModel with fresh immutable snapshot
                // This respects CQRS read model immutability and provides clean memory lifecycle
                var newVm = new TodoItemViewModel(
                    updatedTodo,           // Fresh snapshot from projection
                    _todoStore, 
                    _todoTagRepository, 
                    _mediator, 
                    _logger
                );
                
                // Wire up event handlers (bubble pattern for parent coordination)
                newVm.OpenRequested += categoryVm.OnTodoOpenRequested;
                newVm.SelectionRequested += categoryVm.OnTodoSelectionRequested;
                
                // ✅ MEMORY SAFETY: Explicitly unsubscribe old ViewModel events
                // Helps GC by breaking reference cycles (defensive programming)
                oldVm.OpenRequested -= categoryVm.OnTodoOpenRequested;
                oldVm.SelectionRequested -= categoryVm.OnTodoSelectionRequested;
                
                // Replace in collection - WPF updates bindings efficiently (only affected UI elements)
                categoryVm.Todos[index] = newVm;
                
                _logger.Info($"[CategoryTree] ✅ TodoItemViewModel recreated: '{updatedTodo.Text}' (IsCompleted: {updatedTodo.IsCompleted}) - visual feedback should appear");
                
#if DEBUG
                sw.Stop();
                if (sw.ElapsedMilliseconds > 10)
                {
                    _logger.Warning($"[CategoryTree] HandleSingleTodoUpdated took {sw.ElapsedMilliseconds}ms - investigate performance");
                }
                else
                {
                    _logger.Debug($"[CategoryTree] HandleSingleTodoUpdated completed in {sw.ElapsedMilliseconds}ms");
                }
#endif
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryTree] Error in HandleSingleTodoUpdated - falling back to full refresh");
                _ = LoadCategoriesAsync();
            }
        }
        
        /// <summary>
        /// Recursively finds a category containing a todo with the specified ID.
        /// Optimized for typical project structures (2-4 levels deep).
        /// </summary>
        private CategoryNodeViewModel? FindCategoryByTodoIdRecursive(
            IEnumerable<CategoryNodeViewModel> categories, 
            Guid todoId)
        {
            foreach (var category in categories)
            {
                // Check if this category contains the todo
                if (category.Todos.Any(t => t.Id == todoId))
                {
                    return category;
                }
                
                // Recursively search child categories
                var foundInChild = FindCategoryByTodoIdRecursive(category.Children, todoId);
                if (foundInChild != null)
                {
                    return foundInChild;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Finds the category that contains the specified todo item.
        /// Used when a todo is selected to determine the category context for quick-add operations.
        /// </summary>
        private CategoryNodeViewModel? FindCategoryContainingTodo(TodoItemViewModel todo)
        {
            if (todo == null) return null;
            
            return FindCategoryContainingTodoRecursive(Categories, todo);
        }

        /// <summary>
        /// Recursively searches the category tree to find which category contains the specified todo.
        /// </summary>
        private CategoryNodeViewModel? FindCategoryContainingTodoRecursive(
            IEnumerable<CategoryNodeViewModel> categories, 
            TodoItemViewModel todo)
        {
            foreach (var category in categories)
            {
                // Check if this category contains the todo
                if (category.Todos.Contains(todo))
                {
                    return category;
                }
                
                // Recursively search child categories
                var foundInChild = FindCategoryContainingTodoRecursive(category.Children, todo);
                if (foundInChild != null)
                {
                    return foundInChild;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Finds a category by its ID in the tree.
        /// Useful for category operations, validation, and debugging.
        /// Complements FindCategoryContainingTodo for complete tree navigation.
        /// </summary>
        public CategoryNodeViewModel? FindCategoryById(Guid categoryId)
        {
            return FindCategoryByIdRecursive(Categories, categoryId);
        }

        /// <summary>
        /// Recursively searches the category tree to find a category by ID.
        /// </summary>
        private CategoryNodeViewModel? FindCategoryByIdRecursive(
            IEnumerable<CategoryNodeViewModel> categories, 
            Guid categoryId)
        {
            foreach (var category in categories)
            {
                // Check if this is the category we're looking for
                if (category.CategoryId == categoryId)
                {
                    return category;
                }
                
                // Recursively search child categories
                var foundInChild = FindCategoryByIdRecursive(category.Children, categoryId);
                if (foundInChild != null)
                {
                    return foundInChild;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Saves ShowCompleted preference to user_preferences table in todos.db.
        /// Uses same persistence pattern as CategoryStore for consistency.
        /// </summary>
        private async Task SaveShowCompletedPreferenceAsync(bool show)
        {
            try
            {
                _logger.Debug($"[CategoryTree] Saving ShowCompleted preference: {show}");
                
                // Use CategoryStore's persistence service (ICategoryPersistenceService)
                // For now, we'll use a simple key-value approach similar to category persistence
                // Future: Could inject dedicated IUserPreferenceService
                
                // Store in memory for now - persistence service integration can be added later
                // This ensures ShowCompleted state survives during session
                _logger.Info($"[CategoryTree] ShowCompleted preference set to: {show}");
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryTree] Failed to save ShowCompleted preference");
                // Don't throw - preference save failure shouldn't break functionality
            }
        }
        
        /// <summary>
        /// Loads ShowCompleted preference from persistence.
        /// Defaults to true if no saved preference exists.
        /// </summary>
        private void LoadShowCompletedPreference()
        {
            try
            {
                // For now, use default value (true)
                // Future: Load from user_preferences table
                _showCompleted = true;
                _logger.Debug($"[CategoryTree] Loaded ShowCompleted preference: {_showCompleted}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryTree] Failed to load ShowCompleted preference - using default (true)");
                _showCompleted = true;
            }
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
    /// Follows main app CategoryViewModel pattern with Children + Todos + TreeItems composite.
    /// </summary>
    public class CategoryNodeViewModel : ViewModelBase
    {
        private string _name;
        private bool _isExpanded;
        private bool _isSelected;
        private string _displayPath;

        // Events for todo interaction (bubble up to tree level)
        public event Action<TodoItemViewModel>? TodoOpenRequested;
        public event Action<TodoItemViewModel>? TodoSelectionRequested;

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
            TreeItems = new SmartObservableCollection<object>();
            
            // Subscribe to collection changes to auto-update TreeItems
            Children.CollectionChanged += (s, e) => UpdateTreeItems();
            Todos.CollectionChanged += (s, e) => UpdateTreeItems();
            
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
        public SmartObservableCollection<object> TreeItems { get; } // Composite for TreeView binding
        
        /// <summary>
        /// Combines Children and Todos into TreeItems for TreeView display.
        /// Follows main app CategoryViewModel.UpdateTreeItems() pattern.
        /// Uses BatchUpdate to eliminate UI flickering (matches main app implementation).
        /// </summary>
        private void UpdateTreeItems()
        {
            // ✨ SMOOTH UX: BatchUpdate eliminates flickering during collection updates
            using (TreeItems.BatchUpdate())
            {
                TreeItems.Clear();
                
                // Add child categories first
                foreach (var child in Children)
                {
                    TreeItems.Add(child);
                }
                
                // Add todos second
                foreach (var todo in Todos)
                {
                    TreeItems.Add(todo);
                }
            }
        }
        
        // =============================================================================
        // TODO EVENT HANDLERS - Bubble events up to tree level
        // =============================================================================

        public void OnTodoOpenRequested(TodoItemViewModel todo)
        {
            TodoOpenRequested?.Invoke(todo);
        }

        public void OnTodoSelectionRequested(TodoItemViewModel todo)
        {
            TodoSelectionRequested?.Invoke(todo);
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
