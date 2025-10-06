using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Domain.Common;
using NoteNest.Domain.Categories;

namespace NoteNest.Application.Common.Interfaces
{
    public interface ICategoryRepository
    {
        Task<Category> GetByIdAsync(CategoryId id);
        Task<IReadOnlyList<Category>> GetAllAsync();
        Task<IReadOnlyList<Category>> GetRootCategoriesAsync();
        Task<Result> CreateAsync(Category category);
        Task<Result> UpdateAsync(Category category);
        Task<Result> DeleteAsync(CategoryId id);
        Task<bool> ExistsAsync(CategoryId id);
        
        /// <summary>
        /// Forces cache invalidation to ensure fresh data on next load.
        /// Used after operations that modify the tree structure (create, update, delete).
        /// </summary>
        Task InvalidateCacheAsync();
    }
}
