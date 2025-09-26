using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Domain.Common;
using NoteNest.Domain.Notes;
using NoteNest.Domain.Categories;

namespace NoteNest.Application.Common.Interfaces
{
    public interface INoteRepository
    {
        Task<Note> GetByIdAsync(NoteId id);
        Task<IReadOnlyList<Note>> GetByCategoryAsync(CategoryId categoryId);
        Task<IReadOnlyList<Note>> GetPinnedAsync();
        Task<Result> CreateAsync(Note note);
        Task<Result> UpdateAsync(Note note);
        Task<Result> DeleteAsync(NoteId id);
        Task<bool> ExistsAsync(NoteId id);
        Task<bool> TitleExistsInCategoryAsync(CategoryId categoryId, string title, NoteId excludeId = null);
    }
}
