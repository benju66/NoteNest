using System;
using NoteNest.Domain.Common;

namespace NoteNest.Domain.Tags.Events
{
    /// <summary>
    /// Domain events for tag aggregate lifecycle.
    /// </summary>
    
    public record TagCreated(Guid TagId, string Name, string DisplayName) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
    
    public record TagUsageIncremented(Guid TagId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
    
    public record TagUsageDecremented(Guid TagId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
    
    public record TagAddedToEntity(Guid EntityId, string EntityType, string Tag, string DisplayName, string Source) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
    
    public record TagRemovedFromEntity(Guid EntityId, string EntityType, string Tag) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
    
    public record TagCategorySet(Guid TagId, string Category) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
    
    public record TagColorSet(Guid TagId, string Color) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
}

