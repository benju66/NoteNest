using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MediatR;
using NoteNest.UI.ViewModels.Common;
using NoteNest.UI.Services;

namespace NoteNest.UI.ViewModels.Categories
{
    /// <summary>
    /// Focused ViewModel for category operations - replaces category logic from MainViewModel
    /// </summary>
    public class CategoryOperationsViewModel : ViewModelBase
    {
        private readonly IMediator _mediator;
        private readonly IDialogService _dialogService;
        private bool _isProcessing;
        private string _statusMessage;

        public CategoryOperationsViewModel(
            IMediator mediator,
            IDialogService dialogService)
        {
            _mediator = mediator;
            _dialogService = dialogService;
            
            InitializeCommands();
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set => SetProperty(ref _isProcessing, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand CreateCategoryCommand { get; private set; }
        public ICommand DeleteCategoryCommand { get; private set; }
        public ICommand RenameCategoryCommand { get; private set; }

        // Events for UI coordination
        public event Action<string> CategoryCreated;
        public event Action<string> CategoryDeleted;
        public event Action<string, string> CategoryRenamed;

        private void InitializeCommands()
        {
            // Commands now accept ViewModel objects from context menu
            CreateCategoryCommand = new AsyncRelayCommand<object>(ExecuteCreateCategory, CanCreateCategory);
            DeleteCategoryCommand = new AsyncRelayCommand<object>(ExecuteDeleteCategory, CanDeleteCategory);
            RenameCategoryCommand = new AsyncRelayCommand<object>(ExecuteRenameCategory, CanRenameCategory);
        }

        private async Task ExecuteCreateCategory(object parameter)
        {
            // Extract CategoryViewModel from parameter (parent category)
            var parentCategory = parameter as CategoryViewModel;
            var parentCategoryId = parentCategory?.Id;
            
            try
            {
                IsProcessing = true;
                StatusMessage = "Creating category...";

                var categoryName = await _dialogService.ShowInputDialogAsync(
                    "New Category",
                    "Enter category name:",
                    "",
                    text => string.IsNullOrWhiteSpace(text) ? "Category name cannot be empty." : null);

                if (string.IsNullOrWhiteSpace(categoryName))
                {
                    StatusMessage = "Category creation cancelled.";
                    return;
                }

                // TODO: Implement CreateCategoryCommand when we add category CQRS operations
                // For now, create the category directory directly
                var parentPath = parentCategory?.Path ?? System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                    "MyNotes", "Notes");
                var newCategoryPath = System.IO.Path.Combine(parentPath, categoryName);
                
                if (System.IO.Directory.Exists(newCategoryPath))
                {
                    StatusMessage = "A category with this name already exists.";
                    _dialogService.ShowError("A category with this name already exists.", "Duplicate Category");
                    return;
                }
                
                System.IO.Directory.CreateDirectory(newCategoryPath);
                StatusMessage = $"Created category: {categoryName}";
                CategoryCreated?.Invoke(newCategoryPath);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error creating category: {ex.Message}";
                _dialogService.ShowError(ex.Message, "Error");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task ExecuteDeleteCategory(object parameter)
        {
            // Extract CategoryViewModel from parameter
            var category = parameter as CategoryViewModel;
            var categoryId = category?.Id ?? parameter?.ToString();
            
            if (string.IsNullOrEmpty(categoryId))
                return;

            try
            {
                var confirmed = await _dialogService.ShowConfirmationDialogAsync(
                    $"Are you sure you want to delete '{category?.Name ?? "this category"}' and all its contents? This action cannot be undone.",
                    "Confirm Delete");

                if (!confirmed)
                    return;

                IsProcessing = true;
                StatusMessage = "Deleting category...";

                // TODO: Implement DeleteCategoryCommand when we add category CQRS operations
                // For now, delete the directory directly
                if (category != null && System.IO.Directory.Exists(category.Path))
                {
                    System.IO.Directory.Delete(category.Path, recursive: true);
                    StatusMessage = $"Deleted category: {category.Name}";
                    CategoryDeleted?.Invoke(categoryId);
                }
                else
                {
                    StatusMessage = "Category not found.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error deleting category: {ex.Message}";
                _dialogService.ShowError(ex.Message, "Error");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task ExecuteRenameCategory(object parameter)
        {
            // Extract CategoryViewModel from parameter
            var category = parameter as CategoryViewModel;
            var categoryId = category?.Id ?? parameter?.ToString();
            
            if (string.IsNullOrEmpty(categoryId) || category == null)
                return;

            try
            {
                IsProcessing = true;
                StatusMessage = "Renaming category...";

                // Show dialog to get new name
                var newName = await _dialogService.ShowInputDialogAsync(
                    "Rename Category",
                    "Enter new category name:",
                    category.Name,
                    text => string.IsNullOrWhiteSpace(text) ? "Category name cannot be empty." : null);

                if (string.IsNullOrWhiteSpace(newName) || newName == category.Name)
                {
                    StatusMessage = "Rename cancelled.";
                    return;
                }

                // TODO: Implement RenameCategoryCommand when we add category CQRS operations
                // For now, rename the directory directly
                if (System.IO.Directory.Exists(category.Path))
                {
                    var parentPath = System.IO.Path.GetDirectoryName(category.Path);
                    var newPath = System.IO.Path.Combine(parentPath, newName);
                    
                    if (System.IO.Directory.Exists(newPath))
                    {
                        StatusMessage = "A category with this name already exists.";
                        _dialogService.ShowError("A category with this name already exists.", "Duplicate Category");
                        return;
                    }
                    
                    System.IO.Directory.Move(category.Path, newPath);
                    StatusMessage = $"Renamed category to: {newName}";
                    CategoryRenamed?.Invoke(categoryId, newName);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error renaming category: {ex.Message}";
                _dialogService.ShowError(ex.Message, "Error");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private bool CanCreateCategory(object parameter) => !IsProcessing;
        
        private bool CanDeleteCategory(object parameter)
        {
            var category = parameter as CategoryViewModel;
            var categoryId = category?.Id ?? parameter?.ToString();
            return !IsProcessing && !string.IsNullOrEmpty(categoryId);
        }
        
        private bool CanRenameCategory(object parameter)
        {
            var category = parameter as CategoryViewModel;
            var categoryId = category?.Id ?? parameter?.ToString();
            return !IsProcessing && !string.IsNullOrEmpty(categoryId);
        }
    }
}
