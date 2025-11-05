using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence
{
    /// <summary>
    /// Legacy interface kept for compatibility.
    /// In event-sourced version, tags are managed through projections.
    /// ViewModels that use this interface should pass null.
    /// </summary>
    public interface ITodoTagRepository
    {
        Task<List<TodoTag>> GetByTodoIdAsync(Guid todoId);
        Task AddAsync(TodoTag tag);
        Task DeleteAsync(Guid todoId, string tag);
        Task DeleteAutoTagsAsync(Guid todoId);
    }
}
