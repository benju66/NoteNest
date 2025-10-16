using System;
using NoteNest.Domain.Common;

namespace NoteNest.Domain.Categories.Events
{
    /// <summary>
    /// Domain events for category lifecycle.
    /// </summary>
    
    public record CategoryCreated(Guid CategoryId, Guid? ParentId, string Name, string Path) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
    
    public record CategoryRenamed(Guid CategoryId, string OldName, string NewName, string OldPath, string NewPath) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
    
    public record CategoryMoved(Guid CategoryId, Guid? OldParentId, Guid? NewParentId, string OldPath, string NewPath) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
    
    public record CategoryDeleted(Guid CategoryId, string Name, string Path) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
    
    public record CategoryPinned(Guid CategoryId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
    
    public record CategoryUnpinned(Guid CategoryId) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
}

