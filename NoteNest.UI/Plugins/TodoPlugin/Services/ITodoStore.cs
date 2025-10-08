using System;
using System.Collections.ObjectModel;
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
        void Add(TodoItem todo);
        void Update(TodoItem todo);
        void Delete(Guid id);
    }
}
