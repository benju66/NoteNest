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
    }

    // Placeholder Category domain model for now
    public class Category
    {
        public CategoryId Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public CategoryId ParentId { get; set; }
    }
}
