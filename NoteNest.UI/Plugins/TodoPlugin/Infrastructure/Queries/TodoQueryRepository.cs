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
        // CONVERSION HELPERS
        // =============================================================================
        
        private TodoItem ConvertDtoToTodoItem(TodoDto dto)
        {
            if (dto == null) return null;
            
            return new TodoItem
            {
                Id = dto.Id,
                Text = dto.Text,
                Description = dto.Description,
                IsCompleted = dto.IsCompleted,
                CompletedDate = dto.CompletedDate,
                DueDate = dto.DueDate,
                ReminderDate = dto.ReminderDate,
                Priority = (Priority)dto.Priority,
                IsFavorite = dto.IsFavorite,
                Order = dto.Order,
                CreatedDate = dto.CreatedDate,
                ModifiedDate = dto.ModifiedDate,
                CategoryId = dto.CategoryId,
                ParentId = dto.ParentId,
                SourceNoteId = dto.SourceNoteId,
                SourceFilePath = dto.SourceFilePath,
                SourceLineNumber = dto.SourceLineNumber,
                SourceCharOffset = dto.SourceCharOffset,
                IsOrphaned = dto.IsOrphaned,
                Tags = new List<string>(), // Tags loaded separately if needed
                LinkedNoteIds = new List<string>()
            };
        }
        
        private List<TodoItem> ConvertDtoListToTodoItemList(List<TodoDto> dtos)
        {
            if (dtos == null) return new List<TodoItem>();
            return dtos.Select(ConvertDtoToTodoItem).Where(t => t != null).ToList();
        }

        // =============================================================================
        // READ OPERATIONS - Delegate to QueryService (reads from projections.db)
        // =============================================================================

        // Wrapper methods for ITodoRepository compatibility
        public async Task<TodoItem?> GetByIdAsync(Guid id) => await GetTodoByIdAsync(id);
        public async Task<List<TodoItem>> GetAllAsync(bool includeCompleted = true) => await GetAllTodosAsync(includeCompleted);
        public async Task<List<TodoItem>> GetByCategoryAsync(Guid categoryId, bool includeCompleted = false) => await GetTodosByCategoryAsync(categoryId, includeCompleted);
        public async Task<List<TodoItem>> GetByNoteIdAsync(Guid noteId) => await GetTodosByNoteIdAsync(noteId);

        public async Task<TodoItem> GetTodoByIdAsync(Guid id)
        {
            try
            {
                var dto = await _queryService.GetTodoByIdAsync(id);
                return ConvertDtoToTodoItem(dto);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get todo by ID: {id}");
                return null;
            }
        }

        public async Task<List<TodoItem>> GetAllTodosAsync(bool includeCompleted = true)
        {
            try
            {
                var dtos = await _queryService.GetAllTodosAsync(includeCompleted);
                return ConvertDtoListToTodoItemList(dtos);
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
                var dtos = await _queryService.GetAllTodosAsync(includeCompleted: false);
                return ConvertDtoListToTodoItemList(dtos);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get active todos");
                return new List<TodoItem>();
            }
        }

        public async Task<List<TodoItem>> GetTodosByCategoryAsync(Guid categoryId, bool includeCompleted = false)
        {
            try
            {
                var dtos = await _queryService.GetTodosByCategoryAsync(categoryId);
                var allTodos = ConvertDtoListToTodoItemList(dtos);
                return includeCompleted ? allTodos : allTodos.Where(t => !t.IsCompleted).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get todos by category: {categoryId}");
                return new List<TodoItem>();
            }
        }

        public async Task<List<TodoItem>> GetTodosByNoteIdAsync(Guid noteId)
        {
            try
            {
                // TODO: GetTodosByNoteIdAsync doesn't exist in ITodoQueryService
                // For now, return empty list
                return new List<TodoItem>();
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
                var dtos = await _queryService.GetSmartListTodosAsync(SmartListType.Completed);
                var completedTodos = ConvertDtoListToTodoItemList(dtos);
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
                var allTodoDtos = await _queryService.GetAllTodosAsync(includeCompleted: false);
                var orphanedDtos = allTodoDtos.Where(t => t.IsOrphaned).ToList();
                return ConvertDtoListToTodoItemList(orphanedDtos);
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
            // No-op: Last seen tracking not needed in event-sourced system
            // The event store already tracks when events occur
            _logger.Debug($"UpdateLastSeenAsync called for todo {todoId} - no-op in event-sourced system");
            return Task.CompletedTask;
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

