using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Core.Models;

namespace NoteNest.Core.Interfaces.Services
{
    public interface ICategoryManagementService
    {
        Task<CategoryModel> CreateCategoryAsync(string name, string? parentId = null);
        Task<CategoryModel> CreateSubCategoryAsync(CategoryModel parent, string name);
        Task<bool> DeleteCategoryAsync(CategoryModel category);
        Task<bool> RenameCategoryAsync(CategoryModel category, string newName);
        Task<bool> ToggleCategoryPinAsync(CategoryModel category);
        Task<List<CategoryModel>> LoadCategoriesAsync();
        Task SaveCategoriesAsync(List<CategoryModel> categories);
    }
}