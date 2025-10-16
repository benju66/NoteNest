using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence
{
    /// <summary>
    /// Repository for managing todo tags locally in todos.db.
    /// Respects database isolation - no cross-database operations.
    /// </summary>
    public class TodoTagRepository : ITodoTagRepository
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;

        public TodoTagRepository(string connectionString, IAppLogger logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<TodoTag>> GetByTodoIdAsync(Guid todoId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"SELECT 
                    todo_id as TodoId,
                    display_name as Tag,
                    CASE WHEN source != 'manual' THEN 1 ELSE 0 END as IsAuto,
                    created_at as CreatedAt
                FROM todo_tags
                WHERE todo_id = @TodoId
                ORDER BY display_name";

                var tags = await connection.QueryAsync<TodoTag>(sql, new { TodoId = todoId.ToString() });
                var todoTags = tags.ToList();

                _logger.Debug($"[TodoTagRepository] Loaded {todoTags.Count} tags for todo {todoId}");
                return todoTags;
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] Failed to get tags for todo {todoId}", ex);
                return new List<TodoTag>();
            }
        }

        public async Task<List<TodoTag>> GetAutoTagsAsync(Guid todoId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"SELECT 
                    todo_id as TodoId,
                    display_name as Tag,
                    1 as IsAuto,
                    created_at as CreatedAt
                FROM todo_tags
                WHERE todo_id = @TodoId AND source != 'manual'
                ORDER BY display_name";

                var tags = await connection.QueryAsync<TodoTag>(sql, new { TodoId = todoId.ToString() });
                return tags.ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] Failed to get auto tags for todo {todoId}", ex);
                return new List<TodoTag>();
            }
        }

        public async Task<List<TodoTag>> GetManualTagsAsync(Guid todoId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"SELECT 
                    todo_id as TodoId,
                    display_name as Tag,
                    0 as IsAuto,
                    created_at as CreatedAt
                FROM todo_tags
                WHERE todo_id = @TodoId AND source = 'manual'
                ORDER BY display_name";

                var tags = await connection.QueryAsync<TodoTag>(sql, new { TodoId = todoId.ToString() });
                return tags.ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] Failed to get manual tags for todo {todoId}", ex);
                return new List<TodoTag>();
            }
        }

        public async Task<bool> ExistsAsync(Guid todoId, string tagName)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var count = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM todo_tags WHERE todo_id = @TodoId AND tag = @Tag",
                    new { TodoId = todoId.ToString(), Tag = tagName.ToLower().Trim() }
                );

                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] Failed to check if tag '{tagName}' exists for todo {todoId}", ex);
                return false;
            }
        }

        public async Task AddAsync(TodoTag tag)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"INSERT OR REPLACE INTO todo_tags 
                    (todo_id, tag, display_name, source, created_at)
                    VALUES (@TodoId, @Tag, @DisplayName, @Source, @CreatedAt)";

                await connection.ExecuteAsync(sql, new
                {
                    TodoId = tag.TodoId.ToString(),
                    Tag = tag.Tag.ToLower().Trim(),
                    DisplayName = tag.Tag.Trim(),
                    Source = tag.IsAuto ? "auto-inherit" : "manual",
                    CreatedAt = tag.CreatedAt
                });

                _logger.Debug($"[TodoTagRepository] Added tag '{tag.Tag}' to todo {tag.TodoId} (is_auto={tag.IsAuto})");
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] Failed to add tag '{tag.Tag}' to todo {tag.TodoId}", ex);
                throw;
            }
        }

        public async Task DeleteAsync(Guid todoId, string tagName)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                await connection.ExecuteAsync(
                    "DELETE FROM todo_tags WHERE todo_id = @TodoId AND tag = @Tag",
                    new { TodoId = todoId.ToString(), Tag = tagName.ToLower().Trim() }
                );

                _logger.Debug($"[TodoTagRepository] Deleted tag '{tagName}' from todo {todoId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] Failed to delete tag '{tagName}' from todo {todoId}", ex);
                throw;
            }
        }

        public async Task DeleteAutoTagsAsync(Guid todoId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var deletedCount = await connection.ExecuteAsync(
                    "DELETE FROM todo_tags WHERE todo_id = @TodoId AND source != 'manual'",
                    new { TodoId = todoId.ToString() }
                );

                _logger.Debug($"[TodoTagRepository] Deleted {deletedCount} auto-tags from todo {todoId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] Failed to delete auto tags from todo {todoId}", ex);
                throw;
            }
        }

        public async Task DeleteAllAsync(Guid todoId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                await connection.ExecuteAsync(
                    "DELETE FROM todo_tags WHERE todo_id = @TodoId",
                    new { TodoId = todoId.ToString() }
                );

                _logger.Debug($"[TodoTagRepository] Deleted all tags from todo {todoId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] Failed to delete all tags from todo {todoId}", ex);
                throw;
            }
        }
    }
}