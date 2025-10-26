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
            
            _logger.Info("[TodoStore] ⚡ CONSTRUCTOR called - About to subscribe to events");
            
            // Subscribe to category events for automatic cleanup
            SubscribeToEvents();
            
            _logger.Info("[TodoStore] ✅ CONSTRUCTOR complete - Subscriptions registered");
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
                
                // Load ALL todos including completed (for completed items management feature)
                // Filtering will be done at ViewModel layer based on ShowCompleted preference
                var todos = await _repository.GetAllAsync(includeCompleted: true);
                
                using (_todos.BatchUpdate())
                {
                    _todos.Clear();
                    _todos.AddRange(todos);
                }
                
                _isInitialized = true;
                _logger.Info($"[TodoStore] Loaded {todos.Count} todos from database (including completed)");
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
            // Get all todos for this category (exclude orphaned only)
            // Completed filtering is handled at ViewModel layer based on ShowCompleted preference
            // Orphaned todos show in "Uncategorized" virtual category
            var items = _todos.Where(t => t.CategoryId == categoryId && !t.IsOrphaned);
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
            
            _logger.Info($"[TodoStore] Subscribing to NoteNest.Domain.Common.IDomainEvent...");
            
            // Todo CQRS domain events - subscribe to base interface to match published type
            // Handlers publish as: PublishAsync<IDomainEvent>(domainEvent)
            // So we must subscribe to: Subscribe<IDomainEvent>
            _logger.Info($"[TodoStore] 🎯 SUBSCRIBING to IDomainEvent on event bus: {_eventBus.GetType().Name}");
            
            _eventBus.Subscribe<NoteNest.Domain.Common.IDomainEvent>(async domainEvent =>
            {
                try
                {
                    _logger.Info($"[TodoStore] 📬 ⚡⚡⚡ SUBSCRIPTION FIRED! Received event: {domainEvent.GetType().FullName}");
                    
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
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoCompletedAsync (event-driven)");
                            await HandleTodoCompletedEventAsync(e);
                            break;
                            
                        case Domain.Events.TodoUncompletedEvent e:
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoUncompletedEventAsync (event-driven)");
                            await HandleTodoUncompletedEventAsync(e);
                            break;
                            
                        case Domain.Events.TodoTextUpdatedEvent e:
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoTextUpdatedEventAsync (event-driven)");
                            await HandleTodoTextUpdatedEventAsync(e);
                            break;
                            
                        case Domain.Events.TodoDueDateChangedEvent e:
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoDueDateChangedEventAsync (event-driven)");
                            await HandleTodoDueDateChangedEventAsync(e);
                            break;
                            
                        case Domain.Events.TodoPriorityChangedEvent e:
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoPriorityChangedEventAsync (event-driven)");
                            await HandleTodoPriorityChangedEventAsync(e);
                            break;
                            
                        case Domain.Events.TodoFavoritedEvent e:
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoFavoritedEventAsync (event-driven)");
                            await HandleTodoFavoritedEventAsync(e);
                            break;
                            
                        case Domain.Events.TodoUnfavoritedEvent e:
                            _logger.Debug($"[TodoStore] Dispatching to HandleTodoUnfavoritedEventAsync (event-driven)");
                            await HandleTodoUnfavoritedEventAsync(e);
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
        /// Event-driven CQRS pattern: Command handler updates projections first, then publishes event.
        /// Store subscribes and loads from ready database.
        /// </summary>
        private async Task HandleTodoCreatedAsync(Domain.Events.TodoCreatedEvent e)
        {
            try
            {
                _logger.Info($"[TodoStore] 🎯 HandleTodoCreatedAsync STARTED for TodoId: {e.TodoId.Value}");
                _logger.Debug($"[TodoStore] Event details - Text: '{e.Text}', CategoryId: {e.CategoryId}");
                
                // Load fresh todo from database (projections should be updated before event is published)
                _logger.Debug($"[TodoStore] Calling Repository.GetByIdAsync({e.TodoId.Value})...");
                var todo = await _repository.GetByIdAsync(e.TodoId.Value);
                
                if (todo == null)
                {
                    _logger.Error($"[TodoStore] ❌ CRITICAL: Todo not found in database after creation: {e.TodoId.Value}");
                    _logger.Error($"[TodoStore] This means projections haven't run yet - timing issue");
                    return;
                }
                
                _logger.Info($"[TodoStore] ✅ Todo loaded from database: '{todo.Text}', CategoryId: {todo.CategoryId}");
                
                // Add to UI collection (on UI thread)
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Check if already exists (prevent duplicates)
                    var exists = _todos.Any(t => t.Id == todo.Id);
                    
                    if (!exists)
                    {
                        _todos.Add(todo);
                        _logger.Info($"[TodoStore] ✅ Todo added to UI collection: '{todo.Text}'");
                        _logger.Debug($"[TodoStore] Collection count: {_todos.Count}");
                    }
                    else
                    {
                        _logger.Warning($"[TodoStore] ⚠️ Todo already in collection, skipping add");
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
                _logger.Info($"[TodoStore] 🎯🎯🎯 HandleTodoUpdatedAsync STARTED for TodoId: {todoId.Value}");
                
                // Load fresh todo from database
                _logger.Debug($"[TodoStore] Calling Repository.GetByIdAsync({todoId.Value})...");
                var updatedTodo = await _repository.GetByIdAsync(todoId.Value);
                if (updatedTodo == null)
                {
                    _logger.Warning($"[TodoStore] Todo not found in database after update: {todoId.Value}");
                    return;
                }
                
                _logger.Info($"[TodoStore] ✅ Loaded from DB: '{updatedTodo.Text}', IsCompleted={updatedTodo.IsCompleted}, Priority={updatedTodo.Priority}, IsFavorite={updatedTodo.IsFavorite}");
                
                // Update in UI collection (on UI thread)
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _logger.Debug($"[TodoStore] Dispatcher.InvokeAsync executing...");
                    
                    var existing = _todos.FirstOrDefault(t => t.Id == todoId.Value);
                    if (existing != null)
                    {
                        var index = _todos.IndexOf(existing);
                        _logger.Info($"[TodoStore] 🔄 REPLACING in _todos collection at index {index}");
                        _logger.Debug($"[TodoStore]    OLD: '{existing.Text}', IsCompleted={existing.IsCompleted}");
                        _logger.Debug($"[TodoStore]    NEW: '{updatedTodo.Text}', IsCompleted={updatedTodo.IsCompleted}");
                        
                        _todos[index] = updatedTodo;
                        
                        _logger.Info($"[TodoStore] ✅ ✅ ✅ REPLACED in _todos collection! This should fire CollectionChanged.Replace event");
                    }
                    else
                    {
                        // Todo might have been created/moved - add it
                        _todos.Add(updatedTodo);
                        _logger.Info($"[TodoStore] ✅ Added missing todo to UI collection: {updatedTodo.Text}");
                    }
                });
                
                _logger.Info($"[TodoStore] 🏁 HandleTodoUpdatedAsync COMPLETED for TodoId: {todoId.Value}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoStore] Failed to handle todo update event");
            }
        }
        
        /// <summary>
        /// Handles TodoCompletedEvent by applying event data directly to model.
        /// Event-driven approach: Event IS the source of truth, no database query needed.
        /// </summary>
        private async Task HandleTodoCompletedEventAsync(Domain.Events.TodoCompletedEvent e)
        {
            try
            {
                _logger.Info($"[TodoStore] 🎯 HandleTodoCompletedEventAsync (event-driven) for TodoId: {e.TodoId.Value}");
                
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var existing = _todos.FirstOrDefault(t => t.Id == e.TodoId.Value);
                    if (existing == null)
                    {
                        _logger.Warning($"[TodoStore] Todo not found in collection: {e.TodoId.Value}");
                        return;
                    }
                    
                    var index = _todos.IndexOf(existing);
                    _logger.Debug($"[TodoStore] Applying TodoCompletedEvent to item at index {index}");
                    
                    // ✅ Apply event data directly (create new instance for clean replacement)
                    var updated = new TodoItem
                    {
                        Id = existing.Id,
                        CategoryId = existing.CategoryId,
                        ParentId = existing.ParentId,
                        Text = existing.Text,
                        Description = existing.Description,
                        IsCompleted = true,  // ← FROM EVENT
                        CompletedDate = e.OccurredAt,  // ← FROM EVENT
                        DueDate = existing.DueDate,
                        ReminderDate = existing.ReminderDate,
                        Priority = existing.Priority,
                        IsFavorite = existing.IsFavorite,
                        Order = existing.Order,
                        CreatedDate = existing.CreatedDate,
                        ModifiedDate = e.OccurredAt,  // ← FROM EVENT
                        Tags = existing.Tags,
                        SourceNoteId = existing.SourceNoteId,
                        SourceFilePath = existing.SourceFilePath,
                        SourceLineNumber = existing.SourceLineNumber,
                        SourceCharOffset = existing.SourceCharOffset,
                        IsOrphaned = existing.IsOrphaned,
                        LinkedNoteIds = existing.LinkedNoteIds
                    };
                    
                    _logger.Info($"[TodoStore] ✅ Applied event: IsCompleted={updated.IsCompleted}, CompletedDate={updated.CompletedDate}");
                    
                    // Replace triggers CollectionChanged.Replace
                    _todos[index] = updated;
                    
                    _logger.Info($"[TodoStore] ✅ Collection updated - UI should reflect completion immediately");
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoStore] Failed to handle TodoCompletedEvent");
            }
        }
        
        /// <summary>
        /// Handles TodoUncompletedEvent by applying event data directly to model.
        /// </summary>
        private async Task HandleTodoUncompletedEventAsync(Domain.Events.TodoUncompletedEvent e)
        {
            try
            {
                _logger.Info($"[TodoStore] 🎯 HandleTodoUncompletedEventAsync (event-driven) for TodoId: {e.TodoId.Value}");
                
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var existing = _todos.FirstOrDefault(t => t.Id == e.TodoId.Value);
                    if (existing == null) return;
                    
                    var index = _todos.IndexOf(existing);
                    
                    var updated = new TodoItem
                    {
                        Id = existing.Id,
                        CategoryId = existing.CategoryId,
                        ParentId = existing.ParentId,
                        Text = existing.Text,
                        Description = existing.Description,
                        IsCompleted = false,  // ← FROM EVENT
                        CompletedDate = null,  // ← FROM EVENT
                        DueDate = existing.DueDate,
                        ReminderDate = existing.ReminderDate,
                        Priority = existing.Priority,
                        IsFavorite = existing.IsFavorite,
                        Order = existing.Order,
                        CreatedDate = existing.CreatedDate,
                        ModifiedDate = e.OccurredAt,
                        Tags = existing.Tags,
                        SourceNoteId = existing.SourceNoteId,
                        SourceFilePath = existing.SourceFilePath,
                        SourceLineNumber = existing.SourceLineNumber,
                        SourceCharOffset = existing.SourceCharOffset,
                        IsOrphaned = existing.IsOrphaned,
                        LinkedNoteIds = existing.LinkedNoteIds
                    };
                    
                    _todos[index] = updated;
                    _logger.Info($"[TodoStore] ✅ Applied uncomplete event");
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoStore] Failed to handle TodoUncompletedEvent");
            }
        }
        
        /// <summary>
        /// Handles TodoTextUpdatedEvent by applying event data directly to model.
        /// </summary>
        private async Task HandleTodoTextUpdatedEventAsync(Domain.Events.TodoTextUpdatedEvent e)
        {
            try
            {
                _logger.Info($"[TodoStore] 🎯 HandleTodoTextUpdatedEventAsync (event-driven) for TodoId: {e.TodoId.Value}");
                
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var existing = _todos.FirstOrDefault(t => t.Id == e.TodoId.Value);
                    if (existing == null) return;
                    
                    var index = _todos.IndexOf(existing);
                    
                    var updated = new TodoItem
                    {
                        Id = existing.Id,
                        CategoryId = existing.CategoryId,
                        ParentId = existing.ParentId,
                        Text = e.NewText,  // ← FROM EVENT
                        Description = existing.Description,
                        IsCompleted = existing.IsCompleted,
                        CompletedDate = existing.CompletedDate,
                        DueDate = existing.DueDate,
                        ReminderDate = existing.ReminderDate,
                        Priority = existing.Priority,
                        IsFavorite = existing.IsFavorite,
                        Order = existing.Order,
                        CreatedDate = existing.CreatedDate,
                        ModifiedDate = e.OccurredAt,  // ← FROM EVENT
                        Tags = existing.Tags,
                        SourceNoteId = existing.SourceNoteId,
                        SourceFilePath = existing.SourceFilePath,
                        SourceLineNumber = existing.SourceLineNumber,
                        SourceCharOffset = existing.SourceCharOffset,
                        IsOrphaned = existing.IsOrphaned,
                        LinkedNoteIds = existing.LinkedNoteIds
                    };
                    
                    _todos[index] = updated;
                    _logger.Info($"[TodoStore] ✅ Applied text update: '{e.NewText}'");
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoStore] Failed to handle TodoTextUpdatedEvent");
            }
        }
        
        /// <summary>
        /// Handles TodoPriorityChangedEvent by applying event data directly to model.
        /// </summary>
        private async Task HandleTodoPriorityChangedEventAsync(Domain.Events.TodoPriorityChangedEvent e)
        {
            try
            {
                _logger.Info($"[TodoStore] 🎯 HandleTodoPriorityChangedEventAsync (event-driven) for TodoId: {e.TodoId.Value}");
                
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var existing = _todos.FirstOrDefault(t => t.Id == e.TodoId.Value);
                    if (existing == null) return;
                    
                    var index = _todos.IndexOf(existing);
                    
                    var updated = new TodoItem
                    {
                        Id = existing.Id,
                        CategoryId = existing.CategoryId,
                        ParentId = existing.ParentId,
                        Text = existing.Text,
                        Description = existing.Description,
                        IsCompleted = existing.IsCompleted,
                        CompletedDate = existing.CompletedDate,
                        DueDate = existing.DueDate,
                        ReminderDate = existing.ReminderDate,
                        Priority = (Priority)e.NewPriority,  // ← FROM EVENT
                        IsFavorite = existing.IsFavorite,
                        Order = existing.Order,
                        CreatedDate = existing.CreatedDate,
                        ModifiedDate = e.OccurredAt,  // ← FROM EVENT
                        Tags = existing.Tags,
                        SourceNoteId = existing.SourceNoteId,
                        SourceFilePath = existing.SourceFilePath,
                        SourceLineNumber = existing.SourceLineNumber,
                        SourceCharOffset = existing.SourceCharOffset,
                        IsOrphaned = existing.IsOrphaned,
                        LinkedNoteIds = existing.LinkedNoteIds
                    };
                    
                    _todos[index] = updated;
                    _logger.Info($"[TodoStore] ✅ Applied priority change: {e.NewPriority}");
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoStore] Failed to handle TodoPriorityChangedEvent");
            }
        }
        
        /// <summary>
        /// Handles TodoDueDateChangedEvent by applying event data directly to model.
        /// </summary>
        private async Task HandleTodoDueDateChangedEventAsync(Domain.Events.TodoDueDateChangedEvent e)
        {
            try
            {
                _logger.Info($"[TodoStore] 🎯 HandleTodoDueDateChangedEventAsync (event-driven) for TodoId: {e.TodoId.Value}");
                
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var existing = _todos.FirstOrDefault(t => t.Id == e.TodoId.Value);
                    if (existing == null) return;
                    
                    var index = _todos.IndexOf(existing);
                    
                    var updated = new TodoItem
                    {
                        Id = existing.Id,
                        CategoryId = existing.CategoryId,
                        ParentId = existing.ParentId,
                        Text = existing.Text,
                        Description = existing.Description,
                        IsCompleted = existing.IsCompleted,
                        CompletedDate = existing.CompletedDate,
                        DueDate = e.NewDueDate,  // ← FROM EVENT
                        ReminderDate = existing.ReminderDate,
                        Priority = existing.Priority,
                        IsFavorite = existing.IsFavorite,
                        Order = existing.Order,
                        CreatedDate = existing.CreatedDate,
                        ModifiedDate = e.OccurredAt,  // ← FROM EVENT
                        Tags = existing.Tags,
                        SourceNoteId = existing.SourceNoteId,
                        SourceFilePath = existing.SourceFilePath,
                        SourceLineNumber = existing.SourceLineNumber,
                        SourceCharOffset = existing.SourceCharOffset,
                        IsOrphaned = existing.IsOrphaned,
                        LinkedNoteIds = existing.LinkedNoteIds
                    };
                    
                    _todos[index] = updated;
                    _logger.Info($"[TodoStore] ✅ Applied due date change: {e.NewDueDate}");
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoStore] Failed to handle TodoDueDateChangedEvent");
            }
        }
        
        /// <summary>
        /// Handles TodoFavoritedEvent by applying event data directly to model.
        /// </summary>
        private async Task HandleTodoFavoritedEventAsync(Domain.Events.TodoFavoritedEvent e)
        {
            try
            {
                _logger.Info($"[TodoStore] 🎯 HandleTodoFavoritedEventAsync (event-driven) for TodoId: {e.TodoId.Value}");
                
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var existing = _todos.FirstOrDefault(t => t.Id == e.TodoId.Value);
                    if (existing == null) return;
                    
                    var index = _todos.IndexOf(existing);
                    
                    var updated = new TodoItem
                    {
                        Id = existing.Id,
                        CategoryId = existing.CategoryId,
                        ParentId = existing.ParentId,
                        Text = existing.Text,
                        Description = existing.Description,
                        IsCompleted = existing.IsCompleted,
                        CompletedDate = existing.CompletedDate,
                        DueDate = existing.DueDate,
                        ReminderDate = existing.ReminderDate,
                        Priority = existing.Priority,
                        IsFavorite = true,  // ← FROM EVENT
                        Order = existing.Order,
                        CreatedDate = existing.CreatedDate,
                        ModifiedDate = e.OccurredAt,  // ← FROM EVENT
                        Tags = existing.Tags,
                        SourceNoteId = existing.SourceNoteId,
                        SourceFilePath = existing.SourceFilePath,
                        SourceLineNumber = existing.SourceLineNumber,
                        SourceCharOffset = existing.SourceCharOffset,
                        IsOrphaned = existing.IsOrphaned,
                        LinkedNoteIds = existing.LinkedNoteIds
                    };
                    
                    _todos[index] = updated;
                    _logger.Info($"[TodoStore] ✅ Applied favorite event");
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoStore] Failed to handle TodoFavoritedEvent");
            }
        }
        
        /// <summary>
        /// Handles TodoUnfavoritedEvent by applying event data directly to model.
        /// </summary>
        private async Task HandleTodoUnfavoritedEventAsync(Domain.Events.TodoUnfavoritedEvent e)
        {
            try
            {
                _logger.Info($"[TodoStore] 🎯 HandleTodoUnfavoritedEventAsync (event-driven) for TodoId: {e.TodoId.Value}");
                
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var existing = _todos.FirstOrDefault(t => t.Id == e.TodoId.Value);
                    if (existing == null) return;
                    
                    var index = _todos.IndexOf(existing);
                    
                    var updated = new TodoItem
                    {
                        Id = existing.Id,
                        CategoryId = existing.CategoryId,
                        ParentId = existing.ParentId,
                        Text = existing.Text,
                        Description = existing.Description,
                        IsCompleted = existing.IsCompleted,
                        CompletedDate = existing.CompletedDate,
                        DueDate = existing.DueDate,
                        ReminderDate = existing.ReminderDate,
                        Priority = existing.Priority,
                        IsFavorite = false,  // ← FROM EVENT
                        Order = existing.Order,
                        CreatedDate = existing.CreatedDate,
                        ModifiedDate = e.OccurredAt,  // ← FROM EVENT
                        Tags = existing.Tags,
                        SourceNoteId = existing.SourceNoteId,
                        SourceFilePath = existing.SourceFilePath,
                        SourceLineNumber = existing.SourceLineNumber,
                        SourceCharOffset = existing.SourceCharOffset,
                        IsOrphaned = existing.IsOrphaned,
                        LinkedNoteIds = existing.LinkedNoteIds
                    };
                    
                    _todos[index] = updated;
                    _logger.Info($"[TodoStore] ✅ Applied unfavorite event");
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoStore] Failed to handle TodoUnfavoritedEvent");
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
