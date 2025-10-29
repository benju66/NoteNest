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
            
            // ✅ CRITICAL: Ensure writes are durable (matches BaseProjection pattern)
            // This ensures ALL writes wait for disk sync before returning success
            // Without this, changes can be lost in WAL file on app close
            await connection.ExecuteAsync("PRAGMA synchronous = FULL");
            
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
            
            // ✅ FIX: Use INSERT OR REPLACE instead of UPDATE (proven to persist reliably)
            // Pattern matches HandleTodoCreatedAsync() which works correctly
            // First, get current row to preserve all fields
            var current = await connection.QueryFirstOrDefaultAsync(
                "SELECT * FROM todo_view WHERE id = @Id",
                new { Id = e.TodoId.Value.ToString() });
            
            if (current == null)
            {
                _logger.Warning($"[{Name}] ⚠️ Todo {e.TodoId} not found in todo_view for completion");
                return;
            }
            
            // INSERT OR REPLACE entire row with is_completed = 1
            await connection.ExecuteAsync(
                @"INSERT OR REPLACE INTO todo_view 
                  (id, text, description, is_completed, completed_date, category_id, category_name, category_path,
                   parent_id, sort_order, priority, is_favorite, due_date, reminder_date,
                   source_type, source_note_id, source_file_path, source_line_number, source_char_offset,
                   is_orphaned, created_at, modified_at)
                  VALUES 
                  (@Id, @Text, @Description, 1, @CompletedDate, @CategoryId, @CategoryName, @CategoryPath,
                   @ParentId, @SortOrder, @Priority, @IsFavorite, @DueDate, @ReminderDate,
                   @SourceType, @SourceNoteId, @SourceFilePath, @SourceLineNumber, @SourceCharOffset,
                   @IsOrphaned, @CreatedAt, @ModifiedAt)",
                new
                {
                    Id = current.id,
                    Text = current.text,
                    Description = current.description,
                    CompletedDate = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds(),
                    CategoryId = current.category_id,
                    CategoryName = current.category_name,
                    CategoryPath = current.category_path,
                    ParentId = current.parent_id,
                    SortOrder = current.sort_order,
                    Priority = current.priority,
                    IsFavorite = current.is_favorite,
                    DueDate = current.due_date,
                    ReminderDate = current.reminder_date,
                    SourceType = current.source_type,
                    SourceNoteId = current.source_note_id,
                    SourceFilePath = current.source_file_path,
                    SourceLineNumber = current.source_line_number,
                    SourceCharOffset = current.source_char_offset,
                    IsOrphaned = current.is_orphaned,
                    CreatedAt = current.created_at,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            // ✅ VERIFICATION: Confirm the write succeeded
            try
            {
                var verifyValue = await connection.ExecuteScalarAsync<int>(
                    "SELECT is_completed FROM todo_view WHERE id = @Id",
                    new { Id = e.TodoId.Value.ToString() });
                _logger.Info($"[{Name}] 🔍 VERIFICATION: is_completed in DB after INSERT OR REPLACE = {verifyValue}");
                
                if (verifyValue != 1)
                {
                    _logger.Error($"[{Name}] ❌ CRITICAL: Verification failed! Expected 1, got {verifyValue}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[{Name}] Failed during verification");
            }
            
            _logger.Info($"[{Name}] ✅ Todo completed using INSERT OR REPLACE: {e.TodoId}, is_completed set to 1");
        }
        
        private async Task HandleTodoUncompletedAsync(TodoUncompletedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            // ✅ FIX: Use INSERT OR REPLACE instead of UPDATE
            var current = await connection.QueryFirstOrDefaultAsync(
                "SELECT * FROM todo_view WHERE id = @Id",
                new { Id = e.TodoId.Value.ToString() });
            
            if (current == null)
            {
                _logger.Warning($"[{Name}] ⚠️ Todo {e.TodoId} not found in todo_view for uncompletion");
                return;
            }
            
            // INSERT OR REPLACE entire row with is_completed = 0
            await connection.ExecuteAsync(
                @"INSERT OR REPLACE INTO todo_view 
                  (id, text, description, is_completed, completed_date, category_id, category_name, category_path,
                   parent_id, sort_order, priority, is_favorite, due_date, reminder_date,
                   source_type, source_note_id, source_file_path, source_line_number, source_char_offset,
                   is_orphaned, created_at, modified_at)
                  VALUES 
                  (@Id, @Text, @Description, 0, NULL, @CategoryId, @CategoryName, @CategoryPath,
                   @ParentId, @SortOrder, @Priority, @IsFavorite, @DueDate, @ReminderDate,
                   @SourceType, @SourceNoteId, @SourceFilePath, @SourceLineNumber, @SourceCharOffset,
                   @IsOrphaned, @CreatedAt, @ModifiedAt)",
                new
                {
                    Id = current.id,
                    Text = current.text,
                    Description = current.description,
                    CategoryId = current.category_id,
                    CategoryName = current.category_name,
                    CategoryPath = current.category_path,
                    ParentId = current.parent_id,
                    SortOrder = current.sort_order,
                    Priority = current.priority,
                    IsFavorite = current.is_favorite,
                    DueDate = current.due_date,
                    ReminderDate = current.reminder_date,
                    SourceType = current.source_type,
                    SourceNoteId = current.source_note_id,
                    SourceFilePath = current.source_file_path,
                    SourceLineNumber = current.source_line_number,
                    SourceCharOffset = current.source_char_offset,
                    IsOrphaned = current.is_orphaned,
                    CreatedAt = current.created_at,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Info($"[{Name}] ✅ Todo uncompleted using INSERT OR REPLACE: {e.TodoId}");
        }
        
        private async Task HandleTodoTextUpdatedAsync(TodoTextUpdatedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            // ✅ FIX: Use INSERT OR REPLACE instead of UPDATE
            var current = await connection.QueryFirstOrDefaultAsync(
                "SELECT * FROM todo_view WHERE id = @Id",
                new { Id = e.TodoId.Value.ToString() });
            
            if (current == null)
            {
                _logger.Warning($"[{Name}] ⚠️ Todo {e.TodoId} not found in todo_view for text update");
                return;
            }
            
            // INSERT OR REPLACE entire row with new text
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
                    Id = current.id,
                    Text = e.NewText,  // ← Updated field
                    Description = current.description,
                    IsCompleted = current.is_completed,
                    CompletedDate = current.completed_date,
                    CategoryId = current.category_id,
                    CategoryName = current.category_name,
                    CategoryPath = current.category_path,
                    ParentId = current.parent_id,
                    SortOrder = current.sort_order,
                    Priority = current.priority,
                    IsFavorite = current.is_favorite,
                    DueDate = current.due_date,
                    ReminderDate = current.reminder_date,
                    SourceType = current.source_type,
                    SourceNoteId = current.source_note_id,
                    SourceFilePath = current.source_file_path,
                    SourceLineNumber = current.source_line_number,
                    SourceCharOffset = current.source_char_offset,
                    IsOrphaned = current.is_orphaned,
                    CreatedAt = current.created_at,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Info($"[{Name}] ✅ Todo text updated using INSERT OR REPLACE: {e.TodoId}");
        }
        
        private async Task HandleTodoDueDateChangedAsync(TodoDueDateChangedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            // ✅ FIX: Use INSERT OR REPLACE instead of UPDATE
            var current = await connection.QueryFirstOrDefaultAsync(
                "SELECT * FROM todo_view WHERE id = @Id",
                new { Id = e.TodoId.Value.ToString() });
            
            if (current == null)
            {
                _logger.Warning($"[{Name}] ⚠️ Todo {e.TodoId} not found in todo_view for due date change");
                return;
            }
            
            // INSERT OR REPLACE entire row with new due_date
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
                    Id = current.id,
                    Text = current.text,
                    Description = current.description,
                    IsCompleted = current.is_completed,
                    CompletedDate = current.completed_date,
                    CategoryId = current.category_id,
                    CategoryName = current.category_name,
                    CategoryPath = current.category_path,
                    ParentId = current.parent_id,
                    SortOrder = current.sort_order,
                    Priority = current.priority,
                    IsFavorite = current.is_favorite,
                    DueDate = e.NewDueDate.HasValue ? new DateTimeOffset(e.NewDueDate.Value).ToUnixTimeSeconds() : (long?)null,  // ← Updated field
                    ReminderDate = current.reminder_date,
                    SourceType = current.source_type,
                    SourceNoteId = current.source_note_id,
                    SourceFilePath = current.source_file_path,
                    SourceLineNumber = current.source_line_number,
                    SourceCharOffset = current.source_char_offset,
                    IsOrphaned = current.is_orphaned,
                    CreatedAt = current.created_at,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Info($"[{Name}] ✅ Todo due date changed using INSERT OR REPLACE: {e.TodoId}");
        }
        
        private async Task HandleTodoPriorityChangedAsync(TodoPriorityChangedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            // ✅ FIX: Use INSERT OR REPLACE instead of UPDATE
            var current = await connection.QueryFirstOrDefaultAsync(
                "SELECT * FROM todo_view WHERE id = @Id",
                new { Id = e.TodoId.Value.ToString() });
            
            if (current == null)
            {
                _logger.Warning($"[{Name}] ⚠️ Todo {e.TodoId} not found in todo_view for priority change");
                return;
            }
            
            // INSERT OR REPLACE entire row with new priority
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
                    Id = current.id,
                    Text = current.text,
                    Description = current.description,
                    IsCompleted = current.is_completed,
                    CompletedDate = current.completed_date,
                    CategoryId = current.category_id,
                    CategoryName = current.category_name,
                    CategoryPath = current.category_path,
                    ParentId = current.parent_id,
                    SortOrder = current.sort_order,
                    Priority = e.NewPriority,  // ← Updated field
                    IsFavorite = current.is_favorite,
                    DueDate = current.due_date,
                    ReminderDate = current.reminder_date,
                    SourceType = current.source_type,
                    SourceNoteId = current.source_note_id,
                    SourceFilePath = current.source_file_path,
                    SourceLineNumber = current.source_line_number,
                    SourceCharOffset = current.source_char_offset,
                    IsOrphaned = current.is_orphaned,
                    CreatedAt = current.created_at,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Info($"[{Name}] ✅ Todo priority changed using INSERT OR REPLACE: {e.TodoId} to {e.NewPriority}");
        }
        
        private async Task HandleTodoFavoritedAsync(TodoFavoritedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            // ✅ FIX: Use INSERT OR REPLACE instead of UPDATE
            var current = await connection.QueryFirstOrDefaultAsync(
                "SELECT * FROM todo_view WHERE id = @Id",
                new { Id = e.TodoId.Value.ToString() });
            
            if (current == null)
            {
                _logger.Warning($"[{Name}] ⚠️ Todo {e.TodoId} not found in todo_view for favorite");
                return;
            }
            
            // INSERT OR REPLACE entire row with is_favorite = 1
            await connection.ExecuteAsync(
                @"INSERT OR REPLACE INTO todo_view 
                  (id, text, description, is_completed, completed_date, category_id, category_name, category_path,
                   parent_id, sort_order, priority, is_favorite, due_date, reminder_date,
                   source_type, source_note_id, source_file_path, source_line_number, source_char_offset,
                   is_orphaned, created_at, modified_at)
                  VALUES 
                  (@Id, @Text, @Description, @IsCompleted, @CompletedDate, @CategoryId, @CategoryName, @CategoryPath,
                   @ParentId, @SortOrder, @Priority, 1, @DueDate, @ReminderDate,
                   @SourceType, @SourceNoteId, @SourceFilePath, @SourceLineNumber, @SourceCharOffset,
                   @IsOrphaned, @CreatedAt, @ModifiedAt)",
                new
                {
                    Id = current.id,
                    Text = current.text,
                    Description = current.description,
                    IsCompleted = current.is_completed,
                    CompletedDate = current.completed_date,
                    CategoryId = current.category_id,
                    CategoryName = current.category_name,
                    CategoryPath = current.category_path,
                    ParentId = current.parent_id,
                    SortOrder = current.sort_order,
                    Priority = current.priority,
                    // IsFavorite = 1 (hardcoded in SQL above)
                    DueDate = current.due_date,
                    ReminderDate = current.reminder_date,
                    SourceType = current.source_type,
                    SourceNoteId = current.source_note_id,
                    SourceFilePath = current.source_file_path,
                    SourceLineNumber = current.source_line_number,
                    SourceCharOffset = current.source_char_offset,
                    IsOrphaned = current.is_orphaned,
                    CreatedAt = current.created_at,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Info($"[{Name}] ✅ Todo favorited using INSERT OR REPLACE: {e.TodoId}");
        }
        
        private async Task HandleTodoUnfavoritedAsync(TodoUnfavoritedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            // ✅ FIX: Use INSERT OR REPLACE instead of UPDATE
            var current = await connection.QueryFirstOrDefaultAsync(
                "SELECT * FROM todo_view WHERE id = @Id",
                new { Id = e.TodoId.Value.ToString() });
            
            if (current == null)
            {
                _logger.Warning($"[{Name}] ⚠️ Todo {e.TodoId} not found in todo_view for unfavorite");
                return;
            }
            
            // INSERT OR REPLACE entire row with is_favorite = 0
            await connection.ExecuteAsync(
                @"INSERT OR REPLACE INTO todo_view 
                  (id, text, description, is_completed, completed_date, category_id, category_name, category_path,
                   parent_id, sort_order, priority, is_favorite, due_date, reminder_date,
                   source_type, source_note_id, source_file_path, source_line_number, source_char_offset,
                   is_orphaned, created_at, modified_at)
                  VALUES 
                  (@Id, @Text, @Description, @IsCompleted, @CompletedDate, @CategoryId, @CategoryName, @CategoryPath,
                   @ParentId, @SortOrder, @Priority, 0, @DueDate, @ReminderDate,
                   @SourceType, @SourceNoteId, @SourceFilePath, @SourceLineNumber, @SourceCharOffset,
                   @IsOrphaned, @CreatedAt, @ModifiedAt)",
                new
                {
                    Id = current.id,
                    Text = current.text,
                    Description = current.description,
                    IsCompleted = current.is_completed,
                    CompletedDate = current.completed_date,
                    CategoryId = current.category_id,
                    CategoryName = current.category_name,
                    CategoryPath = current.category_path,
                    ParentId = current.parent_id,
                    SortOrder = current.sort_order,
                    Priority = current.priority,
                    // IsFavorite = 0 (hardcoded in SQL above)
                    DueDate = current.due_date,
                    ReminderDate = current.reminder_date,
                    SourceType = current.source_type,
                    SourceNoteId = current.source_note_id,
                    SourceFilePath = current.source_file_path,
                    SourceLineNumber = current.source_line_number,
                    SourceCharOffset = current.source_char_offset,
                    IsOrphaned = current.is_orphaned,
                    CreatedAt = current.created_at,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Info($"[{Name}] ✅ Todo unfavorited using INSERT OR REPLACE: {e.TodoId}");
        }
        
        private async Task HandleTodoDeletedAsync(TodoDeletedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            var rowsAffected = await connection.ExecuteAsync(
                "DELETE FROM todo_view WHERE id = @Id",
                new { Id = e.TodoId.Value.ToString() });
            
            // Note: Checkpoint no-op in DELETE mode (kept for compatibility)
            try
            {
                await connection.ExecuteAsync("PRAGMA wal_checkpoint(FULL)");
                _logger.Debug($"[{Name}] Database checkpoint completed for delete");
            }
            catch (Exception checkpointEx)
            {
                _logger.Warning($"[{Name}] Database checkpoint warning: {checkpointEx.Message}");
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

