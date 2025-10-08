using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MediatR;
using NoteNest.Core.Commands;
using NoteNest.Core.Services.Logging;
using NoteNest.Plugins.TodoPlugin.Application.Commands.Categories;
using NoteNest.Plugins.TodoPlugin.Application.Common.Interfaces;
using NoteNest.Plugins.TodoPlugin.Application.Queries.Categories;
using NoteNest.Plugins.TodoPlugin.Domain.Entities;
using NoteNest.Plugins.TodoPlugin.Domain.ValueObjects;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.Plugins.TodoPlugin.UI.ViewModels
{
    /// <summary>
    /// View model for the category tree view.
    /// </summary>
    public class CategoryTreeViewModel : ViewModelBase
    {
        private readonly IMediator _mediator;
        private readonly ICategoryStore _categoryStore;
        private readonly IAppLogger _logger;
        
        private ObservableCollection<CategoryNodeViewModel> _categories;
        private CategoryNodeViewModel? _selectedCategory;
        private SmartListNodeViewModel? _selectedSmartList;
        private ObservableCollection<SmartListNodeViewModel> _smartLists;

        public CategoryTreeViewModel(
            IMediator mediator,
            ICategoryStore categoryStore,
            IAppLogger logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _categoryStore = categoryStore ?? throw new ArgumentNullException(nameof(categoryStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _categories = new ObservableCollection<CategoryNodeViewModel>();
            _smartLists = new ObservableCollection<SmartListNodeViewModel>();
            
            InitializeCommands();
            InitializeSmartLists();
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

        public event EventHandler<CategoryId>? CategorySelected;
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
                
                var command = new CreateCategoryCommand
                {
                    Name = categoryName,
                    ParentId = parent?.CategoryId
                };
                
                var result = await _mediator.Send(command);
                if (result.IsSuccess)
                {
                    await LoadCategoriesAsync();
                }
                else
                {
                    _logger.Warning($"Failed to create category: {result.Error}");
                }
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
                
                var command = new UpdateCategoryCommand
                {
                    CategoryId = categoryVm.CategoryId,
                    Name = newName
                };
                
                var result = await _mediator.Send(command);
                if (result.IsSuccess)
                {
                    categoryVm.Name = newName;
                }
                else
                {
                    _logger.Warning($"Failed to rename category: {result.Error}");
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
                var command = new DeleteCategoryCommand
                {
                    CategoryId = categoryVm.CategoryId
                };
                
                var result = await _mediator.Send(command);
                if (result.IsSuccess)
                {
                    await LoadCategoriesAsync();
                }
                else
                {
                    _logger.Warning($"Failed to delete category: {result.Error}");
                }
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
            
            // Select Today by default
            SelectedSmartList = SmartLists.FirstOrDefault();
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                // Get all categories from store
                var allCategories = _categoryStore.Categories;
                
                // Build tree structure
                Categories.Clear();
                var rootCategories = allCategories.Where(c => c.ParentId == null);
                
                foreach (var category in rootCategories)
                {
                    var nodeVm = BuildCategoryNode(category, allCategories);
                    Categories.Add(nodeVm);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading categories");
            }
        }

        private CategoryNodeViewModel BuildCategoryNode(Category category, ObservableCollection<Category> allCategories)
        {
            var nodeVm = new CategoryNodeViewModel(category);
            
            // Find child categories
            var children = allCategories.Where(c => c.ParentId == category.Id);
            foreach (var child in children)
            {
                var childNode = BuildCategoryNode(child, allCategories);
                nodeVm.Children.Add(childNode);
            }
            
            return nodeVm;
        }

        #endregion
    }

    /// <summary>
    /// View model for category tree nodes.
    /// </summary>
    public class CategoryNodeViewModel : ViewModelBase
    {
        private string _name;
        private bool _isExpanded;
        private bool _isSelected;

        public CategoryNodeViewModel(Category category)
        {
            CategoryId = category.Id;
            _name = category.Name;
            Children = new ObservableCollection<CategoryNodeViewModel>();
        }

        public CategoryId CategoryId { get; }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
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

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
