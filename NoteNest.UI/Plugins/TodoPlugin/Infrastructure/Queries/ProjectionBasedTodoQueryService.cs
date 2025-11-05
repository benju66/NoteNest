using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Application.Queries;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Queries
{
    /// <summary>
    /// Query service implementation that reads from projections.db.
    /// All todo data comes from the todo_view projection table.
    /// Tags are queried from entity_tags with recursive CTE for inheritance.
    /// </summary>
    public class ProjectionBasedTodoQueryService : ITodoQueryService
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;

        public ProjectionBasedTodoQueryService(string connectionString, IAppLogger logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<TodoDto>> GetAllTodosAsync(bool includeCompleted = false)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT 
                    id AS Id,
                    text AS Text,
                    description AS Description,
                    is_completed AS IsCompleted,
                    datetime(completed_date, 'unixepoch') AS CompletedDate,
                    datetime(due_date, 'unixepoch') AS DueDate,
                    datetime(reminder_date, 'unixepoch') AS ReminderDate,
                    priority AS Priority,
                    is_favorite AS IsFavorite,
                    sort_order AS [Order],
                    datetime(created_at, 'unixepoch') AS CreatedDate,
                    datetime(modified_at, 'unixepoch') AS ModifiedDate,
                    category_id AS CategoryId,
                    parent_id AS ParentId,
                    source_note_id AS SourceNoteId,
                    source_file_path AS SourceFilePath,
                    source_line_number AS SourceLineNumber,
                    source_char_offset AS SourceCharOffset,
                    is_orphaned AS IsOrphaned
                FROM todo_view";

            if (!includeCompleted)
            {
                sql += " WHERE is_completed = 0";
            }

            sql += " ORDER BY sort_order, created_at";

            var todos = await connection.QueryAsync<TodoDto>(sql);
            return todos.ToList();
        }

        public async Task<TodoDto> GetTodoByIdAsync(Guid todoId)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT 
                    id AS Id,
                    text AS Text,
                    description AS Description,
                    is_completed AS IsCompleted,
                    datetime(completed_date, 'unixepoch') AS CompletedDate,
                    datetime(due_date, 'unixepoch') AS DueDate,
                    datetime(reminder_date, 'unixepoch') AS ReminderDate,
                    priority AS Priority,
                    is_favorite AS IsFavorite,
                    sort_order AS [Order],
                    datetime(created_at, 'unixepoch') AS CreatedDate,
                    datetime(modified_at, 'unixepoch') AS ModifiedDate,
                    category_id AS CategoryId,
                    parent_id AS ParentId,
                    source_note_id AS SourceNoteId,
                    source_file_path AS SourceFilePath,
                    source_line_number AS SourceLineNumber,
                    source_char_offset AS SourceCharOffset,
                    is_orphaned AS IsOrphaned
                FROM todo_view
                WHERE id = @TodoId";

            return await connection.QueryFirstOrDefaultAsync<TodoDto>(sql, new { TodoId = todoId.ToString() });
        }

        public async Task<List<TodoDto>> GetTodosByCategoryAsync(Guid categoryId, bool includeCompleted = false)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT 
                    id AS Id,
                    text AS Text,
                    description AS Description,
                    is_completed AS IsCompleted,
                    datetime(completed_date, 'unixepoch') AS CompletedDate,
                    datetime(due_date, 'unixepoch') AS DueDate,
                    datetime(reminder_date, 'unixepoch') AS ReminderDate,
                    priority AS Priority,
                    is_favorite AS IsFavorite,
                    sort_order AS [Order],
                    datetime(created_at, 'unixepoch') AS CreatedDate,
                    datetime(modified_at, 'unixepoch') AS ModifiedDate,
                    category_id AS CategoryId,
                    parent_id AS ParentId,
                    source_note_id AS SourceNoteId,
                    source_file_path AS SourceFilePath,
                    source_line_number AS SourceLineNumber,
                    source_char_offset AS SourceCharOffset,
                    is_orphaned AS IsOrphaned
                FROM todo_view
                WHERE category_id = @CategoryId";

            if (!includeCompleted)
            {
                sql += " AND is_completed = 0";
            }

            sql += " ORDER BY sort_order, created_at";

            var todos = await connection.QueryAsync<TodoDto>(sql, new { CategoryId = categoryId.ToString() });
            return todos.ToList();
        }

        public async Task<List<TodoWithTagsDto>> GetTodosWithInheritedTagsAsync(bool includeCompleted = false)
        {
            var todos = await GetAllTodosAsync(includeCompleted);
            var todosWithTags = new List<TodoWithTagsDto>();

            foreach (var todo in todos)
            {
                var todoWithTags = MapToTodoWithTags(todo);
                todoWithTags.Tags = await GetTagsForTodoAsync(todo.Id, todo.CategoryId);
                todosWithTags.Add(todoWithTags);
            }

            return todosWithTags;
        }

        public async Task<TodoWithTagsDto> GetTodoWithTagsAsync(Guid todoId)
        {
            var todo = await GetTodoByIdAsync(todoId);
            if (todo == null) return null;

            var todoWithTags = MapToTodoWithTags(todo);
            todoWithTags.Tags = await GetTagsForTodoAsync(todo.Id, todo.CategoryId);
            return todoWithTags;
        }

        public async Task<List<TodoDto>> GetSmartListTodosAsync(SmartListType listType)
        {
            return listType switch
            {
                SmartListType.All => await GetAllTodosAsync(false),
                SmartListType.Today => await GetTodosDueTodayAsync(),
                SmartListType.Overdue => await GetOverdueTodosAsync(),
                SmartListType.HighPriority => await GetHighPriorityTodosAsync(),
                SmartListType.Favorites => await GetFavoriteTodosAsync(),
                SmartListType.Completed => await GetCompletedTodosAsync(),
                _ => new List<TodoDto>()
            };
        }

        public async Task<List<TodoDto>> GetOverdueTodosAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT 
                    id AS Id,
                    text AS Text,
                    description AS Description,
                    is_completed AS IsCompleted,
                    datetime(completed_date, 'unixepoch') AS CompletedDate,
                    datetime(due_date, 'unixepoch') AS DueDate,
                    datetime(reminder_date, 'unixepoch') AS ReminderDate,
                    priority AS Priority,
                    is_favorite AS IsFavorite,
                    sort_order AS [Order],
                    datetime(created_at, 'unixepoch') AS CreatedDate,
                    datetime(modified_at, 'unixepoch') AS ModifiedDate,
                    category_id AS CategoryId,
                    parent_id AS ParentId,
                    source_note_id AS SourceNoteId,
                    source_file_path AS SourceFilePath,
                    source_line_number AS SourceLineNumber,
                    source_char_offset AS SourceCharOffset,
                    is_orphaned AS IsOrphaned
                FROM todo_view
                WHERE is_completed = 0 
                    AND due_date IS NOT NULL 
                    AND due_date < @Now
                ORDER BY due_date, priority DESC";

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var todos = await connection.QueryAsync<TodoDto>(sql, new { Now = now });
            return todos.ToList();
        }

        public async Task<List<TodoDto>> GetTodosDueTodayAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var todayStart = new DateTimeOffset(DateTime.UtcNow.Date).ToUnixTimeSeconds();
            var todayEnd = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1).AddSeconds(-1)).ToUnixTimeSeconds();

            var sql = @"
                SELECT 
                    id AS Id,
                    text AS Text,
                    description AS Description,
                    is_completed AS IsCompleted,
                    datetime(completed_date, 'unixepoch') AS CompletedDate,
                    datetime(due_date, 'unixepoch') AS DueDate,
                    datetime(reminder_date, 'unixepoch') AS ReminderDate,
                    priority AS Priority,
                    is_favorite AS IsFavorite,
                    sort_order AS [Order],
                    datetime(created_at, 'unixepoch') AS CreatedDate,
                    datetime(modified_at, 'unixepoch') AS ModifiedDate,
                    category_id AS CategoryId,
                    parent_id AS ParentId,
                    source_note_id AS SourceNoteId,
                    source_file_path AS SourceFilePath,
                    source_line_number AS SourceLineNumber,
                    source_char_offset AS SourceCharOffset,
                    is_orphaned AS IsOrphaned
                FROM todo_view
                WHERE is_completed = 0 
                    AND due_date >= @TodayStart 
                    AND due_date <= @TodayEnd
                ORDER BY priority DESC, due_date";

            var todos = await connection.QueryAsync<TodoDto>(sql, new { TodayStart = todayStart, TodayEnd = todayEnd });
            return todos.ToList();
        }

        public async Task<List<TodoDto>> GetFavoriteTodosAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT 
                    id AS Id,
                    text AS Text,
                    description AS Description,
                    is_completed AS IsCompleted,
                    datetime(completed_date, 'unixepoch') AS CompletedDate,
                    datetime(due_date, 'unixepoch') AS DueDate,
                    datetime(reminder_date, 'unixepoch') AS ReminderDate,
                    priority AS Priority,
                    is_favorite AS IsFavorite,
                    sort_order AS [Order],
                    datetime(created_at, 'unixepoch') AS CreatedDate,
                    datetime(modified_at, 'unixepoch') AS ModifiedDate,
                    category_id AS CategoryId,
                    parent_id AS ParentId,
                    source_note_id AS SourceNoteId,
                    source_file_path AS SourceFilePath,
                    source_line_number AS SourceLineNumber,
                    source_char_offset AS SourceCharOffset,
                    is_orphaned AS IsOrphaned
                FROM todo_view
                WHERE is_completed = 0 AND is_favorite = 1
                ORDER BY sort_order, created_at";

            var todos = await connection.QueryAsync<TodoDto>(sql);
            return todos.ToList();
        }

        public async Task<List<TodoDto>> GetHighPriorityTodosAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT 
                    id AS Id,
                    text AS Text,
                    description AS Description,
                    is_completed AS IsCompleted,
                    datetime(completed_date, 'unixepoch') AS CompletedDate,
                    datetime(due_date, 'unixepoch') AS DueDate,
                    datetime(reminder_date, 'unixepoch') AS ReminderDate,
                    priority AS Priority,
                    is_favorite AS IsFavorite,
                    sort_order AS [Order],
                    datetime(created_at, 'unixepoch') AS CreatedDate,
                    datetime(modified_at, 'unixepoch') AS ModifiedDate,
                    category_id AS CategoryId,
                    parent_id AS ParentId,
                    source_note_id AS SourceNoteId,
                    source_file_path AS SourceFilePath,
                    source_line_number AS SourceLineNumber,
                    source_char_offset AS SourceCharOffset,
                    is_orphaned AS IsOrphaned
                FROM todo_view
                WHERE is_completed = 0 AND priority >= 2
                ORDER BY priority DESC, due_date, created_at";

            var todos = await connection.QueryAsync<TodoDto>(sql);
            return todos.ToList();
        }

        public async Task<List<TodoDto>> GetCompletedTodosAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT 
                    id AS Id,
                    text AS Text,
                    description AS Description,
                    is_completed AS IsCompleted,
                    datetime(completed_date, 'unixepoch') AS CompletedDate,
                    datetime(due_date, 'unixepoch') AS DueDate,
                    datetime(reminder_date, 'unixepoch') AS ReminderDate,
                    priority AS Priority,
                    is_favorite AS IsFavorite,
                    sort_order AS [Order],
                    datetime(created_at, 'unixepoch') AS CreatedDate,
                    datetime(modified_at, 'unixepoch') AS ModifiedDate,
                    category_id AS CategoryId,
                    parent_id AS ParentId,
                    source_note_id AS SourceNoteId,
                    source_file_path AS SourceFilePath,
                    source_line_number AS SourceLineNumber,
                    source_char_offset AS SourceCharOffset,
                    is_orphaned AS IsOrphaned
                FROM todo_view
                WHERE is_completed = 1
                ORDER BY completed_date DESC";

            var todos = await connection.QueryAsync<TodoDto>(sql);
            return todos.ToList();
        }

        public async Task<List<TodoDto>> SearchTodosAsync(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return new List<TodoDto>();

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT 
                    id AS Id,
                    text AS Text,
                    description AS Description,
                    is_completed AS IsCompleted,
                    datetime(completed_date, 'unixepoch') AS CompletedDate,
                    datetime(due_date, 'unixepoch') AS DueDate,
                    datetime(reminder_date, 'unixepoch') AS ReminderDate,
                    priority AS Priority,
                    is_favorite AS IsFavorite,
                    sort_order AS [Order],
                    datetime(created_at, 'unixepoch') AS CreatedDate,
                    datetime(modified_at, 'unixepoch') AS ModifiedDate,
                    category_id AS CategoryId,
                    parent_id AS ParentId,
                    source_note_id AS SourceNoteId,
                    source_file_path AS SourceFilePath,
                    source_line_number AS SourceLineNumber,
                    source_char_offset AS SourceCharOffset,
                    is_orphaned AS IsOrphaned
                FROM todo_view
                WHERE text LIKE @SearchPattern OR description LIKE @SearchPattern
                ORDER BY is_completed, sort_order, created_at";

            var searchPattern = $"%{searchText}%";
            var todos = await connection.QueryAsync<TodoDto>(sql, new { SearchPattern = searchPattern });
            return todos.ToList();
        }

        /// <summary>
        /// Get all tags for a todo including inherited tags from categories.
        /// Uses recursive CTE to walk up the category hierarchy.
        /// </summary>
        private async Task<List<TagDto>> GetTagsForTodoAsync(Guid todoId, Guid? categoryId)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // Recursive CTE to get all parent categories
            var sql = @"
                WITH RECURSIVE
                -- Get the todo's direct tags
                todo_tags AS (
                    SELECT 
                        tag,
                        display_name,
                        source,
                        entity_type,
                        entity_id,
                        datetime(created_at, 'unixepoch') AS created_at
                    FROM entity_tags
                    WHERE entity_id = @TodoId AND entity_type = 'todo'
                ),
                -- Get category hierarchy
                category_hierarchy AS (
                    -- Start with the todo's direct category
                    SELECT id, parent_id
                    FROM tree_nodes
                    WHERE id = @CategoryId AND node_type = 'category'
                    
                    UNION ALL
                    
                    -- Recursively get parent categories
                    SELECT t.id, t.parent_id
                    FROM tree_nodes t
                    INNER JOIN category_hierarchy h ON t.id = h.parent_id
                    WHERE t.node_type = 'category'
                ),
                -- Get tags from all categories in hierarchy
                category_tags AS (
                    SELECT 
                        et.tag,
                        et.display_name,
                        CASE 
                            WHEN et.entity_id = @CategoryId THEN 'auto-path'
                            ELSE 'auto-inherit'
                        END AS source,
                        et.entity_type,
                        et.entity_id,
                        datetime(et.created_at, 'unixepoch') AS created_at
                    FROM entity_tags et
                    INNER JOIN category_hierarchy h ON et.entity_id = h.id
                    WHERE et.entity_type = 'category'
                )
                -- Union all tags
                SELECT * FROM todo_tags
                UNION
                SELECT * FROM category_tags
                ORDER BY source, display_name";

            var parameters = new
            {
                TodoId = todoId.ToString(),
                CategoryId = categoryId?.ToString() ?? Guid.Empty.ToString()
            };

            var tags = await connection.QueryAsync<TagDto>(sql, parameters);
            return tags.ToList();
        }

        private TodoWithTagsDto MapToTodoWithTags(TodoDto todo)
        {
            return new TodoWithTagsDto
            {
                Id = todo.Id,
                Text = todo.Text,
                Description = todo.Description,
                IsCompleted = todo.IsCompleted,
                CompletedDate = todo.CompletedDate,
                DueDate = todo.DueDate,
                ReminderDate = todo.ReminderDate,
                Priority = todo.Priority,
                IsFavorite = todo.IsFavorite,
                Order = todo.Order,
                CreatedDate = todo.CreatedDate,
                ModifiedDate = todo.ModifiedDate,
                CategoryId = todo.CategoryId,
                ParentId = todo.ParentId,
                SourceNoteId = todo.SourceNoteId,
                SourceFilePath = todo.SourceFilePath,
                SourceLineNumber = todo.SourceLineNumber,
                SourceCharOffset = todo.SourceCharOffset,
                IsOrphaned = todo.IsOrphaned,
                Tags = new List<TagDto>()
            };
        }
    }
}
