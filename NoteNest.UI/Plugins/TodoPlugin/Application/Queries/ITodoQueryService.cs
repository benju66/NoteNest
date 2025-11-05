using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Queries
{
    /// <summary>
    /// Query service for reading todos from projections.
    /// All queries read from projections.db, not the legacy todos.db.
    /// </summary>
    public interface ITodoQueryService
    {
        /// <summary>
        /// Get all todos, optionally including completed ones.
        /// </summary>
        Task<List<TodoDto>> GetAllTodosAsync(bool includeCompleted = false);

        /// <summary>
        /// Get a specific todo by ID.
        /// </summary>
        Task<TodoDto> GetTodoByIdAsync(Guid todoId);

        /// <summary>
        /// Get all todos in a specific category.
        /// </summary>
        Task<List<TodoDto>> GetTodosByCategoryAsync(Guid categoryId, bool includeCompleted = false);

        /// <summary>
        /// Get todos with their inherited tags denormalized.
        /// Includes tags from the todo itself, its category, and parent categories.
        /// </summary>
        Task<List<TodoWithTagsDto>> GetTodosWithInheritedTagsAsync(bool includeCompleted = false);

        /// <summary>
        /// Get a specific todo with all its tags (manual and inherited).
        /// </summary>
        Task<TodoWithTagsDto> GetTodoWithTagsAsync(Guid todoId);

        /// <summary>
        /// Get todos by smart list criteria.
        /// </summary>
        Task<List<TodoDto>> GetSmartListTodosAsync(SmartListType listType);

        /// <summary>
        /// Get overdue todos.
        /// </summary>
        Task<List<TodoDto>> GetOverdueTodosAsync();

        /// <summary>
        /// Get todos due today.
        /// </summary>
        Task<List<TodoDto>> GetTodosDueTodayAsync();

        /// <summary>
        /// Get favorite todos.
        /// </summary>
        Task<List<TodoDto>> GetFavoriteTodosAsync();

        /// <summary>
        /// Get high priority todos.
        /// </summary>
        Task<List<TodoDto>> GetHighPriorityTodosAsync();

        /// <summary>
        /// Get completed todos.
        /// </summary>
        Task<List<TodoDto>> GetCompletedTodosAsync();

        /// <summary>
        /// Search todos by text.
        /// </summary>
        Task<List<TodoDto>> SearchTodosAsync(string searchText);
    }

    /// <summary>
    /// DTO for todo data from projections.
    /// </summary>
    public class TodoDto
    {
        public Guid Id { get; set; }
        public string Text { get; set; }
        public string Description { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime? CompletedDate { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? ReminderDate { get; set; }
        public int Priority { get; set; }
        public bool IsFavorite { get; set; }
        public int Order { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        
        // Category/hierarchy
        public Guid? CategoryId { get; set; }
        public Guid? ParentId { get; set; }
        
        // Source tracking for RTF integration
        public Guid? SourceNoteId { get; set; }
        public string SourceFilePath { get; set; }
        public int? SourceLineNumber { get; set; }
        public int? SourceCharOffset { get; set; }
        public bool IsOrphaned { get; set; }
    }

    /// <summary>
    /// DTO for todo with denormalized tags.
    /// </summary>
    public class TodoWithTagsDto : TodoDto
    {
        public List<TagDto> Tags { get; set; } = new();
    }

    /// <summary>
    /// DTO for tag information including source.
    /// </summary>
    public class TagDto
    {
        public string Tag { get; set; }
        public string DisplayName { get; set; }
        public string Source { get; set; } // "manual", "auto-inherit", "auto-path"
        public string EntityType { get; set; } // "todo", "category", "note"
        public Guid EntityId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

}