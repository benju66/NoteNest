using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Application.Queries;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Queries
{
    /// <summary>
    /// Read-only repository for Todos using ITodoQueryService projection.
    /// Provides TodoItem data from the todo_view projection in projections.db.
    /// Mirrors CategoryQueryRepository and NoteQueryRepository pattern for consistency.
    /// Write operations throw NotSupportedException (use CQRS command handlers instead).
    /// </summary>
    public class TodoQueryRepository : ITodoRepository
    {
        private readonly ITodoQueryService _queryService;
        private readonly IAppLogger _logger;

        public TodoQueryRepository(ITodoQueryService queryService, IAppLogger logger)
        {
            _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // =============================================================================
        // READ OPERATIONS - Delegate to QueryService (reads from projections.db)
        // =============================================================================

        public async Task<TodoItem> GetByIdAsync(Guid id)
        {
            try
            {
                return await _queryService.GetByIdAsync(id);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get todo by ID: {id}");
                return null;
            }
        }

        public async Task<List<TodoItem>> GetAllAsync(bool includeCompleted = true)
        {
            try
            {
                return await _queryService.GetAllAsync(includeCompleted);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get all todos");
                return new List<TodoItem>();
            }
        }

        public async Task<List<TodoItem>> GetActiveTodosAsync()
        {
            try
            {
                return await _queryService.GetAllAsync(includeCompleted: false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get active todos");
                return new List<TodoItem>();
            }
        }

        public async Task<List<TodoItem>> GetByCategoryAsync(Guid categoryId, bool includeCompleted = false)
        {
            try
            {
                var allTodos = await _queryService.GetByCategoryAsync(categoryId);
                return includeCompleted ? allTodos : allTodos.Where(t => !t.IsCompleted).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get todos by category: {categoryId}");
                return new List<TodoItem>();
            }
        }

        public async Task<List<TodoItem>> GetByNoteIdAsync(Guid noteId)
        {
            try
            {
                return await _queryService.GetByNoteIdAsync(noteId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get todos by note ID: {noteId}");
                return new List<TodoItem>();
            }
        }

        public async Task<List<TodoItem>> GetRecentlyCompletedAsync(int count = 100)
        {
            try
            {
                var completedTodos = await _queryService.GetSmartListAsync(SmartListType.Completed);
                return completedTodos.Take(count).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get recently completed todos");
                return new List<TodoItem>();
            }
        }

        public async Task<List<TodoItem>> GetOrphanedTodosAsync()
        {
            try
            {
                var allTodos = await _queryService.GetAllAsync(includeCompleted: false);
                return allTodos.Where(t => t.IsOrphaned).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get orphaned todos");
                return new List<TodoItem>();
            }
        }

        // =============================================================================
        // WRITE OPERATIONS - Not Supported (use CQRS command handlers)
        // =============================================================================

        public Task<bool> InsertAsync(TodoItem todo)
        {
            throw new NotSupportedException("Insert operations not supported in query repository. Use CreateTodoCommand instead.");
        }

        public Task<bool> UpdateAsync(TodoItem todo)
        {
            throw new NotSupportedException("Update operations not supported in query repository. Use appropriate command (CompleteTodoCommand, UpdateTodoTextCommand, etc.) instead.");
        }

        public Task<bool> DeleteAsync(Guid id)
        {
            throw new NotSupportedException("Delete operations not supported in query repository. Use DeleteTodoCommand instead.");
        }

        public Task<int> DeleteCompletedAsync(DateTime? beforeDate = null)
        {
            throw new NotSupportedException("Bulk delete operations not supported in query repository. Use appropriate command handler instead.");
        }

        public Task<bool> MarkAsOrphanedAsync(Guid id)
        {
            throw new NotSupportedException("Update operations not supported in query repository. Use MarkTodoAsOrphanedCommand instead.");
        }

        public Task<int> DeleteOrphanedAsync()
        {
            throw new NotSupportedException("Bulk delete operations not supported in query repository. Use DeleteOrphanedTodosCommand instead.");
        }

        public Task UpdateLastSeenAsync(Guid todoId)
        {
            throw new NotSupportedException("Update operations not supported in query repository. Todo sync uses UpdateTodoCommand instead.");
        }

        public Task<int> MarkOrphanedByNoteAsync(Guid noteId)
        {
            throw new NotSupportedException("Bulk update operations not supported in query repository. Use MarkTodosAsOrphanedCommand instead.");
        }

        public Task UpdateCategoryForTodosAsync(Guid oldCategoryId, Guid? newCategoryId)
        {
            throw new NotSupportedException("Bulk update operations not supported in query repository. Category changes handled through projection updates.");
        }
    }
}

