using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Application.Queries;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Queries
{
    /// <summary>
    /// Query service for todo projection.
    /// Queries projections.db todo_view table with optimized SQL.
    /// </summary>
    public class TodoQueryService : ITodoQueryService
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;

        public TodoQueryService(string projectionsConnectionString, IAppLogger logger)
        {
            _connectionString = projectionsConnectionString ?? throw new ArgumentNullException(nameof(projectionsConnectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<TodoItem> GetByIdAsync(Guid id)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var dto = await connection.QueryFirstOrDefaultAsync<TodoDto>(
                    "SELECT * FROM todo_view WHERE id = @Id",
                    new { Id = id.ToString() });

                return dto != null ? MapToTodoItem(dto) : null;
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

                string sql;
                object param;

                if (categoryId.HasValue)
                {
                    sql = @"SELECT * FROM todo_view 
                           WHERE category_id = @CategoryId
                           ORDER BY sort_order, created_at";
                    param = new { CategoryId = categoryId.Value.ToString() };
                }
                else
                {
                    sql = @"SELECT * FROM todo_view 
                           WHERE category_id IS NULL
                           ORDER BY sort_order, created_at";
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
                var todayEnd = DateTimeOffset.UtcNow.Date.AddDays(1).AddSeconds(-1).ToUnixTimeSeconds();

                var sql = type switch
                {
                    Models.SmartListType.Today => 
                        $"SELECT * FROM todo_view WHERE is_completed = 0 AND (due_date IS NULL OR due_date <= {todayEnd}) ORDER BY priority DESC, due_date",
                    
                    Models.SmartListType.Overdue => 
                        $"SELECT * FROM todo_view WHERE is_completed = 0 AND due_date < {now} ORDER BY due_date",
                    
                    Models.SmartListType.Upcoming => 
                        $"SELECT * FROM todo_view WHERE is_completed = 0 AND due_date > {todayEnd} ORDER BY due_date LIMIT 50",
                    
                    Models.SmartListType.Completed => 
                        "SELECT * FROM todo_view WHERE is_completed = 1 ORDER BY completed_date DESC LIMIT 100",
                    
                    Models.SmartListType.Favorite => 
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

                var sql = includeCompleted
                    ? "SELECT * FROM todo_view ORDER BY is_completed, sort_order, created_at"
                    : "SELECT * FROM todo_view WHERE is_completed = 0 ORDER BY sort_order, created_at";

                var dtos = await connection.QueryAsync<TodoDto>(sql);
                return dtos.Select(MapToTodoItem).Where(t => t != null).ToList();
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
                    CategoryName = dto.CategoryName,
                    ParentId = string.IsNullOrEmpty(dto.ParentId) ? null : Guid.Parse(dto.ParentId),
                    SortOrder = dto.SortOrder,
                    Priority = dto.Priority,
                    IsFavorite = dto.IsFavorite == 1,
                    DueDate = dto.DueDate.HasValue 
                        ? DateTimeOffset.FromUnixTimeSeconds(dto.DueDate.Value).DateTime 
                        : (DateTime?)null,
                    ReminderDate = dto.ReminderDate.HasValue 
                        ? DateTimeOffset.FromUnixTimeSeconds(dto.ReminderDate.Value).DateTime 
                        : (DateTime?)null,
                    SourceType = dto.SourceType,
                    SourceNoteId = string.IsNullOrEmpty(dto.SourceNoteId) ? null : Guid.Parse(dto.SourceNoteId),
                    SourceFilePath = dto.SourceFilePath,
                    IsOrphaned = dto.IsOrphaned == 1,
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(dto.CreatedAt).DateTime,
                    ModifiedAt = DateTimeOffset.FromUnixTimeSeconds(dto.ModifiedAt).DateTime,
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

