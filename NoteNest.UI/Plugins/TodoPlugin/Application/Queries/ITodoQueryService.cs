using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Queries
{
    /// <summary>
    /// Query service for todo projection.
    /// Provides optimized queries for todo data.
    /// Uses SmartListType from Models namespace.
    /// </summary>
    public interface ITodoQueryService
    {
        /// <summary>
        /// Get todo by ID.
        /// </summary>
        Task<TodoItem> GetByIdAsync(Guid id);
        
        /// <summary>
        /// Get todos for a category.
        /// </summary>
        Task<List<TodoItem>> GetByCategoryAsync(Guid? categoryId);
        
        /// <summary>
        /// Get todos from a smart list.
        /// </summary>
        Task<List<TodoItem>> GetSmartListAsync(SmartListType type);
        
        /// <summary>
        /// Get todos linked to a note.
        /// </summary>
        Task<List<TodoItem>> GetByNoteIdAsync(Guid noteId);
        
        /// <summary>
        /// Search todos by text.
        /// </summary>
        Task<List<TodoItem>> SearchAsync(string query);
        
        /// <summary>
        /// Get all todos.
        /// </summary>
        Task<List<TodoItem>> GetAllAsync(bool includeCompleted = true);
    }
}

