using System.Threading.Tasks;
using NoteNest.Core.Models;

namespace NoteNest.Core.Interfaces.Services
{
    /// <summary>
    /// Service responsible for tree operations (CRUD).
    /// Separated from UI concerns to follow SRP - handles tree operation coordination.
    /// Uses pure data models to avoid UI dependencies.
    /// </summary>
    public interface ITreeOperationService
    {
        /// <summary>
        /// Creates a new category at the root level
        /// </summary>
        Task<TreeOperationResult<CategoryModel>> CreateCategoryAsync(string name);
        
        /// <summary>
        /// Creates a new subcategory under the specified parent
        /// </summary>
        Task<TreeOperationResult<CategoryModel>> CreateSubCategoryAsync(TreeSubCategoryCreateRequest request);
        
        /// <summary>
        /// Renames a category
        /// </summary>
        Task<TreeOperationResult<bool>> RenameCategoryAsync(TreeCategoryOperationRequest request, string newName);
        
        /// <summary>
        /// Toggles the pinned status of a category
        /// </summary>
        Task<TreeOperationResult<bool>> ToggleCategoryPinAsync(TreeCategoryOperationRequest request);
        
        /// <summary>
        /// Deletes a category and all its contents
        /// </summary>
        Task<TreeOperationResult<bool>> DeleteCategoryAsync(TreeCategoryOperationRequest request);
        
        /// <summary>
        /// Renames a note
        /// </summary>
        Task<TreeOperationResult<bool>> RenameNoteAsync(TreeNoteOperationRequest request, string newName);
        
        /// <summary>
        /// Deletes a note
        /// </summary>
        Task<TreeOperationResult<bool>> DeleteNoteAsync(TreeNoteOperationRequest request);
        
        /// <summary>
        /// Moves a note to a different category
        /// </summary>
        Task<TreeOperationResult<bool>> MoveNoteAsync(TreeNoteMoveRequest request);
    }
    
    /// <summary>
    /// Result of a tree operation with success status and optional data
    /// </summary>
    public class TreeOperationResult<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public string? ErrorMessage { get; set; }
        public string? StatusMessage { get; set; }
        
        public static TreeOperationResult<T> CreateSuccess(T data, string statusMessage = null)
        {
            return new TreeOperationResult<T>
            {
                Success = true,
                Data = data,
                StatusMessage = statusMessage
            };
        }
        
        public static TreeOperationResult<T> CreateFailure(string errorMessage)
        {
            return new TreeOperationResult<T>
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}