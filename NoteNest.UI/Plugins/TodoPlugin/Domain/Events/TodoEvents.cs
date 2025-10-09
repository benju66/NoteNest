using System;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Domain.ValueObjects;

namespace NoteNest.UI.Plugins.TodoPlugin.Domain.Events
{
    public record TodoCreatedEvent(TodoId TodoId, string Text, Guid? CategoryId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record TodoCompletedEvent(TodoId TodoId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record TodoUncompletedEvent(TodoId TodoId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record TodoTextUpdatedEvent(TodoId TodoId, string NewText) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record TodoDeletedEvent(TodoId TodoId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record TodoDueDateChangedEvent(TodoId TodoId, DateTime? NewDueDate) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record TodoFavoritedEvent(TodoId TodoId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record TodoUnfavoritedEvent(TodoId TodoId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record TodoPriorityChangedEvent(TodoId TodoId, int NewPriority) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
}

