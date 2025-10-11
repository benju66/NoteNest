using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence
{
    /// <summary>
    /// Clean repository interface following DDD + DTO pattern.
    /// Flow: Database → TodoItemDto → TodoAggregate → TodoItem (UI)
    /// 
    /// Only methods actually used by consumers (TodoStore, TodoSyncService, CategoryCleanupService).
    /// </summary>
    public interface ITodoRepository
    {
        // =========================================================================
        // CORE OPERATIONS (TodoStore)
        // =========================================================================
        
        /// <summary>
        /// Get all todos with optional completion filter.
        /// </summary>
        Task<List<TodoItem>> GetAllAsync(bool includeCompleted = true);
        
        /// <summary>
        /// Get single todo by ID. Returns null if not found.
        /// </summary>
        Task<TodoItem?> GetByIdAsync(Guid id);
        
        /// <summary>
        /// Insert new todo. Returns true if successful.
        /// </summary>
        Task<bool> InsertAsync(TodoItem todo);
        
        /// <summary>
        /// Update existing todo. Returns true if successful.
        /// </summary>
        Task<bool> UpdateAsync(TodoItem todo);
        
        /// <summary>
        /// Delete todo permanently. Returns true if deleted.
        /// </summary>
        Task<bool> DeleteAsync(Guid id);
        
        // =========================================================================
        // QUERY OPERATIONS (TodoStore)
        // =========================================================================
        
        /// <summary>
        /// Get todos by category ID.
        /// </summary>
        Task<List<TodoItem>> GetByCategoryAsync(Guid categoryId, bool includeCompleted = false);
        
        /// <summary>
        /// Get recently completed todos.
        /// </summary>
        Task<List<TodoItem>> GetRecentlyCompletedAsync(int count = 10);
        
        // =========================================================================
        // NOTE SYNC OPERATIONS (TodoSyncService)
        // =========================================================================
        
        /// <summary>
        /// Get todos linked to a specific note.
        /// </summary>
        Task<List<TodoItem>> GetByNoteIdAsync(Guid noteId);
        
        /// <summary>
        /// Update last_seen timestamp for a todo (used by sync service).
        /// </summary>
        Task UpdateLastSeenAsync(Guid todoId);
        
        /// <summary>
        /// Mark all todos from a note as orphaned (when note deleted).
        /// Returns count of todos marked.
        /// </summary>
        Task<int> MarkOrphanedByNoteAsync(Guid noteId);
        
        // =========================================================================
        // CLEANUP OPERATIONS (CategoryCleanupService)
        // =========================================================================
        
        /// <summary>
        /// Update category for todos in bulk.
        /// </summary>
        Task UpdateCategoryForTodosAsync(Guid oldCategoryId, Guid? newCategoryId);
    }
}

