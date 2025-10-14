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
    /// Repository for managing todo tags.
    /// Follows TodoRepository patterns for consistency.
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

                var sql = @"
                    SELECT todo_id as TodoId, tag as Tag, is_auto as IsAuto, created_at as CreatedAt
                    FROM todo_tags
                    WHERE todo_id = @TodoId
                    ORDER BY is_auto DESC, tag ASC";

                var results = await connection.QueryAsync<TodoTag>(sql, new { TodoId = todoId.ToString() });
                var tags = results.ToList();

                _logger.Debug($"[TodoTagRepository] Loaded {tags.Count} tags for todo {todoId}");
                return tags;
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] GetByTodoIdAsync failed: {ex.Message}");
                throw;
            }
        }

        public async Task<List<TodoTag>> GetAutoTagsAsync(Guid todoId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT todo_id as TodoId, tag as Tag, is_auto as IsAuto, created_at as CreatedAt
                    FROM todo_tags
                    WHERE todo_id = @TodoId AND is_auto = 1
                    ORDER BY tag ASC";

                var results = await connection.QueryAsync<TodoTag>(sql, new { TodoId = todoId.ToString() });
                return results.ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] GetAutoTagsAsync failed: {ex.Message}");
                throw;
            }
        }

        public async Task<List<TodoTag>> GetManualTagsAsync(Guid todoId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT todo_id as TodoId, tag as Tag, is_auto as IsAuto, created_at as CreatedAt
                    FROM todo_tags
                    WHERE todo_id = @TodoId AND is_auto = 0
                    ORDER BY tag ASC";

                var results = await connection.QueryAsync<TodoTag>(sql, new { TodoId = todoId.ToString() });
                return results.ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] GetManualTagsAsync failed: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> ExistsAsync(Guid todoId, string tagName)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT COUNT(*)
                    FROM todo_tags
                    WHERE todo_id = @TodoId AND tag = @Tag";

                var count = await connection.ExecuteScalarAsync<int>(sql, new 
                { 
                    TodoId = todoId.ToString(), 
                    Tag = tagName 
                });

                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] ExistsAsync failed: {ex.Message}");
                throw;
            }
        }

        public async Task AddAsync(TodoTag tag)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    INSERT INTO todo_tags (todo_id, tag, is_auto, created_at)
                    VALUES (@TodoId, @Tag, @IsAuto, @CreatedAt)";

                await connection.ExecuteAsync(sql, new
                {
                    TodoId = tag.TodoId.ToString(),
                    Tag = tag.Tag,
                    IsAuto = tag.IsAuto ? 1 : 0,
                    CreatedAt = ((DateTimeOffset)tag.CreatedAt).ToUnixTimeSeconds()
                });

                _logger.Debug($"[TodoTagRepository] Added tag '{tag.Tag}' to todo {tag.TodoId} (is_auto={tag.IsAuto})");
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] AddAsync failed: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteAsync(Guid todoId, string tagName)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    DELETE FROM todo_tags
                    WHERE todo_id = @TodoId AND tag = @Tag";

                var rowsAffected = await connection.ExecuteAsync(sql, new
                {
                    TodoId = todoId.ToString(),
                    Tag = tagName
                });

                _logger.Debug($"[TodoTagRepository] Deleted tag '{tagName}' from todo {todoId} ({rowsAffected} rows)");
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] DeleteAsync failed: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteAutoTagsAsync(Guid todoId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    DELETE FROM todo_tags
                    WHERE todo_id = @TodoId AND is_auto = 1";

                var rowsAffected = await connection.ExecuteAsync(sql, new
                {
                    TodoId = todoId.ToString()
                });

                _logger.Debug($"[TodoTagRepository] Deleted {rowsAffected} auto-tags from todo {todoId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] DeleteAutoTagsAsync failed: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteAllAsync(Guid todoId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    DELETE FROM todo_tags
                    WHERE todo_id = @TodoId";

                var rowsAffected = await connection.ExecuteAsync(sql, new
                {
                    TodoId = todoId.ToString()
                });

                _logger.Debug($"[TodoTagRepository] Deleted {rowsAffected} tags from todo {todoId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"[TodoTagRepository] DeleteAllAsync failed: {ex.Message}");
                throw;
            }
        }
    }
}

