using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MediatR;
using NoteNest.UI.ViewModels.Common;
using NoteNest.UI.Services;
using NoteNest.Application.Categories.Commands.CreateCategory;
using NoteNest.Application.Categories.Commands.DeleteCategory;
using NoteNest.Application.Categories.Commands.RenameCategory;

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
            // Store to file for persistent diagnostics
            try { System.IO.File.AppendAllText(@"C:\NoteNest\category_ops_debug.txt", $"\n[{DateTime.Now:HH:mm:ss.fff}] Constructor START\n"); } catch { }
            
            try
            {
                _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
                _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
                
                InitializeCommands();
                
                try { System.IO.File.AppendAllText(@"C:\NoteNest\category_ops_debug.txt", $"[{DateTime.Now:HH:mm:ss.fff}] Constructor COMPLETE ✅\n"); } catch { }
            }
            catch (Exception ex)
            {
                try { System.IO.File.AppendAllText(@"C:\NoteNest\category_ops_debug.txt", $"[{DateTime.Now:HH:mm:ss.fff}] ❌ CONSTRUCTOR FAILED: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
                throw;
            }
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
            System.Diagnostics.Debug.WriteLine($"[CategoryOps] ExecuteCreateCategory CALLED! Parameter: {parameter?.GetType().Name ?? "null"}");
            
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
            System.Diagnostics.Debug.WriteLine($"[CategoryOps] ExecuteDeleteCategory CALLED! Parameter: {parameter?.GetType().Name ?? "null"}");
            
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
            var logFile = @"C:\NoteNest\category_ops_debug.txt";
            try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] ExecuteRenameCategory CALLED!\n"); } catch { }
            
            try
            {
                // Extract CategoryViewModel from parameter
                var category = parameter as CategoryViewModel;
                try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] Category: {category?.Name ?? "NULL"}, Id: {category?.Id ?? "NULL"}\n"); } catch { }
                
                var categoryId = category?.Id ?? parameter?.ToString();
                
                if (string.IsNullOrEmpty(categoryId))
                {
                    try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] CategoryId is NULL - aborting\n"); } catch { }
                    return;
                }
                
                if (category == null)
                {
                    try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] Category is NULL - aborting\n"); } catch { }
                    return;
                }

                IsProcessing = true;
                StatusMessage = "Renaming category...";
                try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] Showing dialog...\n"); } catch { }

                // Show dialog to get new name
                var newName = await _dialogService.ShowInputDialogAsync(
                    "Rename Category",
                    "Enter new category name:",
                    category.Name ?? "",
                    text => string.IsNullOrWhiteSpace(text) ? "Category name cannot be empty." : null);
                
                try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] Dialog returned: {newName ?? "NULL"}\n"); } catch { }

                if (string.IsNullOrWhiteSpace(newName) || newName == category.Name)
                {
                    StatusMessage = "Rename cancelled.";
                    return;
                }

                // Use MediatR CQRS command (updates database + renames directory + updates all descendant paths)
                try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] Creating command...\n"); } catch { }
                
                var command = new RenameCategoryCommand
                {
                    CategoryId = categoryId,
                    NewName = newName
                };
                
                try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] Sending to MediatR...\n"); } catch { }
                var result = await _mediator.Send(command);
                try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] MediatR returned - IsFailure: {result?.IsFailure}\n"); } catch { }
                
                if (result.IsFailure)
                {
                    try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] ❌ Failed: {result.Error}\n"); } catch { }
                    StatusMessage = $"Failed to rename category: {result.Error}";
                    _dialogService.ShowError(result.Error, "Rename Category Error");
                }
                else
                {
                    try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] ✅ Success - {result.Value.UpdatedDescendantCount} descendants\n"); } catch { }
                    StatusMessage = $"Renamed category to: {newName} ({result.Value.UpdatedDescendantCount} items updated)";
                    CategoryRenamed?.Invoke(categoryId, newName);
                }
            }
            catch (Exception ex)
            {
                try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] ❌ EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
                StatusMessage = $"Error renaming category: {ex.Message}";
                _dialogService.ShowError($"Error: {ex.Message}\n\nSee debug output for details.", "Rename Error");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private bool CanCreateCategory(object parameter)
        {
            System.Diagnostics.Debug.WriteLine($"[CategoryOps] CanCreateCategory called - IsProcessing: {IsProcessing}, Parameter: {parameter?.GetType().Name ?? "null"}");
            return !IsProcessing;
        }
        
        private bool CanDeleteCategory(object parameter)
        {
            var category = parameter as CategoryViewModel;
            var categoryId = category?.Id ?? parameter?.ToString();
            System.Diagnostics.Debug.WriteLine($"[CategoryOps] CanDeleteCategory called - Parameter: {parameter?.GetType().Name ?? "null"}, CategoryId: {categoryId ?? "null"}");
            return !IsProcessing && !string.IsNullOrEmpty(categoryId);
        }
        
        private bool CanRenameCategory(object parameter)
        {
            var category = parameter as CategoryViewModel;
            var categoryId = category?.Id ?? parameter?.ToString();
            System.Diagnostics.Debug.WriteLine($"[CategoryOps] CanRenameCategory called - Parameter: {parameter?.GetType().Name ?? "null"}, CategoryId: {categoryId ?? "null"}");
            return !IsProcessing && !string.IsNullOrEmpty(categoryId);
        }
    }
}
