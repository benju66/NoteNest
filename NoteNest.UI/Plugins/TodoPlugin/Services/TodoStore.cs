using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Collections;
using NoteNest.UI.Plugins.TodoPlugin.Events;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Services
{
    /// <summary>
    /// Todo store with database persistence.
    /// Maintains ObservableCollection for UI binding while persisting to SQLite.
    /// Uses lazy initialization pattern for optimal performance and thread safety.
    /// Subscribes to category events for automatic orphaned todo management.
    /// </summary>
    public class TodoStore : ITodoStore, IDisposable
    {
        private readonly ITodoRepository _repository;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;
        private readonly SmartObservableCollection<TodoItem> _todos;
        private bool _isInitialized;
        
        // Lazy initialization tracking (thread-safe, matches TreeDatabaseRepository pattern)
        private Task? _initializationTask;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        public TodoStore(ITodoRepository repository, IEventBus eventBus, IAppLogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _todos = new SmartObservableCollection<TodoItem>();
            
            // Subscribe to category events for automatic cleanup
            SubscribeToEvents();
        }

        /// <summary>
        /// Initialize the store by loading todos from database.
        /// Call this once during plugin startup.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            try
            {
                _logger.Info("[TodoStore] Initializing from database...");
                
                var todos = await _repository.GetAllAsync(includeCompleted: false);
                
                using (_todos.BatchUpdate())
                {
                    _todos.Clear();
                    _todos.AddRange(todos);
                }
                
                _isInitialized = true;
                _logger.Info($"[TodoStore] Loaded {todos.Count} active todos from database");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoStore] Failed to initialize from database - starting with empty collection");
                // Don't throw - graceful degradation with empty collection
                _isInitialized = true;  // Mark as initialized anyway
            }
        }
        
        /// <summary>
        /// Ensures TodoStore is initialized exactly once, thread-safely.
        /// Safe to call multiple times. Waits if initialization in progress.
        /// Enterprise pattern: Lazy initialization with double-check locking.
        /// </summary>
        public async Task EnsureInitializedAsync()
        {
            // Fast path: Already initialized (99% of calls after first load)
            if (_isInitialized)
                return;
            
            // Slow path: Need to initialize (only on first access)
            if (_initializationTask == null)
            {
                await _initLock.WaitAsync();
                try
                {
                    // Double-check after acquiring lock (handles race conditions)
                    if (_initializationTask == null)
                    {
                        _logger.Debug("[TodoStore] Starting lazy initialization...");
                        _initializationTask = InitializeAsync();
                    }
                }
                finally
                {
                    _initLock.Release();
                }
            }
            
            // Wait for initialization to complete (if in progress)
            await _initializationTask;
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

        public async Task AddAsync(TodoItem todo)
        {
            if (todo == null) throw new ArgumentNullException(nameof(todo));
            
            _todos.Add(todo);  // Update UI immediately
            
            try
            {
                // Actually await the database insert
                var success = await _repository.InsertAsync(todo);
                if (success)
                {
                    _logger.Info($"[TodoStore] ✅ Todo saved to database: {todo.Text}");
                }
                else
                {
                    _logger.Warning($"[TodoStore] ⚠️ Failed to save todo (returned false): {todo.Text}");
                    throw new Exception("Database insert returned false");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoStore] ❌ Failed to persist new todo: {todo.Text}");
                // Remove from UI since save failed
                _todos.Remove(todo);
                throw;  // Propagate to ViewModel
            }
        }

        public async Task UpdateAsync(TodoItem todo)
        {
            if (todo == null) throw new ArgumentNullException(nameof(todo));
            
            var existing = GetById(todo.Id);
            if (existing != null)
            {
                var index = _todos.IndexOf(existing);
                _todos[index] = todo;
                
                try
                {
                    var success = await _repository.UpdateAsync(todo);
                    if (success)
                    {
                        _logger.Debug($"[TodoStore] ✅ Todo updated in database: {todo.Text}");
                    }
                    else
                    {
                        _logger.Warning($"[TodoStore] ⚠️ Failed to update todo (returned false): {todo.Text}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"[TodoStore] ❌ Failed to persist todo update: {todo.Id}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Delete a todo using hybrid strategy:
        /// - Manual todos: Hard delete (permanent removal)
        /// - Note-linked todos: Soft delete (mark as orphaned for user visibility)
        /// </summary>
        public async Task DeleteAsync(Guid id)
        {
            var todo = GetById(id);
            if (todo == null)
            {
                _logger.Warning($"[TodoStore] Cannot delete todo - not found: {id}");
                return;
            }
            
            try
            {
                if (todo.SourceNoteId.HasValue)
                {
                    // SOFT DELETE: Note-linked todo → Mark as orphaned
                    _logger.Info($"[TodoStore] Soft deleting note-linked todo (marking as orphaned): \"{todo.Text}\"");
                    
                    todo.IsOrphaned = true;
                    todo.ModifiedDate = DateTime.UtcNow;
                    await UpdateAsync(todo); // Updates in-memory + database
                    
                    _logger.Info($"[TodoStore] ✅ Todo marked as orphaned: \"{todo.Text}\"");
                }
                else
                {
                    // HARD DELETE: Manual todo → Permanent removal
                    _logger.Info($"[TodoStore] Hard deleting manual todo: \"{todo.Text}\"");
                    
                    _todos.Remove(todo); // Remove from UI immediately
                    
                    var success = await _repository.DeleteAsync(id);
                    if (success)
                    {
                        _logger.Info($"[TodoStore] ✅ Todo permanently deleted: \"{todo.Text}\"");
                    }
                    else
                    {
                        _logger.Warning($"[TodoStore] ⚠️ Failed to delete todo from database (returned false): {id}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoStore] ❌ Failed to delete todo: {id}");
                throw;
            }
        }

        /// <summary>
        /// Reload all todos from database (for refresh scenarios).
        /// </summary>
        public async Task ReloadAsync()
        {
            try
            {
                var todos = await _repository.GetAllAsync(includeCompleted: false);
                
                using (_todos.BatchUpdate())
                {
                    _todos.Clear();
                    _todos.AddRange(todos);
                }
                
                _logger.Debug($"[TodoStore] Reloaded {todos.Count} todos from database");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoStore] Failed to reload from database");
            }
        }

        private ObservableCollection<TodoItem> GetTodayItems()
        {
            var today = new SmartObservableCollection<TodoItem>();
            var items = _todos.Where(t => !t.IsCompleted && 
                                        (t.DueDate == null || t.DueDate.Value.Date <= DateTime.Today))
                             .OrderBy(t => t.DueDate ?? DateTime.MaxValue)  // Null dates last
                             .ThenBy(t => t.Priority)
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
            // Show completed todos from in-memory collection
            var completed = new SmartObservableCollection<TodoItem>();
            var items = _todos.Where(t => t.IsCompleted)
                             .OrderByDescending(t => t.CompletedDate);
            completed.AddRange(items);
            return completed;
        }
        
        /// <summary>
        /// Load completed todos from database (for completed view)
        /// </summary>
        public async Task LoadCompletedTodosAsync()
        {
            try
            {
                var completedTodos = await _repository.GetRecentlyCompletedAsync(100);
                
                using (_todos.BatchUpdate())
                {
                    // Add completed todos to collection if not already there
                    foreach (var todo in completedTodos.Where(t => !_todos.Any(x => x.Id == t.Id)))
                    {
                        _todos.Add(todo);
                    }
                }
                
                _logger.Debug($"[TodoStore] Loaded {completedTodos.Count} completed todos from database");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoStore] Failed to load completed todos");
            }
        }
        
        /// <summary>
        /// Subscribe to category events for automatic orphaned todo management.
        /// </summary>
        private void SubscribeToEvents()
        {
            _eventBus.Subscribe<CategoryDeletedEvent>(async e => await HandleCategoryDeletedAsync(e));
        }
        
        /// <summary>
        /// Handles category deletion by setting affected todos' category_id = NULL.
        /// This makes them appear in the "Uncategorized" virtual category.
        /// </summary>
        private async Task HandleCategoryDeletedAsync(CategoryDeletedEvent e)
        {
            try
            {
                _logger.Info($"[TodoStore] Handling category deletion: {e.CategoryName} ({e.CategoryId})");
                
                // Find all todos in the deleted category
                var affectedTodos = _todos.Where(t => t.CategoryId == e.CategoryId).ToList();
                
                if (affectedTodos.Count == 0)
                {
                    _logger.Debug($"[TodoStore] No todos affected by category deletion");
                    return;
                }
                
                _logger.Info($"[TodoStore] Setting category_id = NULL for {affectedTodos.Count} orphaned todos");
                
                // Set category_id = NULL for each affected todo
                foreach (var todo in affectedTodos)
                {
                    todo.CategoryId = null;
                    todo.ModifiedDate = DateTime.UtcNow;
                    await UpdateAsync(todo);
                }
                
                _logger.Info($"[TodoStore] ✅ Successfully orphaned {affectedTodos.Count} todos - they will appear in 'Uncategorized'");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoStore] Failed to handle category deletion: {e.CategoryId}");
                // Don't throw - event handler failures shouldn't crash the application
            }
        }
        
        /// <summary>
        /// Dispose resources (SemaphoreSlim must be disposed to prevent handle leaks).
        /// </summary>
        public void Dispose()
        {
            _initLock?.Dispose();
        }
    }
}
