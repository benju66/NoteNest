using System;
using System.Collections.Generic;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Domain.ValueObjects;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Events;

namespace NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates
{
    public class TodoAggregate : AggregateRoot
    {
        public TodoId Id { get; private set; }
        public TodoText Text { get; private set; }
        public string Description { get; private set; }
        public bool IsCompleted { get; private set; }
        public DateTime? CompletedDate { get; private set; }
        public DueDate DueDate { get; private set; }
        public DateTime? ReminderDate { get; private set; }
        public Priority Priority { get; private set; }
        public bool IsFavorite { get; private set; }
        public int Order { get; private set; }
        public DateTime ModifiedDate { get; private set; }
        public List<string> Tags { get; private set; }
        
        // Category/hierarchy
        public Guid? CategoryId { get; private set; }
        public Guid? ParentId { get; private set; }
        
        // Source tracking for RTF integration
        public Guid? SourceNoteId { get; private set; }
        public string SourceFilePath { get; private set; }
        public int? SourceLineNumber { get; private set; }
        public int? SourceCharOffset { get; private set; }
        public bool IsOrphaned { get; private set; }

        private TodoAggregate() 
        {
            Tags = new List<string>();
        }

        // =============================================================================
        // FACTORY METHODS
        // =============================================================================

        /// <summary>
        /// Create a new todo from user input
        /// </summary>
        public static Result<TodoAggregate> Create(string text, Guid? categoryId = null)
        {
            var textResult = TodoText.Create(text);
            if (textResult.IsFailure)
                return Result.Fail<TodoAggregate>(textResult.Error);

            var aggregate = new TodoAggregate
            {
                Id = TodoId.Create(),
                Text = textResult.Value,
                CategoryId = categoryId,
                IsCompleted = false,
                Priority = Priority.Normal,
                Order = 0,
                CreatedAt = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow,
                Tags = new List<string>()
            };

            aggregate.AddDomainEvent(new TodoCreatedEvent(aggregate.Id, text, categoryId));
            return Result.Ok(aggregate);
        }

        /// <summary>
        /// Create a todo from RTF note bracket parsing
        /// </summary>
        public static Result<TodoAggregate> CreateFromNote(
            string text,
            Guid sourceNoteId,
            string sourceFilePath,
            int? lineNumber = null,
            int? charOffset = null)
        {
            var textResult = TodoText.Create(text);
            if (textResult.IsFailure)
                return Result.Fail<TodoAggregate>(textResult.Error);

            var aggregate = new TodoAggregate
            {
                Id = TodoId.Create(),
                Text = textResult.Value,
                SourceNoteId = sourceNoteId,
                SourceFilePath = sourceFilePath,
                SourceLineNumber = lineNumber,
                SourceCharOffset = charOffset,
                IsCompleted = false,
                Priority = Priority.Normal,
                Order = 0,
                CreatedAt = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow,
                Tags = new List<string>()
            };

            aggregate.AddDomainEvent(new TodoCreatedEvent(aggregate.Id, text, null));
            return Result.Ok(aggregate);
        }

        /// <summary>
        /// Reconstruct aggregate from database (for repository)
        /// </summary>
        public static TodoAggregate CreateFromDatabase(
            Guid id,
            string text,
            bool isCompleted,
            DateTime? completedDate,
            DateTime? dueDate,
            DateTime? reminderDate,
            int priority,
            bool isFavorite,
            int order,
            DateTime createdAt,
            DateTime modifiedDate,
            List<string> tags,
            Guid? categoryId,
            Guid? parentId,
            Guid? sourceNoteId,
            string sourceFilePath,
            int? sourceLineNumber,
            int? sourceCharOffset,
            bool isOrphaned,
            string description)
        {
            var textResult = TodoText.Create(text);
            if (textResult.IsFailure)
                throw new InvalidOperationException($"Invalid text in database: {text}");

            return new TodoAggregate
            {
                Id = TodoId.From(id),
                Text = textResult.Value,
                Description = description,
                IsCompleted = isCompleted,
                CompletedDate = completedDate,
                DueDate = dueDate.HasValue ? DueDate.Create(dueDate.Value).Value : null,
                ReminderDate = reminderDate,
                Priority = (Priority)priority,
                IsFavorite = isFavorite,
                Order = order,
                CreatedAt = createdAt,
                ModifiedDate = modifiedDate,
                Tags = tags ?? new List<string>(),
                CategoryId = categoryId,
                ParentId = parentId,
                SourceNoteId = sourceNoteId,
                SourceFilePath = sourceFilePath,
                SourceLineNumber = sourceLineNumber,
                SourceCharOffset = sourceCharOffset,
                IsOrphaned = isOrphaned
            };
        }

        // =============================================================================
        // BUSINESS LOGIC
        // =============================================================================

        public Result Complete()
        {
            if (IsCompleted)
                return Result.Fail("Todo is already completed");

            IsCompleted = true;
            CompletedDate = DateTime.UtcNow;
            ModifiedDate = DateTime.UtcNow;

            AddDomainEvent(new TodoCompletedEvent(Id));
            return Result.Ok();
        }

        public Result Uncomplete()
        {
            if (!IsCompleted)
                return Result.Fail("Todo is not completed");

            IsCompleted = false;
            CompletedDate = null;
            ModifiedDate = DateTime.UtcNow;

            AddDomainEvent(new TodoUncompletedEvent(Id));
            return Result.Ok();
        }

        public Result UpdateText(string newText)
        {
            var textResult = TodoText.Create(newText);
            if (textResult.IsFailure)
                return Result.Fail(textResult.Error);

            Text = textResult.Value;
            ModifiedDate = DateTime.UtcNow;

            AddDomainEvent(new TodoTextUpdatedEvent(Id, newText));
            return Result.Ok();
        }

        public Result SetDueDate(DateTime? dueDate)
        {
            if (dueDate.HasValue)
            {
                var dueDateResult = DueDate.Create(dueDate.Value);
                if (dueDateResult.IsFailure)
                    return Result.Fail(dueDateResult.Error);
                DueDate = dueDateResult.Value;
            }
            else
            {
                DueDate = null;
            }

            ModifiedDate = DateTime.UtcNow;
            AddDomainEvent(new TodoDueDateChangedEvent(Id, dueDate));
            return Result.Ok();
        }

        public void SetPriority(Priority priority)
        {
            Priority = priority;
            ModifiedDate = DateTime.UtcNow;
            AddDomainEvent(new TodoPriorityChangedEvent(Id, (int)priority));
        }

        public void ToggleFavorite()
        {
            IsFavorite = !IsFavorite;
            ModifiedDate = DateTime.UtcNow;
            
            if (IsFavorite)
                AddDomainEvent(new TodoFavoritedEvent(Id));
            else
                AddDomainEvent(new TodoUnfavoritedEvent(Id));
        }

        public void SetDescription(string description)
        {
            Description = description;
            ModifiedDate = DateTime.UtcNow;
        }

        public void AddTag(string tag)
        {
            if (!Tags.Contains(tag))
            {
                Tags.Add(tag);
                ModifiedDate = DateTime.UtcNow;
            }
        }

        public void RemoveTag(string tag)
        {
            if (Tags.Remove(tag))
            {
                ModifiedDate = DateTime.UtcNow;
            }
        }

        public void SetCategory(Guid? categoryId)
        {
            CategoryId = categoryId;
            ModifiedDate = DateTime.UtcNow;
        }

        public void MarkAsOrphaned()
        {
            IsOrphaned = true;
            ModifiedDate = DateTime.UtcNow;
        }

        // =============================================================================
        // HELPER METHODS
        // =============================================================================

        public bool IsOverdue()
        {
            return !IsCompleted && DueDate != null && DueDate.IsOverdue();
        }

        public bool IsDueToday()
        {
            return !IsCompleted && DueDate != null && DueDate.IsToday();
        }

        public bool IsDueTomorrow()
        {
            return !IsCompleted && DueDate != null && DueDate.IsTomorrow();
        }
    }

    // Keep Priority enum in same file for convenience
    public enum Priority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Urgent = 3
    }
}

