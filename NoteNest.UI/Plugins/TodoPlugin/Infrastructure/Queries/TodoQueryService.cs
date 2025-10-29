using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Application.Queries;
using NoteNest.UI.Plugins.TodoPlugin.Models;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Todos;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Queries
{
    /// <summary>
    /// Query service for todo projection.
    /// Queries projections.db todo_view table for basic data.
    /// Queries events.db for mutable state (completion, priority, due date) - fixes persistence bug.
    /// Hybrid approach: Immutable data from projection, mutable state from events.
    /// </summary>
    public class TodoQueryService : ITodoQueryService
    {
        private readonly string _connectionString;
        private readonly IEventStore _eventStore;
        private readonly IAppLogger _logger;

        public TodoQueryService(
            string projectionsConnectionString, 
            IEventStore eventStore,
            IAppLogger logger)
        {
            _connectionString = projectionsConnectionString ?? throw new ArgumentNullException(nameof(projectionsConnectionString));
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<TodoItem> GetByIdAsync(Guid id)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                await connection.ExecuteAsync("PRAGMA read_uncommitted = 1");

                var dto = await connection.QueryFirstOrDefaultAsync<TodoDto>(
                    @"SELECT id, text, description, is_completed, completed_date,
                             category_id AS CategoryId, category_name AS CategoryName, category_path AS CategoryPath,
                             parent_id AS ParentId, sort_order AS SortOrder, priority, is_favorite AS IsFavorite,
                             due_date AS DueDate, reminder_date AS ReminderDate,
                             source_type AS SourceType, source_note_id AS SourceNoteId, source_file_path AS SourceFilePath,
                             source_line_number AS SourceLineNumber, source_char_offset AS SourceCharOffset,
                             is_orphaned AS IsOrphaned, created_at AS CreatedAt, modified_at AS ModifiedAt
                      FROM todo_view WHERE id = @Id",
                    new { Id = id.ToString() });

                if (dto != null)
                {
                    var mapped = MapToTodoItem(dto);
                    
                    // ✅ FIX: Get mutable state from events.db (reliable persistence)
                    await ApplyEventBasedStateAsync(mapped);
                    
                    _logger.Debug($"[TodoQueryService] Loaded todo from projection + events: '{mapped.Text}' | IsCompleted={mapped.IsCompleted} | Priority={mapped.Priority}");
                    return mapped;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get todo by ID: {id}", ex);
                return null;
            }
        }

        public async Task<List<TodoItem>> GetByCategoryAsync(Guid? categoryId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // Enable reading from WAL
                await connection.ExecuteAsync("PRAGMA read_uncommitted = 1");

                string sql;
                object param;

                var selectColumns = @"SELECT id, text, description, is_completed, completed_date,
                                             category_id AS CategoryId, category_name AS CategoryName, category_path AS CategoryPath,
                                             parent_id AS ParentId, sort_order AS SortOrder, priority, is_favorite AS IsFavorite,
                                             due_date AS DueDate, reminder_date AS ReminderDate,
                                             source_type AS SourceType, source_note_id AS SourceNoteId, source_file_path AS SourceFilePath,
                                             source_line_number AS SourceLineNumber, source_char_offset AS SourceCharOffset,
                                             is_orphaned AS IsOrphaned, created_at AS CreatedAt, modified_at AS ModifiedAt
                                      FROM todo_view";
                
                if (categoryId.HasValue)
                {
                    sql = $"{selectColumns} WHERE category_id = @CategoryId ORDER BY sort_order, created_at";
                    param = new { CategoryId = categoryId.Value.ToString() };
                }
                else
                {
                    sql = $"{selectColumns} WHERE category_id IS NULL ORDER BY sort_order, created_at";
                    param = null;
                }

                var dtos = await connection.QueryAsync<TodoDto>(sql, param);
                return dtos.Select(MapToTodoItem).Where(t => t != null).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get todos by category: {categoryId}", ex);
                return new List<TodoItem>();
            }
        }

        public async Task<List<TodoItem>> GetSmartListAsync(Models.SmartListType type)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var todayEnd = new DateTimeOffset(DateTimeOffset.UtcNow.Date.AddDays(1).AddSeconds(-1)).ToUnixTimeSeconds();

                var sql = type switch
                {
                    Models.SmartListType.Today => 
                        $"SELECT * FROM todo_view WHERE is_completed = 0 AND (due_date IS NULL OR due_date <= {todayEnd}) ORDER BY priority DESC, due_date",
                    
                    Models.SmartListType.Overdue => 
                        $"SELECT * FROM todo_view WHERE is_completed = 0 AND due_date < {now} ORDER BY due_date",
                    
                    Models.SmartListType.ThisWeek => 
                        $"SELECT * FROM todo_view WHERE is_completed = 0 AND due_date > {todayEnd} ORDER BY due_date LIMIT 50",
                    
                    Models.SmartListType.Completed => 
                        "SELECT * FROM todo_view WHERE is_completed = 1 ORDER BY completed_date DESC LIMIT 100",
                    
                    Models.SmartListType.Favorites => 
                        "SELECT * FROM todo_view WHERE is_favorite = 1 AND is_completed = 0 ORDER BY priority DESC, due_date",
                    
                    _ => "SELECT * FROM todo_view WHERE is_completed = 0 ORDER BY sort_order, created_at"
                };

                var dtos = await connection.QueryAsync<TodoDto>(sql);
                return dtos.Select(MapToTodoItem).Where(t => t != null).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get smart list: {type}", ex);
                return new List<TodoItem>();
            }
        }

        public async Task<List<TodoItem>> GetByNoteIdAsync(Guid noteId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var dtos = await connection.QueryAsync<TodoDto>(
                    @"SELECT * FROM todo_view 
                      WHERE source_note_id = @NoteId
                      ORDER BY sort_order",
                    new { NoteId = noteId.ToString() });

                return dtos.Select(MapToTodoItem).Where(t => t != null).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get todos by note ID: {noteId}", ex);
                return new List<TodoItem>();
            }
        }

        public async Task<List<TodoItem>> SearchAsync(string query)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var dtos = await connection.QueryAsync<TodoDto>(
                    @"SELECT * FROM todo_view 
                      WHERE text LIKE @Query OR description LIKE @Query
                      ORDER BY is_completed, priority DESC, created_at
                      LIMIT 100",
                    new { Query = $"%{query}%" });

                return dtos.Select(MapToTodoItem).Where(t => t != null).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to search todos: {query}", ex);
                return new List<TodoItem>();
            }
        }

        public async Task<List<TodoItem>> GetAllAsync(bool includeCompleted = true)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // Enable reading from WAL
                await connection.ExecuteAsync("PRAGMA read_uncommitted = 1");

                var selectColumns = @"SELECT id, text, description, is_completed, completed_date,
                                             category_id AS CategoryId, category_name AS CategoryName, category_path AS CategoryPath,
                                             parent_id AS ParentId, sort_order AS SortOrder, priority, is_favorite AS IsFavorite,
                                             due_date AS DueDate, reminder_date AS ReminderDate,
                                             source_type AS SourceType, source_note_id AS SourceNoteId, source_file_path AS SourceFilePath,
                                             source_line_number AS SourceLineNumber, source_char_offset AS SourceCharOffset,
                                             is_orphaned AS IsOrphaned, created_at AS CreatedAt, modified_at AS ModifiedAt
                                      FROM todo_view";
                
                var sql = includeCompleted
                    ? $"{selectColumns} ORDER BY is_completed, sort_order, created_at"
                    : $"{selectColumns} WHERE is_completed = 0 ORDER BY sort_order, created_at";

                var dtos = await connection.QueryAsync<TodoDto>(sql);
                var todos = dtos.Select(MapToTodoItem).Where(t => t != null).ToList();
                
                // ✅ FIX: Apply event-based state for mutable fields (completion, priority, due date)
                // This bypasses broken projection update persistence
                _logger.Info($"[TodoQueryService] Loaded {todos.Count} todos from todo_view, now applying event-based state...");
                
                foreach (var todo in todos)
                {
                    await ApplyEventBasedStateAsync(todo);
                }
                
                // ✅ DIAGNOSTIC: Log final state after merging with events
                _logger.Info($"[TodoQueryService] GetAllAsync returned {todos.Count} todos with event-based state applied");
                foreach (var todo in todos)
                {
                    _logger.Debug($"[TodoQueryService]   - {todo.Text} (IsCompleted={todo.IsCompleted}, Priority={todo.Priority}, CategoryId={todo.CategoryId})");
                }
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to get all todos", ex);
                return new List<TodoItem>();
            }
        }

        private TodoItem MapToTodoItem(TodoDto dto)
        {
            try
            {
                return new TodoItem
                {
                    Id = Guid.Parse(dto.Id),
                    Text = dto.Text,
                    Description = dto.Description,
                    IsCompleted = dto.IsCompleted == 1,
                    CompletedDate = dto.CompletedDate.HasValue 
                        ? DateTimeOffset.FromUnixTimeSeconds(dto.CompletedDate.Value).DateTime 
                        : (DateTime?)null,
                    CategoryId = string.IsNullOrEmpty(dto.CategoryId) ? null : Guid.Parse(dto.CategoryId),
                    ParentId = string.IsNullOrEmpty(dto.ParentId) ? null : Guid.Parse(dto.ParentId),
                    Order = dto.SortOrder,
                    Priority = (Models.Priority)dto.Priority,
                    IsFavorite = dto.IsFavorite == 1,
                    DueDate = dto.DueDate.HasValue 
                        ? DateTimeOffset.FromUnixTimeSeconds(dto.DueDate.Value).DateTime 
                        : (DateTime?)null,
                    ReminderDate = dto.ReminderDate.HasValue 
                        ? DateTimeOffset.FromUnixTimeSeconds(dto.ReminderDate.Value).DateTime 
                        : (DateTime?)null,
                    SourceNoteId = string.IsNullOrEmpty(dto.SourceNoteId) ? null : Guid.Parse(dto.SourceNoteId),
                    SourceFilePath = dto.SourceFilePath,
                    SourceLineNumber = dto.SourceLineNumber,
                    SourceCharOffset = dto.SourceCharOffset,
                    IsOrphaned = dto.IsOrphaned == 1,
                    CreatedDate = DateTimeOffset.FromUnixTimeSeconds(dto.CreatedAt).DateTime,
                    ModifiedDate = DateTimeOffset.FromUnixTimeSeconds(dto.ModifiedAt).DateTime,
                    Tags = new List<string>() // Tags loaded separately if needed
                };
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to map todo: {dto?.Text}", ex);
                return null;
            }
        }

        // DTOs for Dapper
        /// <summary>
        /// Apply mutable state from events.db to TodoItem.
        /// Fixes persistence bug where projections.db updates don't persist.
        /// Queries event store for latest completion, priority, due date, and favorite state.
        /// </summary>
        private async Task ApplyEventBasedStateAsync(TodoItem todo)
        {
            try
            {
                // Get all events for this todo from events.db
                var allEvents = await _eventStore.GetEventsAsync(todo.Id);
                
                if (allEvents == null || !allEvents.Any())
                {
                    _logger.Debug($"[TodoQueryService] No events found for todo {todo.Id}, using projection defaults");
                    return;  // Use whatever was in projection
                }
                
                // ✅ Completion State (most recent completion/uncompletion event wins)
                var completionEvents = allEvents
                    .Where(e => e is NoteNest.UI.Plugins.TodoPlugin.Domain.Events.TodoCompletedEvent || 
                                e is NoteNest.UI.Plugins.TodoPlugin.Domain.Events.TodoUncompletedEvent)
                    .OrderByDescending(e => e.OccurredAt)
                    .FirstOrDefault();
                
                if (completionEvents != null)
                {
                    todo.IsCompleted = (completionEvents is NoteNest.UI.Plugins.TodoPlugin.Domain.Events.TodoCompletedEvent);
                    if (todo.IsCompleted && completionEvents is NoteNest.UI.Plugins.TodoPlugin.Domain.Events.TodoCompletedEvent)
                    {
                        todo.CompletedDate = completionEvents.OccurredAt;
                    }
                    else
                    {
                        todo.CompletedDate = null;
                    }
                    
                    _logger.Debug($"[TodoQueryService] Applied completion from events: {todo.Text} → IsCompleted={todo.IsCompleted}");
                }
                
                // ✅ Priority State (most recent priority change event)
                var priorityEvent = allEvents
                    .OfType<NoteNest.UI.Plugins.TodoPlugin.Domain.Events.TodoPriorityChangedEvent>()
                    .OrderByDescending(e => e.OccurredAt)
                    .FirstOrDefault();
                
                if (priorityEvent != null)
                {
                    todo.Priority = (Priority)priorityEvent.NewPriority;
                    _logger.Debug($"[TodoQueryService] Applied priority from events: {todo.Text} → Priority={todo.Priority}");
                }
                
                // ✅ Due Date State (most recent due date change)
                var dueDateEvent = allEvents
                    .OfType<NoteNest.UI.Plugins.TodoPlugin.Domain.Events.TodoDueDateChangedEvent>()
                    .OrderByDescending(e => e.OccurredAt)
                    .FirstOrDefault();
                
                if (dueDateEvent != null)
                {
                    todo.DueDate = dueDateEvent.NewDueDate;
                    _logger.Debug($"[TodoQueryService] Applied due date from events: {todo.Text} → DueDate={todo.DueDate}");
                }
                
                // ✅ Favorite State (most recent favorite/unfavorite event)
                var favoriteEvents = allEvents
                    .Where(e => e is NoteNest.UI.Plugins.TodoPlugin.Domain.Events.TodoFavoritedEvent || 
                                e is NoteNest.UI.Plugins.TodoPlugin.Domain.Events.TodoUnfavoritedEvent)
                    .OrderByDescending(e => e.OccurredAt)
                    .FirstOrDefault();
                
                if (favoriteEvents != null)
                {
                    todo.IsFavorite = (favoriteEvents is NoteNest.UI.Plugins.TodoPlugin.Domain.Events.TodoFavoritedEvent);
                    _logger.Debug($"[TodoQueryService] Applied favorite from events: {todo.Text} → IsFavorite={todo.IsFavorite}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoQueryService] Failed to apply event-based state for todo {todo.Id}, using projection defaults");
                // Don't throw - use projection defaults if event query fails
            }
        }

        private class TodoDto
        {
            public string Id { get; set; }
            public string Text { get; set; }
            public string Description { get; set; }
            public int IsCompleted { get; set; }
            public long? CompletedDate { get; set; }
            public string CategoryId { get; set; }
            public string CategoryName { get; set; }
            public string ParentId { get; set; }
            public int SortOrder { get; set; }
            public int Priority { get; set; }
            public int IsFavorite { get; set; }
            public long? DueDate { get; set; }
            public long? ReminderDate { get; set; }
            public string SourceType { get; set; }
            public string SourceNoteId { get; set; }
            public string SourceFilePath { get; set; }
            public int? SourceLineNumber { get; set; }
            public int? SourceCharOffset { get; set; }
            public int IsOrphaned { get; set; }
            public long CreatedAt { get; set; }
            public long ModifiedAt { get; set; }
        }

        private class TagCloudItem
        {
            public string DisplayName { get; set; }
            public int UsageCount { get; set; }
        }

        private class TagSuggestionDb
        {
            public string Tag { get; set; }
            public string DisplayName { get; set; }
            public int UsageCount { get; set; }
        }

        private class EntityWithTagDb
        {
            public string EntityId { get; set; }
            public string EntityType { get; set; }
            public string Tag { get; set; }
        }
    }
}

