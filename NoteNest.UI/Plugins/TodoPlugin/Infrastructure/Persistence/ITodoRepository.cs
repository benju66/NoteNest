using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence
{
    /// <summary>
    /// Repository interface for todo database operations.
    /// Follows TreeDatabaseRepository pattern with high-performance queries.
    /// </summary>
    public interface ITodoRepository
    {
        // =========================================================================
        // CORE CRUD OPERATIONS
        // =========================================================================
        
        Task<TodoItem?> GetByIdAsync(Guid id);
        Task<List<TodoItem>> GetAllAsync(bool includeCompleted = true);
        Task<bool> InsertAsync(TodoItem todo);
        Task<bool> UpdateAsync(TodoItem todo);
        Task<bool> DeleteAsync(Guid id);
        Task<int> BulkInsertAsync(IEnumerable<TodoItem> todos);
        
        // =========================================================================
        // QUERY OPERATIONS
        // =========================================================================
        
        Task<List<TodoItem>> GetByCategoryAsync(Guid categoryId, bool includeCompleted = false);
        Task<List<TodoItem>> GetBySourceAsync(TodoSource sourceType);
        Task<List<TodoItem>> GetByParentAsync(Guid parentId);
        Task<List<TodoItem>> GetRootTodosAsync(Guid? categoryId = null);
        
        // =========================================================================
        // SMART LISTS
        // =========================================================================
        
        Task<List<TodoItem>> GetTodayTodosAsync();
        Task<List<TodoItem>> GetOverdueTodosAsync();
        Task<List<TodoItem>> GetHighPriorityTodosAsync();
        Task<List<TodoItem>> GetFavoriteTodosAsync();
        Task<List<TodoItem>> GetRecentlyCompletedAsync(int count = 100);
        Task<List<TodoItem>> GetScheduledTodosAsync();
        
        // =========================================================================
        // SEARCH
        // =========================================================================
        
        Task<List<TodoItem>> SearchAsync(string searchTerm);
        Task<List<TodoItem>> GetByTagAsync(string tag);
        Task<List<string>> GetAllTagsAsync();
        
        // =========================================================================
        // TAG OPERATIONS
        // =========================================================================
        
        Task<List<string>> GetTagsForTodoAsync(Guid todoId);
        Task<bool> AddTagAsync(Guid todoId, string tag);
        Task<bool> RemoveTagAsync(Guid todoId, string tag);
        Task<bool> SetTagsAsync(Guid todoId, IEnumerable<string> tags);
        
        // =========================================================================
        // NOTE-LINKED TODO OPERATIONS
        // =========================================================================
        
        Task<List<TodoItem>> GetByNoteIdAsync(Guid noteId);
        Task<List<TodoItem>> GetOrphanedTodosAsync();
        Task<int> MarkOrphanedByNoteAsync(Guid noteId);
        Task<bool> UpdateLastSeenAsync(Guid todoId);
        
        // =========================================================================
        // MAINTENANCE
        // =========================================================================
        
        Task<int> DeleteCompletedOlderThanAsync(int days);
        Task<int> DeleteOrphanedOlderThanAsync(int days);
        Task<DatabaseStats> GetStatsAsync();
        Task<bool> OptimizeAsync();
        Task<bool> VacuumAsync();
        
        // =========================================================================
        // REBUILD OPERATIONS (for note-linked todos)
        // =========================================================================
        
        Task<int> DeleteAllNoteLin​kedTodosAsync();
        Task<bool> RebuildFromNotesAsync(string notesRootPath);
    }
    
    /// <summary>
    /// Database statistics
    /// </summary>
    public class DatabaseStats
    {
        public int TotalTodos { get; set; }
        public int ActiveTodos { get; set; }
        public int CompletedTodos { get; set; }
        public int ManualTodos { get; set; }
        public int NoteLinkedTodos { get; set; }
        public int OrphanedTodos { get; set; }
        public int OverdueTodos { get; set; }
        public int HighPriorityTodos { get; set; }
        public long DatabaseSizeBytes { get; set; }
    }
}

