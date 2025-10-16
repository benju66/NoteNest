using System;
using System.Collections.Generic;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates;

namespace NoteNest.UI.Plugins.TodoPlugin.Models
{
    /// <summary>
    /// UI model for a Todo item. Converts to/from TodoAggregate for business logic.
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
        
        // Source tracking for RTF integration
        public Guid? SourceNoteId { get; set; }
        public string? SourceFilePath { get; set; }
        public int? SourceLineNumber { get; set; }
        public int? SourceCharOffset { get; set; }
        public bool IsOrphaned { get; set; }
        public List<string> LinkedNoteIds { get; set; } = new();  // Legacy, for future use

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

        // =============================================================================
        // AGGREGATE CONVERSIONS (for clean architecture)
        // =============================================================================

        /// <summary>
        /// Convert UI model to domain aggregate for business logic
        /// </summary>
        public TodoAggregate ToAggregate()
        {
            return TodoAggregate.CreateFromDatabase(
                id: this.Id,
                text: this.Text,
                isCompleted: this.IsCompleted,
                completedDate: this.CompletedDate,
                dueDate: this.DueDate,
                reminderDate: this.ReminderDate,
                priority: (int)this.Priority,
                isFavorite: this.IsFavorite,
                order: this.Order,
                createdAt: this.CreatedDate,
                modifiedDate: this.ModifiedDate,
                tags: this.Tags,
                categoryId: this.CategoryId,
                parentId: this.ParentId,
                sourceNoteId: this.SourceNoteId,
                sourceFilePath: this.SourceFilePath,
                sourceLineNumber: this.SourceLineNumber,
                sourceCharOffset: this.SourceCharOffset,
                isOrphaned: this.IsOrphaned,
                description: this.Description
            );
        }

        /// <summary>
        /// Convert domain aggregate to UI model
        /// </summary>
        public static TodoItem FromAggregate(TodoAggregate aggregate)
        {
            return new TodoItem
            {
                Id = aggregate.Id,
                CategoryId = aggregate.CategoryId,
                ParentId = aggregate.ParentId,
                Text = aggregate.Text.Value,
                Description = aggregate.Description,
                IsCompleted = aggregate.IsCompleted,
                CompletedDate = aggregate.CompletedDate,
                DueDate = aggregate.DueDate?.Value,
                ReminderDate = aggregate.ReminderDate,
                Priority = (Priority)(int)aggregate.Priority,  // Cast from domain to UI enum
                IsFavorite = aggregate.IsFavorite,
                Order = aggregate.Order,
                CreatedDate = aggregate.CreatedAt,
                ModifiedDate = aggregate.ModifiedDate,
                Tags = aggregate.Tags ?? new List<string>(),
                SourceNoteId = aggregate.SourceNoteId,
                SourceFilePath = aggregate.SourceFilePath,
                SourceLineNumber = aggregate.SourceLineNumber,
                SourceCharOffset = aggregate.SourceCharOffset,
                IsOrphaned = aggregate.IsOrphaned
            };
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
