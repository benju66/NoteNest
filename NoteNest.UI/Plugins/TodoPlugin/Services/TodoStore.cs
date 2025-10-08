using System;
using System.Collections.ObjectModel;
using System.Linq;
using NoteNest.UI.Collections;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Services
{
    /// <summary>
    /// Simple in-memory implementation of ITodoStore.
    /// </summary>
    public class TodoStore : ITodoStore
    {
        private readonly SmartObservableCollection<TodoItem> _todos;

        public TodoStore()
        {
            _todos = new SmartObservableCollection<TodoItem>();
        }

        public ObservableCollection<TodoItem> AllTodos => _todos;

        public ObservableCollection<TodoItem> GetByCategory(Guid? categoryId)
        {
            var filtered = new SmartObservableCollection<TodoItem>();
            var items = _todos.Where(t => t.CategoryId == categoryId);
            filtered.AddRange(items);
            return filtered;
        }

        public ObservableCollection<TodoItem> GetSmartList(SmartListType type)
        {
            return type switch
            {
                SmartListType.Today => GetTodayItems(),
                SmartListType.Overdue => GetOverdueItems(),
                SmartListType.HighPriority => GetHighPriorityItems(),
                SmartListType.Favorites => GetFavoriteItems(),
                SmartListType.All => AllTodos,
                SmartListType.Completed => GetCompletedItems(),
                _ => new SmartObservableCollection<TodoItem>()
            };
        }

        public TodoItem? GetById(Guid id)
        {
            return _todos.FirstOrDefault(t => t.Id == id);
        }

        public void Add(TodoItem todo)
        {
            if (todo == null) throw new ArgumentNullException(nameof(todo));
            _todos.Add(todo);
        }

        public void Update(TodoItem todo)
        {
            if (todo == null) throw new ArgumentNullException(nameof(todo));
            
            var existing = GetById(todo.Id);
            if (existing != null)
            {
                var index = _todos.IndexOf(existing);
                _todos[index] = todo;
            }
        }

        public void Delete(Guid id)
        {
            var todo = GetById(id);
            if (todo != null)
            {
                _todos.Remove(todo);
            }
        }

        private ObservableCollection<TodoItem> GetTodayItems()
        {
            var today = new SmartObservableCollection<TodoItem>();
            var items = _todos.Where(t => !t.IsCompleted && t.IsDueToday())
                             .OrderBy(t => t.Priority)
                             .ThenBy(t => t.Order);
            today.AddRange(items);
            return today;
        }

        private ObservableCollection<TodoItem> GetOverdueItems()
        {
            var overdue = new SmartObservableCollection<TodoItem>();
            var items = _todos.Where(t => t.IsOverdue())
                             .OrderBy(t => t.DueDate)
                             .ThenBy(t => t.Priority);
            overdue.AddRange(items);
            return overdue;
        }

        private ObservableCollection<TodoItem> GetHighPriorityItems()
        {
            var highPriority = new SmartObservableCollection<TodoItem>();
            var items = _todos.Where(t => !t.IsCompleted && t.Priority >= Priority.High)
                             .OrderBy(t => t.Priority)
                             .ThenBy(t => t.DueDate);
            highPriority.AddRange(items);
            return highPriority;
        }

        private ObservableCollection<TodoItem> GetFavoriteItems()
        {
            var favorites = new SmartObservableCollection<TodoItem>();
            var items = _todos.Where(t => t.IsFavorite && !t.IsCompleted)
                             .OrderBy(t => t.Order);
            favorites.AddRange(items);
            return favorites;
        }

        private ObservableCollection<TodoItem> GetCompletedItems()
        {
            var completed = new SmartObservableCollection<TodoItem>();
            var items = _todos.Where(t => t.IsCompleted)
                             .OrderByDescending(t => t.CompletedDate);
            completed.AddRange(items);
            return completed;
        }
    }
}
