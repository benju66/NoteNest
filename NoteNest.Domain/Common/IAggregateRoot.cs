using System;
using System.Collections.Generic;

namespace NoteNest.Domain.Common
{
    /// <summary>
    /// Interface for all aggregate roots.
    /// Enables IEventStore to work with aggregates from different namespaces (main domain + plugins).
    /// Both NoteNest.Domain.Common.AggregateRoot and TodoPlugin.Domain.Common.AggregateRoot implement this.
    /// </summary>
    public interface IAggregateRoot
    {
        /// <summary>
        /// Unique identifier for the aggregate.
        /// </summary>
        Guid Id { get; }
        
        /// <summary>
        /// Version number for optimistic concurrency control.
        /// </summary>
        int Version { get; }
        
        /// <summary>
        /// Uncommitted domain events raised by the aggregate.
        /// </summary>
        IReadOnlyList<IDomainEvent> DomainEvents { get; }
        
        /// <summary>
        /// Mark domain events as committed to the event store.
        /// Increments version and clears uncommitted events.
        /// </summary>
        void MarkEventsAsCommitted();
        
        /// <summary>
        /// Apply an event to rebuild aggregate state from event stream.
        /// Used when loading aggregate from event store.
        /// </summary>
        void Apply(IDomainEvent @event);
    }
}

