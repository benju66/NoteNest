using System;
using NoteNest.Domain.Common;
using NoteNest.Domain.Categories;

namespace NoteNest.Domain.Notes.Events
{
    public record NoteCreatedEvent(NoteId NoteId, CategoryId CategoryId, string Title) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record NoteRenamedEvent(NoteId NoteId, string OldTitle, string NewTitle) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record NoteContentUpdatedEvent(NoteId NoteId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record NoteMovedEvent(NoteId NoteId, CategoryId FromCategoryId, CategoryId ToCategoryId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record NoteDeletedEvent(NoteId NoteId, CategoryId CategoryId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record NotePinnedEvent(NoteId NoteId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record NoteUnpinnedEvent(NoteId NoteId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
}
