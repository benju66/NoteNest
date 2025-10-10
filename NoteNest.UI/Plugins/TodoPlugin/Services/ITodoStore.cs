using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Services
{
    /// <summary>
    /// Interface for Todo data store.
    /// </summary>
    public interface ITodoStore
    {
        ObservableCollection<TodoItem> AllTodos { get; }
        ObservableCollection<TodoItem> GetByCategory(Guid? categoryId);
        ObservableCollection<TodoItem> GetSmartList(SmartListType type);
        TodoItem? GetById(Guid id);
        Task AddAsync(TodoItem todo);
        Task UpdateAsync(TodoItem todo);
        Task DeleteAsync(Guid id);
        
        /// <summary>
        /// Ensures the store is initialized from database (lazy, thread-safe).
        /// Safe to call multiple times. Waits if initialization is in progress.
        /// </summary>
        Task EnsureInitializedAsync();
    }
}
