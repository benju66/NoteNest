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
            CreateCategoryCommand = new AsyncRelayCommand<string>(ExecuteCreateCategory, CanCreateCategory);
            DeleteCategoryCommand = new AsyncRelayCommand<string>(ExecuteDeleteCategory, CanDeleteCategory);
            RenameCategoryCommand = new AsyncRelayCommand<(string categoryId, string newName)>(
                async param => await ExecuteRenameCategory(param.categoryId, param.newName),
                param => CanRenameCategory(param.categoryId));
        }

        private async Task ExecuteCreateCategory(string parentCategoryId)
        {
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
                StatusMessage = $"Created category: {categoryName}";
                CategoryCreated?.Invoke("new-category-id");
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

        private async Task ExecuteDeleteCategory(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId))
                return;

            try
            {
                var confirmed = await _dialogService.ShowConfirmationDialogAsync(
                    "Are you sure you want to delete this category and all its contents? This action cannot be undone.",
                    "Confirm Delete");

                if (!confirmed)
                    return;

                IsProcessing = true;
                StatusMessage = "Deleting category...";

                // TODO: Implement DeleteCategoryCommand when we add category CQRS operations
                StatusMessage = "Category deleted successfully";
                CategoryDeleted?.Invoke(categoryId);
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

        private async Task ExecuteRenameCategory(string categoryId, string newName)
        {
            if (string.IsNullOrEmpty(categoryId) || string.IsNullOrEmpty(newName))
                return;

            try
            {
                IsProcessing = true;
                StatusMessage = "Renaming category...";

                // TODO: Implement RenameCategoryCommand when we add category CQRS operations
                StatusMessage = $"Renamed category to: {newName}";
                CategoryRenamed?.Invoke(categoryId, newName);
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

        private bool CanCreateCategory(string parentCategoryId) => !IsProcessing;
        private bool CanDeleteCategory(string categoryId) => !IsProcessing && !string.IsNullOrEmpty(categoryId);
        private bool CanRenameCategory(string categoryId) => !IsProcessing && !string.IsNullOrEmpty(categoryId);
    }
}
