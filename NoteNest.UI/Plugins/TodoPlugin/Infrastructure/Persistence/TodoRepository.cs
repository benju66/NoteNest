using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence
{
    /// <summary>
    /// High-performance SQLite repository for todo operations.
    /// Follows TreeDatabaseRepository pattern with Dapper.
    /// </summary>
    public class TodoRepository : ITodoRepository
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;
        private readonly SemaphoreSlim _dbLock = new SemaphoreSlim(1, 1);
        
        public TodoRepository(string connectionString, IAppLogger logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        // =========================================================================
        // CORE CRUD OPERATIONS
        // =========================================================================
        
        public async Task<TodoItem?> GetByIdAsync(Guid id)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "SELECT * FROM todos WHERE id = @Id";
                // FIX: Query directly to TodoItem (matches all other queries)
                var todo = await connection.QuerySingleOrDefaultAsync<TodoItem>(sql, new { Id = id.ToString() });
                
                if (todo != null)
                {
                    var tags = await GetTagsForTodoAsync(id);
                    todo.Tags = tags;
                }
                
                return todo;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to get todo by id: {id}");
                return null;
            }
        }
        
        public async Task<List<TodoItem>> GetAllAsync(bool includeCompleted = true)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = includeCompleted 
                    ? "SELECT * FROM todos ORDER BY sort_order ASC"
                    : "SELECT * FROM todos WHERE is_completed = 0 ORDER BY sort_order ASC";
                    
                // DIAGNOSTIC: Check raw SQL result BEFORE Dapper mapping
                var rawResults = (await connection.QueryAsync(sql)).ToList();
                foreach (var row in rawResults)
                {
                    var rawCategoryId = ((IDictionary<string, object>)row).ContainsKey("category_id") 
                        ? ((IDictionary<string, object>)row)["category_id"] 
                        : null;
                    _logger.Debug($"[TodoRepository] RAW SQL result: category_id column = '{rawCategoryId}' (type: {rawCategoryId?.GetType().Name ?? "null"})");
                }
                
                // WORKAROUND: Dapper isn't calling type handler, so manually map the problematic fields
                var todos = new List<TodoItem>();
                foreach (var row in rawResults)
                {
                    var dict = (IDictionary<string, object>)row;
                    var todo = new TodoItem
                    {
                        Id = Guid.Parse((string)dict["id"]),
                        Text = (string)dict["text"],
                        Description = dict["description"] as string,
                        IsCompleted = Convert.ToBoolean(dict["is_completed"]),
                        CompletedDate = dict["completed_date"] != null && dict["completed_date"] is not DBNull
                            ? DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(dict["completed_date"])).UtcDateTime
                            : null,
                        CategoryId = dict["category_id"] != null && dict["category_id"] is not DBNull && !string.IsNullOrWhiteSpace(dict["category_id"].ToString())
                            ? Guid.Parse((string)dict["category_id"])
                            : null,
                        ParentId = dict["parent_id"] != null && dict["parent_id"] is not DBNull && !string.IsNullOrWhiteSpace(dict["parent_id"].ToString())
                            ? Guid.Parse((string)dict["parent_id"])
                            : null,
                        Order = Convert.ToInt32(dict["sort_order"]),
                        Priority = (Models.Priority)Convert.ToInt32(dict["priority"]),
                        IsFavorite = Convert.ToBoolean(dict["is_favorite"]),
                        DueDate = dict["due_date"] != null && dict["due_date"] is not DBNull
                            ? DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(dict["due_date"])).UtcDateTime
                            : null,
                        ReminderDate = dict["reminder_date"] != null && dict["reminder_date"] is not DBNull
                            ? DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(dict["reminder_date"])).UtcDateTime
                            : null,
                        SourceNoteId = dict["source_note_id"] != null && dict["source_note_id"] is not DBNull && !string.IsNullOrWhiteSpace(dict["source_note_id"].ToString())
                            ? Guid.Parse((string)dict["source_note_id"])
                            : null,
                        SourceFilePath = dict["source_file_path"] as string,
                        SourceLineNumber = dict["source_line_number"] is not DBNull ? Convert.ToInt32(dict["source_line_number"]) : null,
                        SourceCharOffset = dict["source_char_offset"] is not DBNull ? Convert.ToInt32(dict["source_char_offset"]) : null,
                        IsOrphaned = Convert.ToBoolean(dict["is_orphaned"]),
                        CreatedDate = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(dict["created_at"])).UtcDateTime,
                        ModifiedDate = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(dict["modified_at"])).UtcDateTime
                    };
                    todos.Add(todo);
                }
                
                await LoadTagsForTodos(connection, todos);
                
                // DIAGNOSTIC: Log what we actually loaded
                foreach (var todo in todos)
                {
                    _logger.Debug($"[TodoRepository] GetAllAsync loaded: \"{todo.Text}\" - CategoryId={todo.CategoryId?.ToString() ?? "NULL"}, IsOrphaned={todo.IsOrphaned}");
                }
                _logger.Info($"[TodoRepository] GetAllAsync returned {todos.Count} todos");
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoRepository] Failed to get all todos");
                return new List<TodoItem>();
            }
        }
        
        public async Task<bool> InsertAsync(TodoItem todo)
        {
            await _dbLock.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                using var transaction = await connection.BeginTransactionAsync();
                
                try
                {
                    // Convert UI model -> Aggregate -> DTO
                    var aggregate = TodoMapper.ToAggregate(todo);
                    var dto = TodoItemDto.FromAggregate(aggregate);
                    
                    var sql = @"
                        INSERT INTO todos (
                            id, text, description, is_completed, completed_date,
                            category_id, parent_id, sort_order, indent_level,
                            priority, is_favorite, due_date, due_time, reminder_date,
                            recurrence_rule, lead_time_days, source_type,
                            source_note_id, source_file_path, source_line_number,
                            source_char_offset, last_seen_in_source, is_orphaned,
                            created_at, modified_at
                        ) VALUES (
                            @Id, @Text, @Description, @IsCompleted, @CompletedDate,
                            @CategoryId, @ParentId, @SortOrder, 0,
                            @Priority, @IsFavorite, @DueDate, NULL, @ReminderDate,
                            NULL, 0, @SourceType,
                            @SourceNoteId, @SourceFilePath, @SourceLineNumber,
                            @SourceCharOffset, NULL, @IsOrphaned,
                            @CreatedAt, @ModifiedAt
                        )";
                    
                    var parameters = new
                    {
                        dto.Id,
                        dto.Text,
                        dto.Description,
                        dto.IsCompleted,
                        dto.CompletedDate,
                        dto.CategoryId,
                        dto.ParentId,
                        dto.SortOrder,
                        dto.Priority,
                        dto.IsFavorite,
                        dto.DueDate,
                        dto.ReminderDate,
                        SourceType = dto.SourceNoteId != null ? "note" : "manual",
                        dto.SourceNoteId,
                        dto.SourceFilePath,
                        dto.SourceLineNumber,
                        dto.SourceCharOffset,
                        dto.IsOrphaned,
                        dto.CreatedAt,
                        dto.ModifiedAt
                    };
                    
                    await connection.ExecuteAsync(sql, parameters, transaction);
                    
                    // Insert tags
                    if (todo.Tags != null && todo.Tags.Any())
                    {
                        await InsertTagsAsync(connection, transaction, todo.Id, todo.Tags);
                    }
                    
                    await transaction.CommitAsync();
                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to insert todo: {todo.Text}");
                return false;
            }
            finally
            {
                _dbLock.Release();
            }
        }
        
        public async Task<bool> UpdateAsync(TodoItem todo)
        {
            await _dbLock.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                using var transaction = await connection.BeginTransactionAsync();
                
                try
                {
                    todo.ModifiedDate = DateTime.UtcNow;
                    
                    // Convert UI model -> Aggregate -> DTO
                    var aggregate = TodoMapper.ToAggregate(todo);
                    var dto = TodoItemDto.FromAggregate(aggregate);
                    
                    var sql = @"
                        UPDATE todos SET
                            text = @Text, description = @Description,
                            is_completed = @IsCompleted, completed_date = @CompletedDate,
                            category_id = @CategoryId, parent_id = @ParentId,
                            sort_order = @SortOrder,
                            priority = @Priority, is_favorite = @IsFavorite,
                            due_date = @DueDate,
                            reminder_date = @ReminderDate,
                            source_type = @SourceType,
                            source_note_id = @SourceNoteId, source_file_path = @SourceFilePath,
                            source_line_number = @SourceLineNumber, source_char_offset = @SourceCharOffset,
                            is_orphaned = @IsOrphaned,
                            modified_at = @ModifiedAt
                        WHERE id = @Id";
                    
                    var parameters = new
                    {
                        dto.Id,
                        dto.Text,
                        dto.Description,
                        dto.IsCompleted,
                        dto.CompletedDate,
                        dto.CategoryId,
                        dto.ParentId,
                        dto.SortOrder,
                        dto.Priority,
                        dto.IsFavorite,
                        dto.DueDate,
                        dto.ReminderDate,
                        SourceType = dto.SourceNoteId != null ? "note" : "manual",
                        dto.SourceNoteId,
                        dto.SourceFilePath,
                        dto.SourceLineNumber,
                        dto.SourceCharOffset,
                        dto.IsOrphaned,
                        dto.ModifiedAt
                    };
                    
                    await connection.ExecuteAsync(sql, parameters, transaction);
                    
                    // Update tags (delete all, re-insert)
                    await connection.ExecuteAsync("DELETE FROM todo_tags WHERE todo_id = @TodoId", 
                        new { TodoId = todo.Id.ToString() }, transaction);
                    
                    if (todo.Tags != null && todo.Tags.Any())
                    {
                        await InsertTagsAsync(connection, transaction, todo.Id, todo.Tags);
                    }
                    
                    await transaction.CommitAsync();
                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to update todo: {todo.Id}");
                return false;
            }
            finally
            {
                _dbLock.Release();
            }
        }
        
        public async Task<bool> DeleteAsync(Guid id)
        {
            await _dbLock.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "DELETE FROM todos WHERE id = @Id";
                var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id.ToString() });
                
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to delete todo: {id}");
                return false;
            }
            finally
            {
                _dbLock.Release();
            }
        }
        
        public async Task<int> BulkInsertAsync(IEnumerable<TodoItem> todos)
        {
            await _dbLock.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                using var transaction = await connection.BeginTransactionAsync();
                
                try
                {
                    var sql = @"
                        INSERT INTO todos (
                            id, text, description, is_completed, completed_date,
                            category_id, parent_id, sort_order, indent_level,
                            priority, is_favorite, due_date, due_time, reminder_date,
                            recurrence_rule, lead_time_days, source_type,
                            source_note_id, source_file_path, source_line_number,
                            source_char_offset, last_seen_in_source, is_orphaned,
                            created_at, modified_at
                        ) VALUES (
                            @Id, @Text, @Description, @IsCompleted, @CompletedDate,
                            @CategoryId, @ParentId, @SortOrder, @IndentLevel,
                            @Priority, @IsFavorite, @DueDate, @DueTime, @ReminderDate,
                            @RecurrenceRule, @LeadTimeDays, @SourceType,
                            @SourceNoteId, @SourceFilePath, @SourceLineNumber,
                            @SourceCharOffset, @LastSeenInSource, @IsOrphaned,
                            @CreatedAt, @ModifiedAt
                        )";
                    
                    var parametersList = todos.Select(MapTodoToParameters).ToList();
                    var rowsAffected = await connection.ExecuteAsync(sql, parametersList, transaction);
                    
                    // Insert tags for all todos
                    foreach (var todo in todos.Where(t => t.Tags != null && t.Tags.Any()))
                    {
                        await InsertTagsAsync(connection, transaction, todo.Id, todo.Tags);
                    }
                    
                    await transaction.CommitAsync();
                    return rowsAffected;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoRepository] Failed to bulk insert todos");
                return 0;
            }
            finally
            {
                _dbLock.Release();
            }
        }
        
        // =========================================================================
        // QUERY OPERATIONS
        // =========================================================================
        
        public async Task<List<TodoItem>> GetByCategoryAsync(Guid categoryId, bool includeCompleted = false)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = includeCompleted
                    ? "SELECT * FROM todos WHERE category_id = @CategoryId ORDER BY sort_order ASC"
                    : "SELECT * FROM todos WHERE category_id = @CategoryId AND is_completed = 0 ORDER BY sort_order ASC";
                
                var todos = (await connection.QueryAsync<TodoItem>(sql, new { CategoryId = categoryId.ToString() })).ToList();
                await LoadTagsForTodos(connection, todos);
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to get todos by category: {categoryId}");
                return new List<TodoItem>();
            }
        }
        
        public async Task<List<TodoItem>> GetBySourceAsync(TodoSource sourceType)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "SELECT * FROM todos WHERE source_type = @SourceType ORDER BY created_at DESC";
                var todos = (await connection.QueryAsync<TodoItem>(sql, new { SourceType = sourceType.ToString().ToLower() })).ToList();
                await LoadTagsForTodos(connection, todos);
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to get todos by source: {sourceType}");
                return new List<TodoItem>();
            }
        }
        
        public async Task<List<TodoItem>> GetByParentAsync(Guid parentId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "SELECT * FROM todos WHERE parent_id = @ParentId ORDER BY sort_order ASC";
                var todos = (await connection.QueryAsync<TodoItem>(sql, new { ParentId = parentId.ToString() })).ToList();
                await LoadTagsForTodos(connection, todos);
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to get todos by parent: {parentId}");
                return new List<TodoItem>();
            }
        }
        
        public async Task<List<TodoItem>> GetRootTodosAsync(Guid? categoryId = null)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = categoryId.HasValue
                    ? "SELECT * FROM todos WHERE parent_id IS NULL AND category_id = @CategoryId ORDER BY sort_order ASC"
                    : "SELECT * FROM todos WHERE parent_id IS NULL ORDER BY sort_order ASC";
                
                var todos = (await connection.QueryAsync<TodoItem>(sql, new { CategoryId = categoryId?.ToString() })).ToList();
                await LoadTagsForTodos(connection, todos);
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoRepository] Failed to get root todos");
                return new List<TodoItem>();
            }
        }
        
        // =========================================================================
        // SMART LISTS
        // =========================================================================
        
        public async Task<List<TodoItem>> GetTodayTodosAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var todos = (await connection.QueryAsync<TodoItem>("SELECT * FROM v_today_todos")).ToList();
                await LoadTagsForTodos(connection, todos);
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoRepository] Failed to get today todos");
                return new List<TodoItem>();
            }
        }
        
        public async Task<List<TodoItem>> GetOverdueTodosAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var todos = (await connection.QueryAsync<TodoItem>("SELECT * FROM v_overdue_todos")).ToList();
                await LoadTagsForTodos(connection, todos);
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoRepository] Failed to get overdue todos");
                return new List<TodoItem>();
            }
        }
        
        public async Task<List<TodoItem>> GetHighPriorityTodosAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var todos = (await connection.QueryAsync<TodoItem>("SELECT * FROM v_high_priority_todos")).ToList();
                await LoadTagsForTodos(connection, todos);
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoRepository] Failed to get high priority todos");
                return new List<TodoItem>();
            }
        }
        
        public async Task<List<TodoItem>> GetFavoriteTodosAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var todos = (await connection.QueryAsync<TodoItem>("SELECT * FROM v_favorite_todos")).ToList();
                await LoadTagsForTodos(connection, todos);
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoRepository] Failed to get favorite todos");
                return new List<TodoItem>();
            }
        }
        
        public async Task<List<TodoItem>> GetRecentlyCompletedAsync(int count = 100)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = $"SELECT * FROM v_recently_completed LIMIT {count}";
                var todos = (await connection.QueryAsync<TodoItem>(sql)).ToList();
                await LoadTagsForTodos(connection, todos);
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoRepository] Failed to get recently completed todos");
                return new List<TodoItem>();
            }
        }
        
        public async Task<List<TodoItem>> GetScheduledTodosAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "SELECT * FROM todos WHERE is_completed = 0 AND due_date IS NOT NULL ORDER BY due_date ASC";
                var todos = (await connection.QueryAsync<TodoItem>(sql)).ToList();
                await LoadTagsForTodos(connection, todos);
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoRepository] Failed to get scheduled todos");
                return new List<TodoItem>();
            }
        }
        
        // =========================================================================
        // SEARCH
        // =========================================================================
        
        public async Task<List<TodoItem>> SearchAsync(string searchTerm)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    SELECT t.* FROM todos t
                    JOIN todos_fts fts ON t.id = fts.id
                    WHERE todos_fts MATCH @SearchTerm
                    ORDER BY rank";
                
                var todos = (await connection.QueryAsync<TodoItem>(sql, new { SearchTerm = searchTerm })).ToList();
                await LoadTagsForTodos(connection, todos);
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to search todos: {searchTerm}");
                return new List<TodoItem>();
            }
        }
        
        public async Task<List<TodoItem>> GetByTagAsync(string tag)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    SELECT t.* FROM todos t
                    JOIN todo_tags tt ON t.id = tt.todo_id
                    WHERE tt.tag = @Tag
                    ORDER BY t.sort_order ASC";
                
                var todos = (await connection.QueryAsync<TodoItem>(sql, new { Tag = tag })).ToList();
                await LoadTagsForTodos(connection, todos);
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to get todos by tag: {tag}");
                return new List<TodoItem>();
            }
        }
        
        public async Task<List<string>> GetAllTagsAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "SELECT DISTINCT tag FROM todo_tags ORDER BY tag ASC";
                return (await connection.QueryAsync<string>(sql)).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoRepository] Failed to get all tags");
                return new List<string>();
            }
        }
        
        // =========================================================================
        // TAG OPERATIONS
        // =========================================================================
        
        public async Task<List<string>> GetTagsForTodoAsync(Guid todoId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "SELECT tag FROM todo_tags WHERE todo_id = @TodoId ORDER BY tag ASC";
                return (await connection.QueryAsync<string>(sql, new { TodoId = todoId.ToString() })).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to get tags for todo: {todoId}");
                return new List<string>();
            }
        }
        
        public async Task<bool> AddTagAsync(Guid todoId, string tag)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "INSERT OR IGNORE INTO todo_tags (todo_id, tag, created_at) VALUES (@TodoId, @Tag, @CreatedAt)";
                await connection.ExecuteAsync(sql, new { TodoId = todoId.ToString(), Tag = tag, CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to add tag: {tag} to todo: {todoId}");
                return false;
            }
        }
        
        public async Task<bool> RemoveTagAsync(Guid todoId, string tag)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "DELETE FROM todo_tags WHERE todo_id = @TodoId AND tag = @Tag";
                await connection.ExecuteAsync(sql, new { TodoId = todoId.ToString(), Tag = tag });
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to remove tag: {tag} from todo: {todoId}");
                return false;
            }
        }
        
        public async Task<bool> SetTagsAsync(Guid todoId, IEnumerable<string> tags)
        {
            await _dbLock.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                using var transaction = await connection.BeginTransactionAsync();
                
                try
                {
                    // Delete existing tags
                    await connection.ExecuteAsync("DELETE FROM todo_tags WHERE todo_id = @TodoId", 
                        new { TodoId = todoId.ToString() }, transaction);
                    
                    // Insert new tags
                    await InsertTagsAsync(connection, transaction, todoId, tags);
                    
                    await transaction.CommitAsync();
                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to set tags for todo: {todoId}");
                return false;
            }
            finally
            {
                _dbLock.Release();
            }
        }
        
        // =========================================================================
        // NOTE-LINKED TODO OPERATIONS
        // =========================================================================
        
        public async Task<List<TodoItem>> GetByNoteIdAsync(Guid noteId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "SELECT * FROM todos WHERE source_note_id = @NoteId ORDER BY source_line_number ASC";
                var todos = (await connection.QueryAsync<TodoItem>(sql, new { NoteId = noteId.ToString() })).ToList();
                await LoadTagsForTodos(connection, todos);
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to get todos by note: {noteId}");
                return new List<TodoItem>();
            }
        }
        
        public async Task<List<TodoItem>> GetOrphanedTodosAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "SELECT * FROM todos WHERE is_orphaned = 1 ORDER BY modified_at DESC";
                var todos = (await connection.QueryAsync<TodoItem>(sql)).ToList();
                await LoadTagsForTodos(connection, todos);
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoRepository] Failed to get orphaned todos");
                return new List<TodoItem>();
            }
        }
        
        public async Task<int> MarkOrphanedByNoteAsync(Guid noteId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "UPDATE todos SET is_orphaned = 1 WHERE source_note_id = @NoteId AND source_type = 'note'";
                return await connection.ExecuteAsync(sql, new { NoteId = noteId.ToString() });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to mark orphaned by note: {noteId}");
                return 0;
            }
        }
        
        public async Task<bool> UpdateLastSeenAsync(Guid todoId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "UPDATE todos SET last_seen_in_source = @Timestamp WHERE id = @Id";
                await connection.ExecuteAsync(sql, new { Id = todoId.ToString(), Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to update last seen: {todoId}");
                return false;
            }
        }
        
        // =========================================================================
        // MAINTENANCE
        // =========================================================================
        
        public async Task<int> DeleteCompletedOlderThanAsync(int days)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var timestamp = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeSeconds();
                var sql = "DELETE FROM todos WHERE is_completed = 1 AND completed_date < @Timestamp";
                
                return await connection.ExecuteAsync(sql, new { Timestamp = timestamp });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to delete completed older than {days} days");
                return 0;
            }
        }
        
        public async Task<int> DeleteOrphanedOlderThanAsync(int days)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var timestamp = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeSeconds();
                var sql = "DELETE FROM todos WHERE is_orphaned = 1 AND modified_at < @Timestamp";
                
                return await connection.ExecuteAsync(sql, new { Timestamp = timestamp });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoRepository] Failed to delete orphaned older than {days} days");
                return 0;
            }
        }
        
        public async Task<DatabaseStats> GetStatsAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var stats = await connection.QuerySingleOrDefaultAsync<DatabaseStats>("SELECT * FROM v_todo_stats");
                
                // Get database file size
                var dbPath = new SqliteConnectionStringBuilder(_connectionString).DataSource;
                if (File.Exists(dbPath))
                {
                    stats.DatabaseSizeBytes = new FileInfo(dbPath).Length;
                }
                
                return stats ?? new DatabaseStats();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoRepository] Failed to get stats");
                return new DatabaseStats();
            }
        }
        
        public async Task<bool> OptimizeAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                await connection.ExecuteAsync("PRAGMA optimize");
                await connection.ExecuteAsync("ANALYZE");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoRepository] Failed to optimize database");
                return false;
            }
        }
        
        public async Task<bool> VacuumAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                await connection.ExecuteAsync("VACUUM");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoRepository] Failed to vacuum database");
                return false;
            }
        }
        
        // =========================================================================
        // REBUILD OPERATIONS
        // =========================================================================
        
        public async Task<int> DeleteAllNoteLinkedTodosAsync()
        {
            await _dbLock.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "DELETE FROM todos WHERE source_type = 'note'";
                return await connection.ExecuteAsync(sql);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoRepository] Failed to delete all note-linked todos");
                return 0;
            }
            finally
            {
                _dbLock.Release();
            }
        }
        
        public async Task<bool> RebuildFromNotesAsync(string notesRootPath)
        {
            // Placeholder for future RTF integration
            // This will scan all RTF files and extract todos
            _logger.Info($"[TodoRepository] Rebuild from notes not yet implemented (RTF integration Phase 5)");
            return false;
        }
        
        // =========================================================================
        // HELPER METHODS
        // =========================================================================
        
        private async Task LoadTagsForTodos(SqliteConnection connection, List<TodoItem> todos)
        {
            if (!todos.Any()) return;
            
            var todoIds = todos.Select(t => t.Id.ToString()).ToList();
            var sql = "SELECT todo_id, tag FROM todo_tags WHERE todo_id IN @TodoIds";
            var tagMappings = await connection.QueryAsync<(string TodoId, string Tag)>(sql, new { TodoIds = todoIds });
            
            var tagsByTodoId = tagMappings.GroupBy(x => x.TodoId)
                .ToDictionary(g => Guid.Parse(g.Key), g => g.Select(x => x.Tag).ToList());
            
            foreach (var todo in todos)
            {
                todo.Tags = tagsByTodoId.ContainsKey(todo.Id) ? tagsByTodoId[todo.Id] : new List<string>();
            }
        }
        
        private async Task InsertTagsAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, Guid todoId, IEnumerable<string> tags)
        {
            if (!tags.Any()) return;
            
            var sql = "INSERT OR IGNORE INTO todo_tags (todo_id, tag, created_at) VALUES (@TodoId, @Tag, @CreatedAt)";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var parameters = tags.Select(tag => new { TodoId = todoId.ToString(), Tag = tag, CreatedAt = timestamp });
            
            await connection.ExecuteAsync(sql, parameters, transaction);
        }
        
        private object MapTodoToParameters(TodoItem todo)
        {
            return new
            {
                Id = todo.Id.ToString(),
                todo.Text,
                Description = todo.Description,  // NULL OK - matches TreeDatabaseRepository pattern
                IsCompleted = todo.IsCompleted ? 1 : 0,
                CompletedDate = (long?)todo.CompletedDate?.ToUnixTimeSeconds(),
                CategoryId = todo.CategoryId?.ToString(),  // NULL OK - no FK constraint
                ParentId = todo.ParentId?.ToString(),  // NULL OK - matches TreeDatabaseRepository pattern
                SortOrder = todo.Order, // maps to sort_order
                IndentLevel = 0, // future feature
                Priority = (int)todo.Priority,
                IsFavorite = todo.IsFavorite ? 1 : 0,
                DueDate = (long?)todo.DueDate?.ToUnixTimeSeconds(),
                DueTime = (int?)null, // future feature
                ReminderDate = (long?)todo.ReminderDate?.ToUnixTimeSeconds(),
                RecurrenceRule = (string?)null, // future feature
                LeadTimeDays = 0, // future feature
                SourceType = todo.SourceNoteId.HasValue ? "note" : "manual",  // Detect source type
                SourceNoteId = todo.SourceNoteId?.ToString(),  // NULL if not from note (not empty string!)
                SourceFilePath = todo.SourceFilePath,  // NULL if not from note
                SourceLineNumber = todo.SourceLineNumber,
                SourceCharOffset = todo.SourceCharOffset,
                LastSeenInSource = (long?)null, // Set during reconciliation
                IsOrphaned = todo.IsOrphaned ? 1 : 0,
                CreatedAt = todo.CreatedDate.ToUnixTimeSeconds(),
                ModifiedAt = todo.ModifiedDate.ToUnixTimeSeconds()
            };
        }
    }
    
    // Extension methods for DateTime conversion
    internal static class DateTimeExtensions
    {
        public static long ToUnixTimeSeconds(this DateTime dateTime)
        {
            return new DateTimeOffset(dateTime.ToUniversalTime()).ToUnixTimeSeconds();
        }
    }
}

