using System;
using System.Collections.Generic;

namespace NoteNest.UI.Plugins.TodoPlugin.Domain.Common
{
    public abstract class AggregateRoot : Entity
    {
        private readonly List<IDomainEvent> _domainEvents = new();
        public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
        
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

