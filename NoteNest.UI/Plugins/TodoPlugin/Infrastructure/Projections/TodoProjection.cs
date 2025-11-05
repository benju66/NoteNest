using System;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Infrastructure.Projections;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Events;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Projections
{
    /// <summary>
    /// Builds todo projection from todo-related events.
    /// Maintains denormalized todo view for efficient querying.
    /// </summary>
    public class TodoProjection : BaseProjection
    {
        public override string Name => "TodoView";

        public TodoProjection(string connectionString, IAppLogger logger)
            : base(connectionString, logger)
        {
        }

        public override async Task HandleAsync(IDomainEvent @event)
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

                case TodoDeletedEvent e:
                    await HandleTodoDeletedAsync(e);
                    break;

                case TodoDueDateChangedEvent e:
                    await HandleTodoDueDateChangedAsync(e);
                    break;

                case TodoFavoritedEvent e:
                    await HandleTodoFavoritedAsync(e);
                    break;

                case TodoUnfavoritedEvent e:
                    await HandleTodoUnfavoritedAsync(e);
                    break;

                case TodoPriorityChangedEvent e:
                    await HandleTodoPriorityChangedAsync(e);
                    break;
            }
        }

        protected override async Task ClearProjectionDataAsync()
        {
            using var connection = await OpenConnectionAsync();
            await connection.ExecuteAsync("DELETE FROM todo_view");
            _logger.Info($"[{Name}] Cleared projection data");
        }

        private async Task HandleTodoCreatedAsync(TodoCreatedEvent e)
        {
            using var connection = await OpenConnectionAsync();

            await connection.ExecuteAsync(
                @"INSERT INTO todo_view (
                    id, text, description, is_completed, completed_date,
                    category_id, category_name, category_path, parent_id, sort_order, 
                    priority, is_favorite, due_date, reminder_date, source_type,
                    source_note_id, source_file_path, source_line_number, source_char_offset, 
                    is_orphaned, created_at, modified_at
                ) VALUES (
                    @Id, @Text, @Description, 0, NULL,
                    @CategoryId, NULL, NULL, @ParentId, 0, 
                    1, 0, NULL, NULL, @SourceType,
                    @SourceNoteId, @SourceFilePath, @SourceLineNumber, @SourceCharOffset, 
                    0, @CreatedAt, @ModifiedAt
                )",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    Text = e.Text,
                    Description = (string)null,
                    CategoryId = e.CategoryId?.ToString(),
                    ParentId = (string)null,
                    SourceType = e.SourceNoteId.HasValue ? "note" : "manual",
                    SourceNoteId = e.SourceNoteId?.ToString(),
                    SourceFilePath = e.SourceFilePath,
                    SourceLineNumber = e.SourceLineNumber,
                    SourceCharOffset = e.SourceCharOffset,
                    CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds(),
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });

            _logger.Info($"[{Name}] Todo created: {e.TodoId.Value} - '{e.Text}'");
        }

        private async Task HandleTodoCompletedAsync(TodoCompletedEvent e)
        {
            using var connection = await OpenConnectionAsync();

            await connection.ExecuteAsync(
                @"UPDATE todo_view 
                  SET is_completed = 1, 
                      completed_date = @CompletedDate,
                      modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    CompletedDate = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds(),
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });

            _logger.Debug($"[{Name}] Todo completed: {e.TodoId.Value}");
        }

        private async Task HandleTodoUncompletedAsync(TodoUncompletedEvent e)
        {
            using var connection = await OpenConnectionAsync();

            await connection.ExecuteAsync(
                @"UPDATE todo_view 
                  SET is_completed = 0, 
                      completed_date = NULL,
                      modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });

            _logger.Debug($"[{Name}] Todo uncompleted: {e.TodoId.Value}");
        }

        private async Task HandleTodoTextUpdatedAsync(TodoTextUpdatedEvent e)
        {
            using var connection = await OpenConnectionAsync();

            await connection.ExecuteAsync(
                @"UPDATE todo_view 
                  SET text = @Text,
                      modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    Text = e.NewText,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });

            _logger.Debug($"[{Name}] Todo text updated: {e.TodoId.Value} - '{e.NewText}'");
        }

        private async Task HandleTodoDeletedAsync(TodoDeletedEvent e)
        {
            using var connection = await OpenConnectionAsync();

            await connection.ExecuteAsync(
                "DELETE FROM todo_view WHERE id = @Id",
                new { Id = e.TodoId.Value.ToString() });

            _logger.Info($"[{Name}] Todo deleted: {e.TodoId.Value}");
        }

        private async Task HandleTodoDueDateChangedAsync(TodoDueDateChangedEvent e)
        {
            using var connection = await OpenConnectionAsync();

            await connection.ExecuteAsync(
                @"UPDATE todo_view 
                  SET due_date = @DueDate,
                      modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    DueDate = e.NewDueDate.HasValue 
                        ? new DateTimeOffset(e.NewDueDate.Value).ToUnixTimeSeconds() 
                        : (long?)null,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });

            _logger.Debug($"[{Name}] Todo due date changed: {e.TodoId.Value} - {e.NewDueDate}");
        }

        private async Task HandleTodoFavoritedAsync(TodoFavoritedEvent e)
        {
            using var connection = await OpenConnectionAsync();

            await connection.ExecuteAsync(
                @"UPDATE todo_view 
                  SET is_favorite = 1,
                      modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });

            _logger.Debug($"[{Name}] Todo favorited: {e.TodoId.Value}");
        }

        private async Task HandleTodoUnfavoritedAsync(TodoUnfavoritedEvent e)
        {
            using var connection = await OpenConnectionAsync();

            await connection.ExecuteAsync(
                @"UPDATE todo_view 
                  SET is_favorite = 0,
                      modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });

            _logger.Debug($"[{Name}] Todo unfavorited: {e.TodoId.Value}");
        }

        private async Task HandleTodoPriorityChangedAsync(TodoPriorityChangedEvent e)
        {
            using var connection = await OpenConnectionAsync();

            await connection.ExecuteAsync(
                @"UPDATE todo_view 
                  SET priority = @Priority,
                      modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.TodoId.Value.ToString(),
                    Priority = e.NewPriority,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });

            _logger.Debug($"[{Name}] Todo priority changed: {e.TodoId.Value} - Priority {e.NewPriority}");
        }

    }
}