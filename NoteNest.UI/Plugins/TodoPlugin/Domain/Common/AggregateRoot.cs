using System;
using System.Collections.Generic;
using System.Linq;

namespace NoteNest.UI.Plugins.TodoPlugin.Domain.Common
{
    /// <summary>
    /// TodoPlugin's AggregateRoot - implements shared IAggregateRoot interface.
    /// This allows TodoAggregate to use the main IEventStore while maintaining plugin isolation.
    /// </summary>
    public abstract class AggregateRoot : Entity, NoteNest.Domain.Common.IAggregateRoot
    {
        private readonly List<IDomainEvent> _domainEvents = new();
        public IReadOnlyList<NoteNest.Domain.Common.IDomainEvent> DomainEvents => _domainEvents.Cast<NoteNest.Domain.Common.IDomainEvent>().ToList().AsReadOnly();
        
        // Version for optimistic concurrency
        public int Version { get; protected set; }
        
        // Aggregate ID (required for event sourcing)
        public abstract Guid Id { get; }

        protected void AddDomainEvent(IDomainEvent domainEvent)
        {
            _domainEvents.Add(domainEvent);
        }

        public void ClearDomainEvents()
        {
            _domainEvents.Clear();
        }
        
        /// <summary>
        /// Mark events as committed to event store.
        /// </summary>
        public void MarkEventsAsCommitted()
        {
            Version += _domainEvents.Count;
            _domainEvents.Clear();
        }
        
        /// <summary>
        /// Apply an event to rebuild aggregate state.
        /// Implemented by each aggregate to handle its specific events.
        /// </summary>
        public abstract void Apply(IDomainEvent @event);
        
        /// <summary>
        /// Apply shared interface method - delegates to local Apply.
        /// </summary>
        void NoteNest.Domain.Common.IAggregateRoot.Apply(NoteNest.Domain.Common.IDomainEvent @event)
        {
            // Cast to local IDomainEvent and apply
            if (@event is IDomainEvent localEvent)
            {
                Apply(localEvent);
            }
        }
    }

    public abstract class Entity
    {
        public DateTime CreatedAt { get; protected set; }
        public DateTime UpdatedAt { get; protected set; }
    }

    public interface IDomainEvent
    {
        DateTime OccurredAt { get; }
    }
}

