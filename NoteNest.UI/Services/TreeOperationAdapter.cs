using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Adapter that converts between UI ViewModels and Core data contracts for tree operations.
    /// This maintains clean separation of concerns while allowing UI to work with familiar models.
    /// </summary>
    public class TreeOperationAdapter
    {
        private readonly ITreeOperationService _treeOperationService;

        public TreeOperationAdapter(ITreeOperationService treeOperationService)
        {
            _treeOperationService = treeOperationService;
        }

        /// <summary>
        /// Creates a new category at the root level
        /// </summary>
        public async Task<TreeOperationResult<CategoryModel>> CreateCategoryAsync(string name)
        {
            return await _treeOperationService.CreateCategoryAsync(name);
        }

        /// <summary>
        /// Creates a new subcategory under the specified parent
        /// </summary>
        public async Task<TreeOperationResult<CategoryModel>> CreateSubCategoryAsync(CategoryTreeItem parentCategory, string name)
        {
            var request = new TreeSubCategoryCreateRequest
            {
                ParentCategory = new TreeCategoryOperationRequest
                {
                    CategoryId = parentCategory.Model.Id,
                    CategoryName = parentCategory.Name,
                    CategoryModel = parentCategory.Model
                },
                NewCategoryName = name
            };

            return await _treeOperationService.CreateSubCategoryAsync(request);
        }

        /// <summary>
        /// Renames a category
        /// </summary>
        public async Task<TreeOperationResult<bool>> RenameCategoryAsync(CategoryTreeItem categoryItem, string newName)
        {
            var request = new TreeCategoryOperationRequest
            {
                CategoryId = categoryItem.Model.Id,
                CategoryName = categoryItem.Name,
                CategoryModel = categoryItem.Model
            };

            return await _treeOperationService.RenameCategoryAsync(request, newName);
        }

        /// <summary>
        /// Toggles the pinned status of a category
        /// </summary>
        public async Task<TreeOperationResult<bool>> ToggleCategoryPinAsync(CategoryTreeItem categoryItem)
        {
            var request = new TreeCategoryOperationRequest
            {
                CategoryId = categoryItem.Model.Id,
                CategoryName = categoryItem.Name,
                CategoryModel = categoryItem.Model
            };

            return await _treeOperationService.ToggleCategoryPinAsync(request);
        }

        /// <summary>
        /// Deletes a category and all its contents
        /// </summary>
        public async Task<TreeOperationResult<bool>> DeleteCategoryAsync(CategoryTreeItem categoryItem)
        {
            var request = new TreeCategoryOperationRequest
            {
                CategoryId = categoryItem.Model.Id,
                CategoryName = categoryItem.Name,
                CategoryModel = categoryItem.Model
            };

            return await _treeOperationService.DeleteCategoryAsync(request);
        }

        /// <summary>
        /// Renames a note
        /// </summary>
        public async Task<TreeOperationResult<bool>> RenameNoteAsync(NoteTreeItem noteItem, string newName)
        {
            var request = new TreeNoteOperationRequest
            {
                NoteId = noteItem.Model.Id,
                NoteTitle = noteItem.Title,
                NoteModel = noteItem.Model
            };

            return await _treeOperationService.RenameNoteAsync(request, newName);
        }

        /// <summary>
        /// Deletes a note
        /// </summary>
        public async Task<TreeOperationResult<bool>> DeleteNoteAsync(NoteTreeItem noteItem)
        {
            var request = new TreeNoteOperationRequest
            {
                NoteId = noteItem.Model.Id,
                NoteTitle = noteItem.Title,
                NoteModel = noteItem.Model
            };

            return await _treeOperationService.DeleteNoteAsync(request);
        }

        /// <summary>
        /// Moves a note to a different category
        /// </summary>
        public async Task<TreeOperationResult<bool>> MoveNoteAsync(NoteTreeItem noteItem, CategoryTreeItem targetCategory)
        {
            var request = new TreeNoteMoveRequest
            {
                Note = new TreeNoteOperationRequest
                {
                    NoteId = noteItem.Model.Id,
                    NoteTitle = noteItem.Title,
                    NoteModel = noteItem.Model
                },
                TargetCategory = new TreeCategoryOperationRequest
                {
                    CategoryId = targetCategory.Model.Id,
                    CategoryName = targetCategory.Name,
                    CategoryModel = targetCategory.Model
                }
            };

            return await _treeOperationService.MoveNoteAsync(request);
        }

        /// <summary>
        /// Moves a category to a new parent location
        /// </summary>
        public async Task<TreeOperationResult<bool>> MoveCategoryAsync(string categoryId, string newParentId)
        {
            if (_treeOperationService == null)
                return TreeOperationResult<bool>.CreateFailure("Tree operation service not available");
                
            return await _treeOperationService.MoveCategoryAsync(categoryId, newParentId);
        }
    }
}
