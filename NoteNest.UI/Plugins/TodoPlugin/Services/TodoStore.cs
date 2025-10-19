using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Todos;
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
            // Filter active todos only (exclude orphaned and completed)
            // Orphaned todos show in "Uncategorized", completed in "Completed" smart list
            var items = _todos.Where(t => t.CategoryId == categoryId && 
                                          !t.IsOrphaned &&
                                          !t.IsCompleted);
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
                    if (!todo.IsOrphaned)
                    {
                        // SOFT DELETE: First deletion - Mark as orphaned
                        // Preserves category_id for future restore capability
                        _logger.Info($"[TodoStore] Soft deleting note-linked todo (marking as orphaned): \"{todo.Text}\"");
                        
                        todo.IsOrphaned = true;
                        // category_id PRESERVED (allows restore feature)
                        todo.ModifiedDate = DateTime.UtcNow;
                        await UpdateAsync(todo); // Updates in-memory + database
                        
                        _logger.Info($"[TodoStore] ✅ Todo marked as orphaned: \"{todo.Text}\" - moved to Uncategorized");
                    }
                    else
                    {
                        // HARD DELETE: Second deletion (already orphaned) - User wants it gone
                        _logger.Info($"[TodoStore] Hard deleting already-orphaned todo: \"{todo.Text}\"");
                        
                        _todos.Remove(todo); // Remove from UI immediately
                        
                        var success = await _repository.DeleteAsync(id);
                        if (success)
                        {
                            _logger.Info($"[TodoStore] ✅ Orphaned todo permanently deleted: \"{todo.Text}\"");
                        }
                        else
                        {
                            _logger.Warning($"[TodoStore] ⚠️ Failed to delete orphaned todo from database: {id}");
                        }
                    }
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
        /// Subscribe to todo domain events for CQRS event-driven UI updates.
        /// 
        /// CRITICAL: Subscribe to IDomainEvent (base interface) because handlers publish
        /// events as IDomainEvent type (variable type inference), not concrete type.
        /// Then use pattern matching to dispatch to correct handler.
        /// </summary>
        private void SubscribeToEvents()
        {
            // Category events (not IDomainEvent - keep separate)
            _eventBus.Subscribe<CategoryDeletedEvent>(async e => await HandleCategoryDeletedAsync(e));
            
            // Todo CQRS domain events - subscribe to base interface to match published type
            // Handlers publish as: PublishAsync<IDomainEvent>(domainEvent)
            // So we must subscribe to: Subscribe<IDomainEvent>
            _eventBus.Subscribe<NoteNest.Domain.Common.IDomainEvent>(async domainEvent =>
            {
                try
                {
                    _logger.Debug($"[TodoStore] 📬 Received domain event: {domainEvent.GetType().Name}");
                    
                    // Pattern match on runtime type to dispatch to appropriate handler
                    switch (domainEvent)
                    {
                        case Domain.Events.TodoCreatedEvent e:
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoCreatedAsync");
                            await HandleTodoCreatedAsync(e);
                            break;
                            
                        case Domain.Events.TodoDeletedEvent e:
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoDeletedAsync");
                            await HandleTodoDeletedAsync(e);
                            break;
                            
                        case Domain.Events.TodoCompletedEvent e:
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoUpdatedAsync (Completed)");
                            await HandleTodoUpdatedAsync(e.TodoId);
                            break;
                            
                        case Domain.Events.TodoUncompletedEvent e:
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoUpdatedAsync (Uncompleted)");
                            await HandleTodoUpdatedAsync(e.TodoId);
                            break;
                            
                        case Domain.Events.TodoTextUpdatedEvent e:
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoUpdatedAsync (TextUpdated)");
                            await HandleTodoUpdatedAsync(e.TodoId);
                            break;
                            
                        case Domain.Events.TodoDueDateChangedEvent e:
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoUpdatedAsync (DueDateChanged)");
                            await HandleTodoUpdatedAsync(e.TodoId);
                            break;
                            
                        case Domain.Events.TodoPriorityChangedEvent e:
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoUpdatedAsync (PriorityChanged)");
                            await HandleTodoUpdatedAsync(e.TodoId);
                            break;
                            
                        case Domain.Events.TodoFavoritedEvent e:
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoUpdatedAsync (Favorited)");
                            await HandleTodoUpdatedAsync(e.TodoId);
                            break;
                            
                        case Domain.Events.TodoUnfavoritedEvent e:
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoUpdatedAsync (Unfavorited)");
                            await HandleTodoUpdatedAsync(e.TodoId);
                            break;
                            
                        // Folder tagging events (from Application layer)
                        case NoteNest.Application.FolderTags.Events.FolderTaggedEvent e:
                            _logger.Info($"[TodoStore] Folder {e.FolderId} tagged with {e.Tags.Count} tags: {string.Join(", ", e.Tags)}. New todos will inherit these tags.");
                            // No UI update needed - new todos will automatically get tags via CreateTodoHandler
                            break;
                            
                        case NoteNest.Application.FolderTags.Events.FolderUntaggedEvent e:
                            _logger.Info($"[TodoStore] Folder {e.FolderId} untagged. Removed {e.RemovedTags.Count} tags. Existing todos keep their tags.");
                            // No UI update needed - existing todos keep their tags
                            break;
                            
                        default:
                            _logger.Debug($"[TodoStore] ⚠️ Unhandled domain event type: {domainEvent.GetType().Name}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"[TodoStore] ❌ Error handling domain event: {domainEvent.GetType().Name}");
                    // Don't throw - event handler failures shouldn't crash the application
                }
            });
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
        /// Handles TodoCreatedEvent by loading the new todo from database and adding to collection.
        /// Event-driven CQRS pattern: Command handler saves to DB and publishes event,
        /// Store subscribes and updates UI collection.
        /// </summary>
        private async Task HandleTodoCreatedAsync(Domain.Events.TodoCreatedEvent e)
        {
            try
            {
                _logger.Info($"[TodoStore] 🎯 HandleTodoCreatedAsync STARTED for TodoId: {e.TodoId.Value}");
                _logger.Debug($"[TodoStore] Event details - Text: '{e.Text}', CategoryId: {e.CategoryId}");
                
                // Load fresh todo from database
                _logger.Debug($"[TodoStore] Calling Repository.GetByIdAsync({e.TodoId.Value})...");
                var todo = await _repository.GetByIdAsync(e.TodoId.Value);
                
                if (todo == null)
                {
                    _logger.Error($"[TodoStore] ❌ CRITICAL: Todo not found in database after creation: {e.TodoId.Value}");
                    _logger.Error($"[TodoStore] This means Repository.InsertAsync succeeded but GetByIdAsync failed");
                    _logger.Error($"[TodoStore] Possible timing/transaction/cache issue");
                    return;
                }
                
                _logger.Info($"[TodoStore] ✅ Todo loaded from database: '{todo.Text}', CategoryId: {todo.CategoryId}");
                
                // Add to UI collection (on UI thread)
                _logger.Debug($"[TodoStore] About to invoke on UI thread (Dispatcher.InvokeAsync)...");
                _logger.Debug($"[TodoStore] Current _todos count BEFORE add: {_todos.Count}");
                
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _logger.Debug($"[TodoStore] ✅ Dispatcher.InvokeAsync lambda executing on UI thread");
                    
                    // Check if already exists (prevent duplicates)
                    var exists = _todos.Any(t => t.Id == todo.Id);
                    _logger.Debug($"[TodoStore] Duplicate check - Todo already exists: {exists}");
                    
                    if (!exists)
                    {
                        _logger.Info($"[TodoStore] ➕ Adding todo to _todos collection...");
                        _todos.Add(todo);
                        _logger.Info($"[TodoStore] ✅ Todo added to _todos collection: '{todo.Text}'");
                        _logger.Info($"[TodoStore] Collection count after add: {_todos.Count}");
                        _logger.Info($"[TodoStore] This should fire CollectionChanged event...");
                    }
                    else
                    {
                        _logger.Warning($"[TodoStore] ⚠️ Todo already in collection, skipping add (duplicate)");
                    }
                });
                
                _logger.Info($"[TodoStore] 🏁 HandleTodoCreatedAsync COMPLETED for TodoId: {e.TodoId.Value}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoStore] Failed to handle TodoCreatedEvent");
            }
        }
        
        /// <summary>
        /// Handles TodoDeletedEvent by removing todo from collection.
        /// </summary>
        private async Task HandleTodoDeletedAsync(Domain.Events.TodoDeletedEvent e)
        {
            try
            {
                _logger.Info($"[TodoStore] Handling TodoDeletedEvent: {e.TodoId.Value}");
                
                // Remove from UI collection (on UI thread)
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var existing = _todos.FirstOrDefault(t => t.Id == e.TodoId.Value);
                    if (existing != null)
                    {
                        _todos.Remove(existing);
                        _logger.Info($"[TodoStore] ✅ Removed todo from UI collection: {e.TodoId.Value}");
                        _logger.Debug($"[TodoStore] Collection count after remove: {_todos.Count}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoStore] Failed to handle TodoDeletedEvent");
            }
        }
        
        /// <summary>
        /// Handles todo update events by reloading from database and updating collection.
        /// Used for: Complete, Uncomplete, TextUpdate, DueDateChange, PriorityChange, Favorite.
        /// </summary>
        private async Task HandleTodoUpdatedAsync(TodoId todoId)
        {
            try
            {
                _logger.Debug($"[TodoStore] Handling todo update event: {todoId.Value}");
                
                // Load fresh todo from database
                var updatedTodo = await _repository.GetByIdAsync(todoId.Value);
                if (updatedTodo == null)
                {
                    _logger.Warning($"[TodoStore] Todo not found in database after update: {todoId.Value}");
                    return;
                }
                
                // Update in UI collection (on UI thread)
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var existing = _todos.FirstOrDefault(t => t.Id == todoId.Value);
                    if (existing != null)
                    {
                        var index = _todos.IndexOf(existing);
                        _todos[index] = updatedTodo;
                        _logger.Debug($"[TodoStore] ✅ Updated todo in UI collection: {updatedTodo.Text}");
                    }
                    else
                    {
                        // Todo might have been created/moved - add it
                        _todos.Add(updatedTodo);
                        _logger.Debug($"[TodoStore] ✅ Added missing todo to UI collection: {updatedTodo.Text}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoStore] Failed to handle todo update event");
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
