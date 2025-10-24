using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Application.Projections;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Events;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Projections
{
    /// <summary>
    /// Builds todo_view projection from todo events.
    /// Denormalizes category information for fast queries.
    /// Handles todo lifecycle and organization.
    /// Lives in TodoPlugin to avoid circular dependencies.
    /// </summary>
    public class TodoProjection : IProjection
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;
        
        public string Name => "TodoView";
        
        public TodoProjection(string connectionString, IAppLogger logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task HandleAsync(IDomainEvent @event)
        {
            switch (@event)
            {
                case TodoCreatedEvent e:
                    await HandleTodoCreatedAsync(e);
                    break;
                    
                case TodoCompletedEvent e:
                    await HandleTodoCompletedAsync(e);
                    break;
                    
                case TodoUncompletedEvent e:
                    await HandleTodoUncompletedAsync(e);
                    break;
                    
                case TodoTextUpdatedEvent e:
                    await HandleTodoTextUpdatedAsync(e);
                    break;
                    
                case TodoDueDateChangedEvent e:
                    await HandleTodoDueDateChangedAsync(e);
                    break;
                    
                case TodoPriorityChangedEvent e:
                    await HandleTodoPriorityChangedAsync(e);
                    break;
                    
                case TodoFavoritedEvent e:
                    await HandleTodoFavoritedAsync(e);
                    break;
                    
                case TodoUnfavoritedEvent e:
                    await HandleTodoUnfavoritedAsync(e);
                    break;
                    
                case TodoDeletedEvent e:
                    await HandleTodoDeletedAsync(e);
                    break;
            }
        }
        
        public async Task RebuildAsync()
        {
            _logger.Info($"[{Name}] Starting rebuild...");
            
            // Clear projection data
            using var connection = await OpenConnectionAsync();
            await connection.ExecuteAsync("DELETE FROM todo_view");
            
            // Reset checkpoint
            await SetLastProcessedPositionAsync(0);
            
            _logger.Info($"[{Name}] Rebuild complete - ready to process events");
        }
        
        public async Task<long> GetLastProcessedPositionAsync()
        {
            using var connection = await OpenConnectionAsync();
            
            _logger.Debug($"[{Name}] Querying projection_metadata for projection_name = '{this.Name}'");
            
            var checkpoint = await connection.QueryFirstOrDefaultAsync<ProjectionCheckpoint>(
                "SELECT last_processed_position AS LastProcessedPosition FROM projection_metadata WHERE projection_name = @Name",
                new { Name = this.Name });
            
            var position = checkpoint?.LastProcessedPosition ?? 0;
            _logger.Info($"[{Name}] GetLastProcessedPosition returned: {position} (checkpoint exists: {checkpoint != null}, checkpoint value: {checkpoint?.LastProcessedPosition})");
            
            if (checkpoint != null && position == 0)
            {
                _logger.Warning($"[{Name}] ⚠️ Checkpoint exists but position is 0 - potential mapping issue!");
            }
            
            return position;
        }
        
        public async Task SetLastProcessedPositionAsync(long position)
        {
            using var connection = await OpenConnectionAsync();
            
            _logger.Debug($"[{Name}] Saving position to projection_metadata: {position}");
            
            await connection.ExecuteAsync(
                @"INSERT INTO projection_metadata (projection_name, last_processed_position, last_updated_at, status)
                  VALUES (@Name, @Position, @UpdatedAt, 'ready')
                  ON CONFLICT(projection_name) DO UPDATE SET
                    last_processed_position = @Position,
                    last_updated_at = @UpdatedAt",
                new
                {
                    Name = this.Name,
                    Position = position,
                    UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
            
            _logger.Debug($"[{Name}] ✅ Position saved to projection_metadata: {position}");
        }
        
        private async Task<SqliteConnection> OpenConnectionAsync()
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            return connection;
        }
        
        // =============================================================================
        // TODO EVENT HANDLERS
        // =============================================================================
        
        private async Task HandleTodoCreatedAsync(TodoCreatedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            // Get category name for denormalization (if category exists)
            string categoryName = null;
            string categoryPath = null;
            
            _logger.Debug($"[{Name}] Event CategoryId: {e.CategoryId?.ToString() ?? "NULL"}");
            
            if (e.CategoryId.HasValue)
            {
                _logger.Debug($"[{Name}] Looking up category in tree_view: {e.CategoryId.Value}");
                
                var category = await connection.QueryFirstOrDefaultAsync<CategoryInfo>(
                    "SELECT name, display_path as Path FROM tree_view WHERE id = @Id AND node_type = 'category'",
                    new { Id = e.CategoryId.Value.ToString() });
                
                if (category != null)
                {
                    categoryName = category.Name;
                    categoryPath = category.Path;
                    _logger.Debug($"[{Name}] ✅ Category found: {categoryName}");
                }
                else
                {
                    _logger.Warning($"[{Name}] ⚠️ Category NOT found in tree_view: {e.CategoryId.Value} - will store CategoryId anyway");
                }
            }
            
            // Use INSERT OR REPLACE for idempotency (event replay safe)
            await connection.ExecuteAsync(
                @"INSERT OR REPLACE INTO todo_view 
                  (id, text, description, is_completed, completed_date, category_id, category_name, category_path,
                   parent_id, sort_order, priority, is_favorite, due_date, reminder_date,
                   source_type, source_note_id, source_file_path, source_line_number, source_char_offset,
                   is_orphaned, created_at, modified_at)
                  VALUES 
                  (@Id, @Text, @Description, @IsCompleted, @CompletedDate, @CategoryId, @CategoryName, @CategoryPath,
                   @ParentId, @SortOrder, @Priority, @IsFavorite, @DueDate, @ReminderDate,
                   @SourceType, @SourceNoteId, @SourceFilePath, @SourceLineNumber, @SourceCharOffset,
                   @IsOrphaned, @CreatedAt, @ModifiedAt)",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    Text = e.Text,
                    Description = (string)null,
                    IsCompleted = 0,
                    CompletedDate = (long?)null,
                    CategoryId = e.CategoryId?.ToString(),
                    CategoryName = categoryName,
                    CategoryPath = categoryPath,
                    ParentId = (string)null,
                    SortOrder = 0,
                    Priority = 1,
                    IsFavorite = 0,
                    DueDate = (long?)null,
                    ReminderDate = (long?)null,
                    // ✨ FIX: Use event fields directly (complete source tracking)
                    SourceType = e.SourceNoteId.HasValue ? "note" : "manual",
                    SourceNoteId = e.SourceNoteId?.ToString(),
                    SourceFilePath = e.SourceFilePath,
                    SourceLineNumber = e.SourceLineNumber,
                    SourceCharOffset = e.SourceCharOffset,
                    IsOrphaned = 0,
                    CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds(),
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Info($"[{Name}] ✅ WROTE to todo_view: '{e.Text}' | CategoryId: {e.CategoryId?.ToString() ?? "NULL"} | CategoryName: {categoryName ?? "NULL"}");
        }
        
        private async Task HandleTodoCompletedAsync(TodoCompletedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            var rowsAffected = await connection.ExecuteAsync(
                @"UPDATE todo_view 
                  SET is_completed = 1, completed_date = @CompletedDate, modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    CompletedDate = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds(),
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            // ✅ CRITICAL: Force WAL checkpoint to ensure data immediately visible to other connections
            // This solves read-your-own-writes issue where queries can't see uncommitted WAL changes
            try
            {
                await connection.ExecuteAsync("PRAGMA wal_checkpoint(FULL)");
                _logger.Debug($"[{Name}] WAL checkpoint completed for completion update");
                
                // ✅ VERIFICATION: Check if database actually has the updated value
                var verifyValue = await connection.ExecuteScalarAsync<int>(
                    "SELECT is_completed FROM todo_view WHERE id = @Id",
                    new { Id = e.TodoId.Value.ToString() });
                _logger.Info($"[{Name}] 🔍 VERIFICATION: is_completed in DB after checkpoint = {verifyValue}");
            }
            catch (Exception checkpointEx)
            {
                _logger.Warning($"[{Name}] WAL checkpoint failed - eventual consistency will apply: {checkpointEx.Message}");
                // Don't throw - checkpoint failure shouldn't break the operation
            }
            
            _logger.Info($"[{Name}] ✅ Todo completed: {e.TodoId}, rows affected: {rowsAffected}, is_completed set to 1");
            
            if (rowsAffected == 0)
            {
                _logger.Warning($"[{Name}] ⚠️ UPDATE affected 0 rows! Todo {e.TodoId} might not exist in todo_view yet");
            }
        }
        
        private async Task HandleTodoUncompletedAsync(TodoUncompletedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            var rowsAffected = await connection.ExecuteAsync(
                @"UPDATE todo_view 
                  SET is_completed = 0, completed_date = NULL, modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            // Force WAL checkpoint for immediate visibility
            try
            {
                await connection.ExecuteAsync("PRAGMA wal_checkpoint(FULL)");
                _logger.Debug($"[{Name}] WAL checkpoint completed for uncomplete update");
            }
            catch (Exception checkpointEx)
            {
                _logger.Warning($"[{Name}] WAL checkpoint failed: {checkpointEx.Message}");
            }
            
            _logger.Info($"[{Name}] ✅ Todo uncompleted: {e.TodoId}, rows affected: {rowsAffected}");
        }
        
        private async Task HandleTodoTextUpdatedAsync(TodoTextUpdatedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            var rowsAffected = await connection.ExecuteAsync(
                @"UPDATE todo_view 
                  SET text = @Text, modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    Text = e.NewText,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            // Force WAL checkpoint for immediate visibility
            try
            {
                await connection.ExecuteAsync("PRAGMA wal_checkpoint(FULL)");
                _logger.Debug($"[{Name}] WAL checkpoint completed for text update");
            }
            catch (Exception checkpointEx)
            {
                _logger.Warning($"[{Name}] WAL checkpoint failed: {checkpointEx.Message}");
            }
            
            _logger.Info($"[{Name}] ✅ Todo text updated: {e.TodoId}, rows affected: {rowsAffected}");
        }
        
        private async Task HandleTodoDueDateChangedAsync(TodoDueDateChangedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            var rowsAffected = await connection.ExecuteAsync(
                @"UPDATE todo_view 
                  SET due_date = @DueDate, modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    DueDate = e.NewDueDate.HasValue ? new DateTimeOffset(e.NewDueDate.Value).ToUnixTimeSeconds() : (long?)null,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            // Force WAL checkpoint for immediate visibility
            try
            {
                await connection.ExecuteAsync("PRAGMA wal_checkpoint(FULL)");
                _logger.Debug($"[{Name}] WAL checkpoint completed for due date update");
            }
            catch (Exception checkpointEx)
            {
                _logger.Warning($"[{Name}] WAL checkpoint failed: {checkpointEx.Message}");
            }
            
            _logger.Info($"[{Name}] ✅ Todo due date changed: {e.TodoId}, rows affected: {rowsAffected}");
        }
        
        private async Task HandleTodoPriorityChangedAsync(TodoPriorityChangedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            var rowsAffected = await connection.ExecuteAsync(
                @"UPDATE todo_view 
                  SET priority = @Priority, modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    Priority = e.NewPriority,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            // Force WAL checkpoint for immediate visibility
            try
            {
                await connection.ExecuteAsync("PRAGMA wal_checkpoint(FULL)");
                _logger.Debug($"[{Name}] WAL checkpoint completed for priority update");
            }
            catch (Exception checkpointEx)
            {
                _logger.Warning($"[{Name}] WAL checkpoint failed: {checkpointEx.Message}");
            }
            
            _logger.Info($"[{Name}] ✅ Todo priority changed: {e.TodoId} to {e.NewPriority}, rows affected: {rowsAffected}");
        }
        
        private async Task HandleTodoFavoritedAsync(TodoFavoritedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            var rowsAffected = await connection.ExecuteAsync(
                "UPDATE todo_view SET is_favorite = 1, modified_at = @ModifiedAt WHERE id = @Id",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            // Force WAL checkpoint for immediate visibility
            try
            {
                await connection.ExecuteAsync("PRAGMA wal_checkpoint(FULL)");
                _logger.Debug($"[{Name}] WAL checkpoint completed for favorite update");
            }
            catch (Exception checkpointEx)
            {
                _logger.Warning($"[{Name}] WAL checkpoint failed: {checkpointEx.Message}");
            }
            
            _logger.Info($"[{Name}] ✅ Todo favorited: {e.TodoId}, rows affected: {rowsAffected}");
        }
        
        private async Task HandleTodoUnfavoritedAsync(TodoUnfavoritedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            var rowsAffected = await connection.ExecuteAsync(
                "UPDATE todo_view SET is_favorite = 0, modified_at = @ModifiedAt WHERE id = @Id",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            // Force WAL checkpoint for immediate visibility
            try
            {
                await connection.ExecuteAsync("PRAGMA wal_checkpoint(FULL)");
                _logger.Debug($"[{Name}] WAL checkpoint completed for unfavorite update");
            }
            catch (Exception checkpointEx)
            {
                _logger.Warning($"[{Name}] WAL checkpoint failed: {checkpointEx.Message}");
            }
            
            _logger.Info($"[{Name}] ✅ Todo unfavorited: {e.TodoId}, rows affected: {rowsAffected}");
        }
        
        private async Task HandleTodoDeletedAsync(TodoDeletedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            var rowsAffected = await connection.ExecuteAsync(
                "DELETE FROM todo_view WHERE id = @Id",
                new { Id = e.TodoId.Value.ToString() });
            
            // Force WAL checkpoint for immediate visibility
            try
            {
                await connection.ExecuteAsync("PRAGMA wal_checkpoint(FULL)");
                _logger.Debug($"[{Name}] WAL checkpoint completed for delete");
            }
            catch (Exception checkpointEx)
            {
                _logger.Warning($"[{Name}] WAL checkpoint failed: {checkpointEx.Message}");
            }
            
            _logger.Info($"[{Name}] ✅ Todo deleted: {e.TodoId}, rows affected: {rowsAffected}");
        }
        
        // Helper classes
        private class ProjectionCheckpoint
        {
            public long LastProcessedPosition { get; set; }
        }
        
        private class CategoryInfo
        {
            public string Name { get; set; }
            public string Path { get; set; }
        }
    }
}

