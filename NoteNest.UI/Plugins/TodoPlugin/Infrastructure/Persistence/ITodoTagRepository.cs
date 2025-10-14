using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence
{
    /// <summary>
    /// Repository for managing todo tags.
    /// Handles both auto-generated and manual tags.
    /// </summary>
    public interface ITodoTagRepository
    {
        /// <summary>
        /// Get all tags for a todo (both auto and manual).
        /// </summary>
        Task<List<TodoTag>> GetByTodoIdAsync(Guid todoId);

        /// <summary>
        /// Get only auto-generated tags for a todo.
        /// </summary>
        Task<List<TodoTag>> GetAutoTagsAsync(Guid todoId);

        /// <summary>
        /// Get only manual tags for a todo.
        /// </summary>
        Task<List<TodoTag>> GetManualTagsAsync(Guid todoId);

        /// <summary>
        /// Check if a tag exists on a todo.
        /// </summary>
        Task<bool> ExistsAsync(Guid todoId, string tagName);

        /// <summary>
        /// Add a tag to a todo.
        /// </summary>
        Task AddAsync(TodoTag tag);

        /// <summary>
        /// Remove a specific tag from a todo.
        /// </summary>
        Task DeleteAsync(Guid todoId, string tagName);

        /// <summary>
        /// Remove all auto-generated tags from a todo.
        /// Used when todo is moved to update auto-tags.
        /// </summary>
        Task DeleteAutoTagsAsync(Guid todoId);

        /// <summary>
        /// Remove all tags from a todo.
        /// Used when todo is deleted (cascade should handle this, but explicit is safer).
        /// </summary>
        Task DeleteAllAsync(Guid todoId);
    }
}

