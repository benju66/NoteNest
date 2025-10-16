using System;
using System.Collections.Generic;
using System.Linq;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence
{
    /// <summary>
    /// Database DTO that maps SQLite TEXT/INTEGER types to C# types
    /// </summary>
    public class TodoItemDto
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public string Description { get; set; }
        public int IsCompleted { get; set; }
        public long? CompletedDate { get; set; }
        public long? DueDate { get; set; }
        public long? ReminderDate { get; set; }
        public int Priority { get; set; }
        public int IsFavorite { get; set; }
        public int SortOrder { get; set; }
        public long CreatedAt { get; set; }
        public long ModifiedAt { get; set; }
        
        // Category/hierarchy
        public string CategoryId { get; set; }
        public string ParentId { get; set; }
        
        // Source tracking
        public string SourceType { get; set; }  // 'manual' or 'note'
        public string SourceNoteId { get; set; }
        public string SourceFilePath { get; set; }
        public int? SourceLineNumber { get; set; }
        public int? SourceCharOffset { get; set; }
        public int IsOrphaned { get; set; }

        /// <summary>
        /// Convert DTO to Domain Aggregate
        /// </summary>
        public TodoAggregate ToAggregate(List<string> tags = null)
        {
            return TodoAggregate.CreateFromDatabase(
                id: Guid.Parse(Id),
                text: Text,
                isCompleted: IsCompleted == 1,
                completedDate: CompletedDate.HasValue ? DateTimeOffset.FromUnixTimeSeconds(CompletedDate.Value).UtcDateTime : null,
                dueDate: DueDate.HasValue ? DateTimeOffset.FromUnixTimeSeconds(DueDate.Value).UtcDateTime : null,
                reminderDate: ReminderDate.HasValue ? DateTimeOffset.FromUnixTimeSeconds(ReminderDate.Value).UtcDateTime : null,
                priority: Priority,
                isFavorite: IsFavorite == 1,
                order: SortOrder,
                createdAt: DateTimeOffset.FromUnixTimeSeconds(CreatedAt).UtcDateTime,
                modifiedDate: DateTimeOffset.FromUnixTimeSeconds(ModifiedAt).UtcDateTime,
                tags: tags ?? new List<string>(),
                categoryId: string.IsNullOrEmpty(CategoryId) ? null : Guid.Parse(CategoryId),
                parentId: string.IsNullOrEmpty(ParentId) ? null : Guid.Parse(ParentId),
                sourceNoteId: string.IsNullOrEmpty(SourceNoteId) ? null : Guid.Parse(SourceNoteId),
                sourceFilePath: SourceFilePath,
                sourceLineNumber: SourceLineNumber,
                sourceCharOffset: SourceCharOffset,
                isOrphaned: IsOrphaned == 1,
                description: Description
            );
        }

        /// <summary>
        /// Convert Domain Aggregate to DTO
        /// </summary>
        public static TodoItemDto FromAggregate(TodoAggregate aggregate)
        {
            return new TodoItemDto
            {
                Id = aggregate.Id.ToString(),
                Text = aggregate.Text.Value,
                Description = aggregate.Description,
                IsCompleted = aggregate.IsCompleted ? 1 : 0,
                CompletedDate = aggregate.CompletedDate.HasValue ? new DateTimeOffset(aggregate.CompletedDate.Value).ToUnixTimeSeconds() : null,
                DueDate = aggregate.DueDate?.Value != null ? new DateTimeOffset(aggregate.DueDate.Value).ToUnixTimeSeconds() : null,
                ReminderDate = aggregate.ReminderDate.HasValue ? new DateTimeOffset(aggregate.ReminderDate.Value).ToUnixTimeSeconds() : null,
                Priority = (int)aggregate.Priority,
                IsFavorite = aggregate.IsFavorite ? 1 : 0,
                SortOrder = aggregate.Order,
                CreatedAt = new DateTimeOffset(aggregate.CreatedAt).ToUnixTimeSeconds(),
                ModifiedAt = new DateTimeOffset(aggregate.ModifiedDate).ToUnixTimeSeconds(),
                CategoryId = aggregate.CategoryId?.ToString(),
                ParentId = aggregate.ParentId?.ToString(),
                SourceType = aggregate.SourceNoteId.HasValue ? "note" : "manual",  // Determine type from source
                SourceNoteId = aggregate.SourceNoteId?.ToString(),
                SourceFilePath = aggregate.SourceFilePath,
                SourceLineNumber = aggregate.SourceLineNumber,
                SourceCharOffset = aggregate.SourceCharOffset,
                IsOrphaned = aggregate.IsOrphaned ? 1 : 0
            };
        }
    }
}

