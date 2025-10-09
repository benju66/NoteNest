using System;
using System.Collections.Generic;
using System.Linq;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence
{
    /// <summary>
    /// Maps between UI Model (TodoItem), Domain Model (TodoAggregate), and Database DTO (TodoItemDto)
    /// This keeps UI unchanged while using proper domain model internally
    /// </summary>
    public static class TodoMapper
    {
        // =============================================================================
        // UI MODEL → DOMAIN AGGREGATE
        // =============================================================================

        public static TodoAggregate ToAggregate(TodoItem uiModel)
        {
            return TodoAggregate.CreateFromDatabase(
                id: uiModel.Id,
                text: uiModel.Text,
                isCompleted: uiModel.IsCompleted,
                completedDate: uiModel.CompletedDate,
                dueDate: uiModel.DueDate,
                reminderDate: uiModel.ReminderDate,
                priority: (int)uiModel.Priority,
                isFavorite: uiModel.IsFavorite,
                order: uiModel.Order,
                createdAt: uiModel.CreatedDate,
                modifiedDate: uiModel.ModifiedDate,
                tags: uiModel.Tags,
                categoryId: uiModel.CategoryId,
                parentId: uiModel.ParentId,
                sourceNoteId: uiModel.SourceNoteId,
                sourceFilePath: uiModel.SourceFilePath,
                sourceLineNumber: uiModel.SourceLineNumber,
                sourceCharOffset: uiModel.SourceCharOffset,
                isOrphaned: uiModel.IsOrphaned,
                description: uiModel.Description
            );
        }

        // =============================================================================
        // DOMAIN AGGREGATE → UI MODEL
        // =============================================================================

        public static TodoItem ToUiModel(TodoAggregate aggregate)
        {
            return new TodoItem
            {
                Id = aggregate.Id.Value,
                Text = aggregate.Text.Value,
                Description = aggregate.Description,
                IsCompleted = aggregate.IsCompleted,
                CompletedDate = aggregate.CompletedDate,
                DueDate = aggregate.DueDate?.Value,
                ReminderDate = aggregate.ReminderDate,
                Priority = (Models.Priority)aggregate.Priority,
                IsFavorite = aggregate.IsFavorite,
                Order = aggregate.Order,
                CreatedDate = aggregate.CreatedAt,
                ModifiedDate = aggregate.ModifiedDate,
                Tags = aggregate.Tags.ToList(),
                CategoryId = aggregate.CategoryId,
                ParentId = aggregate.ParentId,
                SourceNoteId = aggregate.SourceNoteId,
                SourceFilePath = aggregate.SourceFilePath,
                SourceLineNumber = aggregate.SourceLineNumber,
                SourceCharOffset = aggregate.SourceCharOffset,
                IsOrphaned = aggregate.IsOrphaned
            };
        }

        // =============================================================================
        // BULK CONVERSIONS
        // =============================================================================

        public static List<TodoItem> ToUiModels(IEnumerable<TodoAggregate> aggregates)
        {
            return aggregates.Select(ToUiModel).ToList();
        }

        public static List<TodoAggregate> ToAggregates(IEnumerable<TodoItem> uiModels)
        {
            return uiModels.Select(ToAggregate).ToList();
        }
    }
}

