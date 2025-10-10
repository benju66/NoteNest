using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MediatR;
using NoteNest.Core.Commands;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Models;
using NoteNest.UI.Plugins.TodoPlugin.Services;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.UI.ViewModels
{
    /// <summary>
    /// View model for the category tree view.
    /// </summary>
    public class CategoryTreeViewModel : ViewModelBase
    {
        private readonly ICategoryStore _categoryStore;
        private readonly ITodoStore _todoStore;
        private readonly IAppLogger _logger;
        
        // Expose for UI operations (delete key, etc.)
        public ICategoryStore CategoryStore => _categoryStore;
        
        private ObservableCollection<CategoryNodeViewModel> _categories;
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
            
            _categories = new ObservableCollection<CategoryNodeViewModel>();
            _smartLists = new ObservableCollection<SmartListNodeViewModel>();
            
            InitializeCommands();
            InitializeSmartLists();
            
            // Subscribe to CategoryStore changes to refresh tree when categories added
            // Use Dispatcher to ensure UI thread execution
            _categoryStore.Categories.CollectionChanged += (s, e) =>
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
                {
                    _logger.Debug("[CategoryTree] CategoryStore changed, refreshing tree");
                    await LoadCategoriesAsync();
                });
            };
            
            _ = LoadCategoriesAsync();
        }

        #region Properties

        public ObservableCollection<CategoryNodeViewModel> Categories
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
                
                // Get all categories from store
                var allCategories = _categoryStore.Categories;
                _logger.Info($"[CategoryTree] CategoryStore contains {allCategories.Count} categories");
                
                // Log each category for diagnostics
                foreach (var cat in allCategories)
                {
                    _logger.Debug($"[CategoryTree] Category in store: Id={cat.Id}, Name='{cat.Name}', ParentId={cat.ParentId}, DisplayPath='{cat.DisplayPath}'");
                }
                
                // Build tree structure
                Categories.Clear();
                var rootCategories = allCategories.Where(c => c.ParentId == null).ToList();
                _logger.Info($"[CategoryTree] Found {rootCategories.Count} root categories (ParentId == null)");
                
                foreach (var category in rootCategories)
                {
                    _logger.Debug($"[CategoryTree] Building tree for root category: {category.Name}");
                    var nodeVm = BuildCategoryNode(category, allCategories);
                    Categories.Add(nodeVm);
                    _logger.Debug($"[CategoryTree] Added CategoryNodeViewModel: DisplayPath='{nodeVm.DisplayPath}'");
                }
                
                _logger.Info($"[CategoryTree] ✅ LoadCategoriesAsync complete - Categories.Count = {Categories.Count}");
                
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

        private CategoryNodeViewModel BuildCategoryNode(Category category, ObservableCollection<Category> allCategories)
        {
            _logger.Debug($"[CategoryTree] BuildCategoryNode for: '{category.Name}', DisplayPath='{category.DisplayPath}'");
            
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
                var childNode = BuildCategoryNode(child, allCategories);
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
