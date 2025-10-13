using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence
{
    /// <summary>
    /// Clean DTO-based TodoRepository following DDD pattern.
    /// Flow: Database → TodoItemDto → TodoAggregate → TodoItem (UI)
    /// 
    /// This is the scorched earth refactor - clean, maintainable, enterprise-grade.
    /// </summary>
    public class TodoRepository : ITodoRepository
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;
        
        public TodoRepository(string connectionString, IAppLogger logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        // =========================================================================
        // CORE OPERATIONS
        // =========================================================================
        
        public async Task<List<TodoItem>> GetAllAsync(bool includeCompleted = true)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = includeCompleted 
                    ? "SELECT * FROM todos ORDER BY sort_order ASC"
                    : "SELECT * FROM todos WHERE is_completed = 0 ORDER BY sort_order ASC";
                    
                // MANUAL MAPPING: Query dynamic objects to avoid Dapper TEXT → Guid? issues
                var rawResults = (await connection.QueryAsync(sql)).ToList();
                var todos = new List<TodoItem>();
                
                foreach (var row in rawResults)
                {
                    try
                    {
                        var dict = (IDictionary<string, object>)row;
                        
                        // Parse row to DTO (manual but reliable)
                        var dto = ParseRowToDto(dict);
                        
                        // Load tags
                        var tags = await GetTagsForTodoAsync(connection, Guid.Parse(dto.Id));
                        
                        // Convert DTO → Aggregate → TodoItem (clean DDD flow)
                        var aggregate = dto.ToAggregate(tags);
                        var todoItem = TodoItem.FromAggregate(aggregate);
                        todos.Add(todoItem);
                        
                        // DIAGNOSTIC: Log what was loaded
                        _logger.Debug($"[TodoRepository] Loaded: \"{todoItem.Text}\" - CategoryId={todoItem.CategoryId?.ToString() ?? "NULL"}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"[TodoRepository] Failed to convert todo: {ex.Message}. Skipping.");
                    }
                }
                
                _logger.Info($"[TodoRepository] ✅ Loaded {todos.Count} todos (includeCompleted={includeCompleted})");
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoRepository] ❌ GetAllAsync failed: {ex.Message}");
                throw;
            }
        }
        
        public async Task<TodoItem?> GetByIdAsync(Guid id)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "SELECT * FROM todos WHERE id = @Id";
                
                // MANUAL MAPPING: Use dynamic query to avoid Dapper TEXT → Guid? issues
                var row = await connection.QuerySingleOrDefaultAsync(sql, new { Id = id.ToString() });
                
                if (row == null)
                {
                    _logger.Debug($"[TodoRepository] Todo {id} not found");
                    return null;
                }
                
                var dict = (IDictionary<string, object>)row;
                var dto = ParseRowToDto(dict);
                
                var tags = await GetTagsForTodoAsync(connection, id);
                var aggregate = dto.ToAggregate(tags);
                return TodoItem.FromAggregate(aggregate);
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoRepository] ❌ GetByIdAsync({id}) failed: {ex.Message}");
                throw;
            }
        }
        
        public async Task<bool> InsertAsync(TodoItem todo)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                using var transaction = connection.BeginTransaction();
                
                try
                {
                    // Convert TodoItem → Aggregate → DTO
                    var aggregate = todo.ToAggregate();
                    var dto = TodoItemDto.FromAggregate(aggregate);
                    
                    var sql = @"
                        INSERT INTO todos (
                            id, text, description, is_completed, completed_date, due_date, 
                            reminder_date, priority, is_favorite, sort_order, created_at, 
                            modified_at, category_id, parent_id, source_type, source_note_id, 
                            source_file_path, source_line_number, source_char_offset, is_orphaned
                        ) VALUES (
                            @Id, @Text, @Description, @IsCompleted, @CompletedDate, @DueDate,
                            @ReminderDate, @Priority, @IsFavorite, @SortOrder, @CreatedAt,
                            @ModifiedAt, @CategoryId, @ParentId, @SourceType, @SourceNoteId,
                            @SourceFilePath, @SourceLineNumber, @SourceCharOffset, @IsOrphaned
                        )";
                    
                    var rowsAffected = await connection.ExecuteAsync(sql, dto, transaction);
                    
                    // Save tags
                    await SaveTagsAsync(connection, todo.Id, todo.Tags, transaction);
                    
                    transaction.Commit();
                    
                    _logger.Info($"[TodoRepository] ✅ Inserted todo: \"{todo.Text}\"");
                    return rowsAffected > 0;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoRepository] ❌ InsertAsync failed: {ex.Message}");
                throw;
            }
        }
        
        public async Task<bool> UpdateAsync(TodoItem todo)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                using var transaction = connection.BeginTransaction();
                
                try
                {
                    // Convert TodoItem → Aggregate → DTO
                    var aggregate = todo.ToAggregate();
                    var dto = TodoItemDto.FromAggregate(aggregate);
                    
                    var sql = @"
                        UPDATE todos SET
                            text = @Text,
                            description = @Description,
                            is_completed = @IsCompleted,
                            completed_date = @CompletedDate,
                            due_date = @DueDate,
                            reminder_date = @ReminderDate,
                            priority = @Priority,
                            is_favorite = @IsFavorite,
                            sort_order = @SortOrder,
                            modified_at = @ModifiedAt,
                            category_id = @CategoryId,
                            parent_id = @ParentId,
                            source_type = @SourceType,
                            source_note_id = @SourceNoteId,
                            source_file_path = @SourceFilePath,
                            source_line_number = @SourceLineNumber,
                            source_char_offset = @SourceCharOffset,
                            is_orphaned = @IsOrphaned
                        WHERE id = @Id";
                    
                    var rowsAffected = await connection.ExecuteAsync(sql, dto, transaction);
                    
                    // Update tags (delete all + reinsert)
                    await DeleteTagsAsync(connection, todo.Id, transaction);
                    await SaveTagsAsync(connection, todo.Id, todo.Tags, transaction);
                    
                    transaction.Commit();
                    
                    _logger.Info($"[TodoRepository] ✅ Updated todo: \"{todo.Text}\"");
                    return rowsAffected > 0;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoRepository] ❌ UpdateAsync failed: {ex.Message}");
                throw;
            }
        }
        
        public async Task<bool> DeleteAsync(Guid id)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                using var transaction = connection.BeginTransaction();
                
                try
                {
                    // Delete tags first (foreign key)
                    await DeleteTagsAsync(connection, id, transaction);
                    
                    // Delete todo
                    var sql = "DELETE FROM todos WHERE id = @Id";
                    var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id.ToString() }, transaction);
                    
                    transaction.Commit();
                    
                    var deleted = rowsAffected > 0;
                    _logger.Info($"[TodoRepository] ✅ Deleted todo {id}: {deleted}");
                    return deleted;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoRepository] ❌ DeleteAsync({id}) failed: {ex.Message}");
                throw;
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
                
                // MANUAL MAPPING: Query dynamic objects
                var rawResults = (await connection.QueryAsync(sql, new { CategoryId = categoryId.ToString() })).ToList();
                var todos = new List<TodoItem>();
                
                foreach (var row in rawResults)
        {
            try
            {
                        var dict = (IDictionary<string, object>)row;
                        var dto = ParseRowToDto(dict);
                        
                        var tags = await GetTagsForTodoAsync(connection, Guid.Parse(dto.Id));
                        var aggregate = dto.ToAggregate(tags);
                        todos.Add(TodoItem.FromAggregate(aggregate));
            }
            catch (Exception ex)
            {
                        _logger.Warning($"[TodoRepository] Failed to convert todo: {ex.Message}");
                    }
                }
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoRepository] ❌ GetByCategoryAsync({categoryId}) failed: {ex.Message}");
                throw;
            }
        }
        
        public async Task<List<TodoItem>> GetRecentlyCompletedAsync(int count = 10)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    SELECT * FROM todos 
                    WHERE is_completed = 1 AND completed_date IS NOT NULL
                    ORDER BY completed_date DESC 
                    LIMIT @Count";
                
                // MANUAL MAPPING: Query dynamic objects
                var rawResults = (await connection.QueryAsync(sql, new { Count = count })).ToList();
                var todos = new List<TodoItem>();
                
                foreach (var row in rawResults)
        {
            try
            {
                        var dict = (IDictionary<string, object>)row;
                        var dto = ParseRowToDto(dict);
                        
                        var tags = await GetTagsForTodoAsync(connection, Guid.Parse(dto.Id));
                        var aggregate = dto.ToAggregate(tags);
                        todos.Add(TodoItem.FromAggregate(aggregate));
            }
            catch (Exception ex)
            {
                        _logger.Warning($"[TodoRepository] Failed to convert todo: {ex.Message}");
                    }
                }
                
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoRepository] ❌ GetRecentlyCompletedAsync failed: {ex.Message}");
                throw;
            }
        }
        
        // =========================================================================
        // NOTE SYNC OPERATIONS
        // =========================================================================
        
        public async Task<List<TodoItem>> GetByNoteIdAsync(Guid noteId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "SELECT * FROM todos WHERE source_note_id = @NoteId ORDER BY source_line_number ASC";
                
                // MANUAL MAPPING: Query dynamic objects
                var rawResults = (await connection.QueryAsync(sql, new { NoteId = noteId.ToString() })).ToList();
                var todos = new List<TodoItem>();
                
                foreach (var row in rawResults)
        {
            try
            {
                        var dict = (IDictionary<string, object>)row;
                        var dto = ParseRowToDto(dict);
                        
                        var tags = await GetTagsForTodoAsync(connection, Guid.Parse(dto.Id));
                        var aggregate = dto.ToAggregate(tags);
                        todos.Add(TodoItem.FromAggregate(aggregate));
            }
            catch (Exception ex)
            {
                        _logger.Warning($"[TodoRepository] Failed to convert todo: {ex.Message}");
                    }
                }
                
                _logger.Debug($"[TodoRepository] Found {todos.Count} todos for note {noteId}");
                return todos;
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoRepository] ❌ GetByNoteIdAsync({noteId}) failed: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Update last_seen timestamp for a todo (for sync tracking).
        /// OPTIONAL: Gracefully handles if column doesn't exist (for backward compatibility).
        /// </summary>
        public async Task UpdateLastSeenAsync(Guid todoId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // Check if last_seen column exists first
                var checkSql = "SELECT COUNT(*) FROM pragma_table_info('todos') WHERE name='last_seen'";
                var columnExists = await connection.ExecuteScalarAsync<int>(checkSql) > 0;
                
                if (!columnExists)
                {
                    _logger.Debug($"[TodoRepository] last_seen column doesn't exist - skipping update (backward compat)");
                    return;  // Gracefully skip if column doesn't exist
                }
                
                var sql = "UPDATE todos SET last_seen = @LastSeen WHERE id = @Id";
                await connection.ExecuteAsync(sql, new 
                { 
                    Id = todoId.ToString(),
                    LastSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
                
                _logger.Debug($"[TodoRepository] Updated last_seen for todo {todoId}");
            }
            catch (Exception ex)
            {
                // Don't propagate - this is optional tracking
                _logger.Warning($"[TodoRepository] ⚠️ UpdateLastSeenAsync({todoId}) failed (non-critical): {ex.Message}");
            }
        }
        
        /// <summary>
        /// Mark all todos from a note as orphaned (when note is deleted).
        /// </summary>
        public async Task<int> MarkOrphanedByNoteAsync(Guid noteId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "UPDATE todos SET is_orphaned = 1 WHERE source_note_id = @NoteId AND is_orphaned = 0";
                var rowsAffected = await connection.ExecuteAsync(sql, new { NoteId = noteId.ToString() });
                
                _logger.Info($"[TodoRepository] ✅ Marked {rowsAffected} todos as orphaned for note {noteId}");
                return rowsAffected;
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoRepository] ❌ MarkOrphanedByNoteAsync({noteId}) failed: {ex.Message}");
                throw;
            }
        }
        
        // =========================================================================
        // CLEANUP OPERATIONS
        // =========================================================================
        
        public async Task UpdateCategoryForTodosAsync(Guid oldCategoryId, Guid? newCategoryId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "UPDATE todos SET category_id = @NewCategoryId WHERE category_id = @OldCategoryId";
                var rowsAffected = await connection.ExecuteAsync(sql, new 
                { 
                    NewCategoryId = newCategoryId?.ToString(),
                    OldCategoryId = oldCategoryId.ToString()
                });
                
                _logger.Info($"[TodoRepository] ✅ Updated category for {rowsAffected} todos");
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoRepository] ❌ UpdateCategoryForTodosAsync failed: {ex.Message}");
                throw;
            }
        }
        
        // =========================================================================
        // MANUAL MAPPING HELPERS (Handles SQLite TEXT → Guid? issues)
        // =========================================================================
        
        /// <summary>
        /// Parse database row to TodoItemDto with manual field mapping.
        /// This handles SQLite TEXT → Guid? conversion issues that Dapper can't handle reliably.
        /// </summary>
        private TodoItemDto ParseRowToDto(IDictionary<string, object> row)
        {
            return new TodoItemDto
            {
                Id = row["id"]?.ToString(),
                Text = row["text"]?.ToString(),
                Description = row["description"]?.ToString(),
                IsCompleted = Convert.ToInt32(row["is_completed"]),
                CompletedDate = row["completed_date"] != null && row["completed_date"] is not DBNull 
                    ? Convert.ToInt64(row["completed_date"]) 
                    : (long?)null,
                DueDate = row["due_date"] != null && row["due_date"] is not DBNull 
                    ? Convert.ToInt64(row["due_date"]) 
                    : (long?)null,
                ReminderDate = row["reminder_date"] != null && row["reminder_date"] is not DBNull 
                    ? Convert.ToInt64(row["reminder_date"]) 
                    : (long?)null,
                Priority = Convert.ToInt32(row["priority"]),
                IsFavorite = Convert.ToInt32(row["is_favorite"]),
                SortOrder = Convert.ToInt32(row["sort_order"]),
                CreatedAt = Convert.ToInt64(row["created_at"]),
                ModifiedAt = Convert.ToInt64(row["modified_at"]),
                CategoryId = ParseGuidColumn(row["category_id"]),
                ParentId = ParseGuidColumn(row["parent_id"]),
                SourceType = row["source_type"]?.ToString(),
                SourceNoteId = ParseGuidColumn(row["source_note_id"]),
                SourceFilePath = row["source_file_path"]?.ToString(),
                SourceLineNumber = row["source_line_number"] != null && row["source_line_number"] is not DBNull 
                    ? Convert.ToInt32(row["source_line_number"]) 
                    : (int?)null,
                SourceCharOffset = row["source_char_offset"] != null && row["source_char_offset"] is not DBNull 
                    ? Convert.ToInt32(row["source_char_offset"]) 
                    : (int?)null,
                IsOrphaned = Convert.ToInt32(row["is_orphaned"])
            };
        }
        
        /// <summary>
        /// Parse SQLite TEXT column to Guid string (nullable).
        /// Handles NULL, DBNull, empty string, and whitespace.
        /// </summary>
        private string ParseGuidColumn(object value)
        {
            if (value == null || value is DBNull)
                return null;
            
            var stringValue = value.ToString();
            if (string.IsNullOrWhiteSpace(stringValue))
                return null;
            
            return stringValue;
        }
        
        // =========================================================================
        // TAG HELPERS (Private)
        // =========================================================================
        
        private async Task<List<string>> GetTagsForTodoAsync(SqliteConnection connection, Guid todoId)
        {
            var sql = "SELECT tag FROM todo_tags WHERE todo_id = @TodoId";
            var tags = await connection.QueryAsync<string>(sql, new { TodoId = todoId.ToString() });
            return tags.ToList();
        }
        
        private async Task SaveTagsAsync(SqliteConnection connection, Guid todoId, List<string> tags, SqliteTransaction transaction)
        {
            if (tags == null || !tags.Any())
                return;
            
            var sql = "INSERT INTO todo_tags (todo_id, tag) VALUES (@TodoId, @Tag)";
            foreach (var tag in tags)
            {
                await connection.ExecuteAsync(sql, new { TodoId = todoId.ToString(), Tag = tag }, transaction);
            }
        }
        
        private async Task DeleteTagsAsync(SqliteConnection connection, Guid todoId, SqliteTransaction transaction)
        {
            var sql = "DELETE FROM todo_tags WHERE todo_id = @TodoId";
            await connection.ExecuteAsync(sql, new { TodoId = todoId.ToString() }, transaction);
        }
    }
}

