using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.ViewModels.Common;
using NoteNest.UI.Services;
using NoteNest.Application.Categories.Commands.CreateCategory;
using NoteNest.Application.Categories.Commands.DeleteCategory;
using NoteNest.Application.Categories.Commands.RenameCategory;
using NoteNest.Application.Categories.Commands.MoveCategory;
using NoteNest.Application.Notes.Commands.MoveNote;

namespace NoteNest.UI.ViewModels.Categories
{
    /// <summary>
    /// Focused ViewModel for category operations - replaces category logic from MainViewModel
    /// </summary>
    public class CategoryOperationsViewModel : ViewModelBase
    {
        private readonly IMediator _mediator;
        private readonly IDialogService _dialogService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IAppLogger _logger;
        private bool _isProcessing;
        private string _statusMessage;

        public CategoryOperationsViewModel(
            IMediator mediator,
            IDialogService dialogService,
            IServiceProvider serviceProvider,
            IAppLogger logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
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
        public ICommand MoveCategoryCommand { get; private set; }
        public ICommand MoveNoteCommand { get; private set; }
        public ICommand AddToTodoCategoriesCommand { get; private set; }

        // Events for UI coordination
        public event Action<string> CategoryCreated;
        public event Action<string> CategoryDeleted;
        public event Action<string, string> CategoryRenamed;
        public event Action<string, string, string> CategoryMoved; // (categoryId, oldParentId, newParentId)
        public event Action<string, string, string> NoteMoved; // (noteId, sourceCategoryId, targetCategoryId)

        private void InitializeCommands()
        {
            // Commands now accept ViewModel objects from context menu
            CreateCategoryCommand = new AsyncRelayCommand<object>(ExecuteCreateCategory, CanCreateCategory);
            DeleteCategoryCommand = new AsyncRelayCommand<object>(ExecuteDeleteCategory, CanDeleteCategory);
            RenameCategoryCommand = new AsyncRelayCommand<object>(ExecuteRenameCategory, CanRenameCategory);
            MoveCategoryCommand = new AsyncRelayCommand<object>(ExecuteMoveCategory, CanMoveCategory);
            MoveNoteCommand = new AsyncRelayCommand<object>(ExecuteMoveNote, CanMoveNote);
            AddToTodoCategoriesCommand = new AsyncRelayCommand<object>(ExecuteAddToTodoCategories, CanAddToTodoCategories);
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

                // Use MediatR CQRS command (updates database + creates directory)
                var command = new CreateCategoryCommand
                {
                    ParentCategoryId = parentCategoryId,
                    Name = categoryName
                };

                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    StatusMessage = $"Failed to create category: {result.Error}";
                    _dialogService.ShowError(result.Error, "Create Category Error");
                }
                else
                {
                    StatusMessage = $"Created category: {categoryName}";
                    CategoryCreated?.Invoke(result.Value.CategoryId);
                }
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

                // Use MediatR CQRS command (soft-deletes database + deletes directory)
                var command = new DeleteCategoryCommand
                {
                    CategoryId = categoryId,
                    DeleteFiles = true
                };

                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    StatusMessage = $"Failed to delete category: {result.Error}";
                    _dialogService.ShowError(result.Error, "Delete Category Error");
                }
                else
                {
                    StatusMessage = $"Deleted category: {result.Value.DeletedCategoryName} ({result.Value.DeletedDescendantCount} items)";
                    CategoryDeleted?.Invoke(categoryId);
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
                    category.Name ?? "",
                    text => string.IsNullOrWhiteSpace(text) ? "Category name cannot be empty." : null);

                if (string.IsNullOrWhiteSpace(newName) || newName == category.Name)
                {
                    StatusMessage = "Rename cancelled.";
                    return;
                }

                // Use MediatR CQRS command (updates database + renames directory + updates all descendant paths)
                var command = new RenameCategoryCommand
                {
                    CategoryId = categoryId,
                    NewName = newName
                };

                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    StatusMessage = $"Failed to rename category: {result.Error}";
                    _dialogService.ShowError(result.Error, "Rename Category Error");
                }
                else
                {
                    StatusMessage = $"Renamed category to: {newName} ({result.Value.UpdatedDescendantCount} items updated)";
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

        /// <summary>
        /// Executes category move operation.
        /// Parameter should be tuple: (CategoryViewModel source, CategoryViewModel target)
        /// </summary>
        private async Task ExecuteMoveCategory(object parameter)
        {
            // Extract source and target from parameter (tuple)
            if (parameter is not (CategoryViewModel sourceCategory, CategoryViewModel targetCategory))
                return;

            try
            {
                IsProcessing = true;
                StatusMessage = $"Moving category '{sourceCategory.Name}'...";

                // Capture old parent ID before move
                var oldParentId = sourceCategory.ParentId;

                // Use MediatR CQRS command
                var command = new MoveCategoryCommand
                {
                    CategoryId = sourceCategory.Id,
                    NewParentId = targetCategory?.Id // null = move to root
                };

                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    StatusMessage = $"Failed to move category: {result.Error}";
                    _dialogService.ShowError(result.Error, "Move Category Error");
                }
                else
                {
                    var targetName = targetCategory?.Name ?? "root";
                    StatusMessage = $"Moved '{sourceCategory.Name}' to '{targetName}' ({result.Value.AffectedDescendantCount} descendants)";
                    CategoryMoved?.Invoke(sourceCategory.Id, oldParentId, targetCategory?.Id);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error moving category: {ex.Message}";
                _dialogService.ShowError(ex.Message, "Error");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        /// <summary>
        /// Executes note move operation.
        /// Parameter should be tuple: (NoteItemViewModel note, CategoryViewModel targetCategory)
        /// </summary>
        private async Task ExecuteMoveNote(object parameter)
        {
            // Extract note and target category from parameter (tuple)
            if (parameter is not (NoteItemViewModel note, CategoryViewModel targetCategory))
                return;

            try
            {
                IsProcessing = true;
                StatusMessage = $"Moving note '{note.Title}'...";

                // Capture source category ID before move (from result)
                var command = new MoveNoteCommand
                {
                    NoteId = note.Id,
                    TargetCategoryId = targetCategory.Id
                };

                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    StatusMessage = $"Failed to move note: {result.Error}";
                    _dialogService.ShowError(result.Error, "Move Note Error");
                }
                else
                {
                    StatusMessage = $"Moved '{note.Title}' to '{targetCategory.Name}'";
                    // Use result data which includes old and new category IDs
                    NoteMoved?.Invoke(note.Id, result.Value.OldCategoryId, result.Value.NewCategoryId);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error moving note: {ex.Message}";
                _dialogService.ShowError(ex.Message, "Error");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private bool CanMoveCategory(object parameter) => !IsProcessing;
        
        private bool CanMoveNote(object parameter) => !IsProcessing;
        
        /// <summary>
        /// Adds the selected note tree category to TodoPlugin categories.
        /// Follows proven pattern from MainShellViewModel event wiring.
        /// </summary>
        private async Task ExecuteAddToTodoCategories(object parameter)
        {
            // Extract CategoryViewModel from parameter
            var categoryViewModel = parameter as CategoryViewModel;
            if (categoryViewModel == null)
            {
                _logger.Warning("[CategoryOps] AddToTodoCategories called without CategoryViewModel");
                return;
            }
            
            try
            {
                IsProcessing = true;
                StatusMessage = $"Adding '{categoryViewModel.Name}' to todo categories...";
                
                _logger.Info($"[CategoryOps] Adding category to todos: {categoryViewModel.Name}");
                
                // Get TodoPlugin's CategoryStore via service locator
                var todoCategoryStore = _serviceProvider.GetService<
                    NoteNest.UI.Plugins.TodoPlugin.Services.ICategoryStore>();
                
                if (todoCategoryStore == null)
                {
                    _logger.Warning("[CategoryOps] TodoPlugin CategoryStore not available");
                    _dialogService.ShowError(
                        "Todo plugin is not loaded or initialized.", 
                        "Todo Categories");
                    StatusMessage = "Todo plugin not available";
                    return;
                }
                
                // Parse category ID
                if (!Guid.TryParse(categoryViewModel.Id, out var categoryId))
                {
                    _logger.Error($"[CategoryOps] Invalid category ID: {categoryViewModel.Id}");
                    _dialogService.ShowError(
                        "Invalid category ID format.",
                        "Error");
                    StatusMessage = "Invalid category ID";
                    return;
                }
                
                // Check if already added
                var existing = todoCategoryStore.GetById(categoryId);
                if (existing != null)
                {
                    _logger.Info($"[CategoryOps] Category already in todos: {categoryViewModel.Name}");
                    _dialogService.ShowInfo(
                        $"'{categoryViewModel.Name}' is already in todo categories.",
                        "Todo Categories");
                    StatusMessage = $"'{categoryViewModel.Name}' already in todos";
                    return;
                }
                
                // Parse original parent ID
                Guid? originalParentId = string.IsNullOrEmpty(categoryViewModel.ParentId) 
                    ? null 
                    : Guid.Parse(categoryViewModel.ParentId);
                
                // Add category to TodoPlugin
                var todoCategory = new NoteNest.UI.Plugins.TodoPlugin.Models.Category
                {
                    Id = categoryId,
                    ParentId = null, // FLAT MODE: Always show at root for immediate visibility
                    OriginalParentId = originalParentId, // Preserve hierarchy for future TreeView
                    Name = categoryViewModel.Name,
                    DisplayPath = BuildCategoryDisplayPath(categoryViewModel), // Breadcrumb path
                    Order = 0,
                    CreatedDate = DateTime.UtcNow,
                    ModifiedDate = DateTime.UtcNow
                };
                
                await todoCategoryStore.AddAsync(todoCategory);
                
                _logger.Info($"✅ Category added to todos: {categoryViewModel.Name}");
                
                // Show success notification
                _dialogService.ShowInfo(
                    $"'{categoryViewModel.Name}' added to todo categories!",
                    "Todo Categories");
                
                StatusMessage = $"Added '{categoryViewModel.Name}' to todo categories";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryOps] Failed to add category to todos");
                _dialogService.ShowError(
                    $"Failed to add category: {ex.Message}",
                    "Error");
                StatusMessage = $"Error adding category: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
            }
        }
        
        private bool CanAddToTodoCategories(object parameter)
        {
            // Can add if parameter is CategoryViewModel and not processing
            return !IsProcessing && parameter is CategoryViewModel;
        }
        
        /// <summary>
        /// Builds a breadcrumb display path for a category.
        /// Example: "Work > Projects > ProjectAlpha"
        /// Uses CategoryViewModel.BreadcrumbPath which already computes the hierarchy.
        /// </summary>
        private string BuildCategoryDisplayPath(CategoryViewModel categoryVm)
        {
            try
            {
                // CategoryViewModel already has BreadcrumbPath property that walks the tree!
                var breadcrumb = categoryVm.BreadcrumbPath;
                
                if (!string.IsNullOrEmpty(breadcrumb))
                {
                    // Clean up the breadcrumb - remove "Notes >" prefix if present
                    var parts = breadcrumb.Split(new[] { " > " }, StringSplitOptions.RemoveEmptyEntries);
                    
                    // Skip "Notes" if it's the first part (workspace root)
                    var relevantParts = parts.Where(p => !p.Equals("Notes", StringComparison.OrdinalIgnoreCase)).ToArray();
                    
                    if (relevantParts.Length > 0)
                    {
                        return string.Join(" > ", relevantParts);
                    }
                }
                
                // Fallback to simple name
                return categoryVm.Name;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to build display path for category: {categoryVm.Name}");
                return categoryVm.Name; // Fallback to simple name
            }
        }
    }
}
