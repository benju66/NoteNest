using System;
using System.Collections.Generic;

namespace NoteNest.UI.Plugins.TodoPlugin.Models
{
    /// <summary>
    /// Simple DTO for a Todo item.
    /// </summary>
    public class TodoItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? CategoryId { get; set; }
        public Guid? ParentId { get; set; }  // For subtasks
        public string Text { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime? CompletedDate { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? ReminderDate { get; set; }
        public Priority Priority { get; set; } = Priority.Normal;
        public bool IsFavorite { get; set; }
        public int Order { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
        public List<string> Tags { get; set; } = new();
        public List<string> LinkedNoteIds { get; set; } = new();

        public bool IsOverdue()
        {
            return !IsCompleted && DueDate.HasValue && DueDate.Value.Date < DateTime.UtcNow.Date;
        }

        public bool IsDueToday()
        {
            return !IsCompleted && DueDate.HasValue && DueDate.Value.Date == DateTime.UtcNow.Date;
        }

        public bool IsDueTomorrow()
        {
            return !IsCompleted && DueDate.HasValue && DueDate.Value.Date == DateTime.UtcNow.Date.AddDays(1);
        }
    }

    public enum Priority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Urgent = 3
    }
    
    public enum TodoSource
    {
        Manual,  // Created directly by user in todo panel
        Note     // Extracted from [bracket] in RTF note
    }

    public enum SmartListType
    {
        Today,
        Tomorrow,
        ThisWeek,
        NextWeek,
        Overdue,
        NoDate,
        HighPriority,
        Favorites,
        RecentlyCompleted,
        All,
        Scheduled,
        Flagged,
        Completed
    }
}
