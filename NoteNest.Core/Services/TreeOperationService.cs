using System;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Service responsible for tree operations (CRUD).
    /// Extracted from MainViewModel to follow SRP - handles tree operation coordination.
    /// Uses pure data models to avoid UI dependencies.
    /// </summary>
    public class TreeOperationService : ITreeOperationService
    {
        private readonly ICategoryManagementService _categoryService;
        private readonly INoteOperationsService _noteOperationsService;
        private readonly IAppLogger _logger;

        public TreeOperationService(
            ICategoryManagementService categoryService,
            INoteOperationsService noteOperationsService,
            IAppLogger logger)
        {
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _noteOperationsService = noteOperationsService ?? throw new ArgumentNullException(nameof(noteOperationsService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a new category at the root level
        /// </summary>
        public async Task<TreeOperationResult<CategoryModel>> CreateCategoryAsync(string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return TreeOperationResult<CategoryModel>.CreateFailure("Category name cannot be empty");
                }

                _logger.Debug($"TreeOperationService: Creating category '{name}'");
                var category = await _categoryService.CreateCategoryAsync(name);
                
                if (category != null)
                {
                    var statusMessage = $"Created category: {name}";
                    _logger.Info(statusMessage);
                    return TreeOperationResult<CategoryModel>.CreateSuccess(category, statusMessage);
                }
                
                return TreeOperationResult<CategoryModel>.CreateFailure("Failed to create category");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error creating category '{name}'");
                return TreeOperationResult<CategoryModel>.CreateFailure($"Error creating category: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a new subcategory under the specified parent
        /// </summary>
        public async Task<TreeOperationResult<CategoryModel>> CreateSubCategoryAsync(TreeSubCategoryCreateRequest request)
        {
            try
            {
                if (request?.ParentCategory?.CategoryModel == null)
                {
                    return TreeOperationResult<CategoryModel>.CreateFailure("Parent category cannot be null");
                }
                
                if (string.IsNullOrWhiteSpace(request.NewCategoryName))
                {
                    return TreeOperationResult<CategoryModel>.CreateFailure("Subcategory name cannot be empty");
                }

                _logger.Debug($"TreeOperationService: Creating subcategory '{request.NewCategoryName}' under '{request.ParentCategory.CategoryName}'");
                var subCategory = await _categoryService.CreateSubCategoryAsync(request.ParentCategory.CategoryModel, request.NewCategoryName);
                
                if (subCategory != null)
                {
                    var statusMessage = $"Created subcategory: {request.NewCategoryName} under {request.ParentCategory.CategoryName}";
                    _logger.Info(statusMessage);
                    return TreeOperationResult<CategoryModel>.CreateSuccess(subCategory, statusMessage);
                }
                
                return TreeOperationResult<CategoryModel>.CreateFailure("Failed to create subcategory");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error creating subcategory '{request?.NewCategoryName}' under parent '{request?.ParentCategory?.CategoryName}'");
                return TreeOperationResult<CategoryModel>.CreateFailure($"Error creating subcategory: {ex.Message}");
            }
        }

        /// <summary>
        /// Renames a category
        /// </summary>
        public async Task<TreeOperationResult<bool>> RenameCategoryAsync(TreeCategoryOperationRequest request, string newName)
        {
            try
            {
                if (request?.CategoryModel == null)
                {
                    return TreeOperationResult<bool>.CreateFailure("Category cannot be null");
                }
                
                if (string.IsNullOrWhiteSpace(newName))
                {
                    return TreeOperationResult<bool>.CreateFailure("New category name cannot be empty");
                }

                _logger.Debug($"TreeOperationService: Renaming category '{request.CategoryName}' to '{newName}'");
                var success = await _categoryService.RenameCategoryAsync(request.CategoryModel, newName);
                
                if (success)
                {
                    var statusMessage = $"Renamed category to '{newName}'";
                    _logger.Info(statusMessage);
                    return TreeOperationResult<bool>.CreateSuccess(true, statusMessage);
                }
                
                return TreeOperationResult<bool>.CreateFailure("Failed to rename category");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error renaming category '{request?.CategoryName}' to '{newName}'");
                return TreeOperationResult<bool>.CreateFailure($"Error renaming category: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggles the pinned status of a category
        /// </summary>
        public async Task<TreeOperationResult<bool>> ToggleCategoryPinAsync(TreeCategoryOperationRequest request)
        {
            try
            {
                if (request?.CategoryModel == null)
                {
                    return TreeOperationResult<bool>.CreateFailure("Category cannot be null");
                }

                _logger.Debug($"TreeOperationService: Toggling pin for category '{request.CategoryName}'");
                var success = await _categoryService.ToggleCategoryPinAsync(request.CategoryModel);
                
                if (success)
                {
                    var statusMessage = request.CategoryModel.Pinned ? 
                        $"Pinned category: {request.CategoryName}" : 
                        $"Unpinned category: {request.CategoryName}";
                    _logger.Info(statusMessage);
                    return TreeOperationResult<bool>.CreateSuccess(true, statusMessage);
                }
                
                return TreeOperationResult<bool>.CreateFailure("Failed to toggle category pin");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error toggling pin for category '{request?.CategoryName}'");
                return TreeOperationResult<bool>.CreateFailure($"Error toggling category pin: {ex.Message}");
            }
        }

        /// <summary>
        /// Deletes a category and all its contents
        /// </summary>
        public async Task<TreeOperationResult<bool>> DeleteCategoryAsync(TreeCategoryOperationRequest request)
        {
            try
            {
                if (request?.CategoryModel == null)
                {
                    return TreeOperationResult<bool>.CreateFailure("Category cannot be null");
                }

                _logger.Debug($"TreeOperationService: Deleting category '{request.CategoryName}'");
                var success = await _categoryService.DeleteCategoryAsync(request.CategoryModel);
                
                if (success)
                {
                    var statusMessage = $"Deleted category: {request.CategoryName}";
                    _logger.Info(statusMessage);
                    return TreeOperationResult<bool>.CreateSuccess(true, statusMessage);
                }
                
                return TreeOperationResult<bool>.CreateFailure("Failed to delete category");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error deleting category '{request?.CategoryName}'");
                return TreeOperationResult<bool>.CreateFailure($"Error deleting category: {ex.Message}");
            }
        }

        /// <summary>
        /// Renames a note
        /// </summary>
        public async Task<TreeOperationResult<bool>> RenameNoteAsync(TreeNoteOperationRequest request, string newName)
        {
            try
            {
                if (request?.NoteModel == null)
                {
                    return TreeOperationResult<bool>.CreateFailure("Note cannot be null");
                }
                
                if (string.IsNullOrWhiteSpace(newName))
                {
                    return TreeOperationResult<bool>.CreateFailure("New note name cannot be empty");
                }

                _logger.Debug($"TreeOperationService: Renaming note '{request.NoteTitle}' to '{newName}'");
                var success = await _noteOperationsService.RenameNoteAsync(request.NoteModel, newName);
                
                if (success)
                {
                    var statusMessage = $"Renamed note to '{newName}'";
                    _logger.Info(statusMessage);
                    return TreeOperationResult<bool>.CreateSuccess(true, statusMessage);
                }
                
                return TreeOperationResult<bool>.CreateFailure("A note with this name already exists or rename failed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error renaming note '{request?.NoteTitle}' to '{newName}'");
                return TreeOperationResult<bool>.CreateFailure($"Error renaming note: {ex.Message}");
            }
        }

        /// <summary>
        /// Deletes a note
        /// </summary>
        public async Task<TreeOperationResult<bool>> DeleteNoteAsync(TreeNoteOperationRequest request)
        {
            try
            {
                if (request?.NoteModel == null)
                {
                    return TreeOperationResult<bool>.CreateFailure("Note cannot be null");
                }

                _logger.Debug($"TreeOperationService: Deleting note '{request.NoteTitle}'");
                await _noteOperationsService.DeleteNoteAsync(request.NoteModel);
                
                var statusMessage = $"Deleted '{request.NoteTitle}'";
                _logger.Info(statusMessage);
                return TreeOperationResult<bool>.CreateSuccess(true, statusMessage);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error deleting note '{request?.NoteTitle}'");
                return TreeOperationResult<bool>.CreateFailure($"Error deleting note: {ex.Message}");
            }
        }

        /// <summary>
        /// Moves a note to a different category
        /// </summary>
        public async Task<TreeOperationResult<bool>> MoveNoteAsync(TreeNoteMoveRequest request)
        {
            try
            {
                if (request?.Note?.NoteModel == null)
                {
                    return TreeOperationResult<bool>.CreateFailure("Note cannot be null");
                }
                
                if (request?.TargetCategory?.CategoryModel == null)
                {
                    return TreeOperationResult<bool>.CreateFailure("Target category cannot be null");
                }

                _logger.Debug($"TreeOperationService: Moving note '{request.Note.NoteTitle}' to category '{request.TargetCategory.CategoryName}'");
                var success = await _noteOperationsService.MoveNoteAsync(request.Note.NoteModel, request.TargetCategory.CategoryModel);
                
                if (success)
                {
                    var statusMessage = $"Moved note to '{request.TargetCategory.CategoryName}'";
                    _logger.Info(statusMessage);
                    return TreeOperationResult<bool>.CreateSuccess(true, statusMessage);
                }
                
                return TreeOperationResult<bool>.CreateFailure("Failed to move note");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error moving note '{request?.Note?.NoteTitle}' to category '{request?.TargetCategory?.CategoryName}'");
                return TreeOperationResult<bool>.CreateFailure($"Error moving note: {ex.Message}");
            }
        }
    }
}
