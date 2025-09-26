using System;
using System.Collections.Generic;

namespace NoteNest.Domain.Common
{
    public abstract class AggregateRoot : Entity
    {
        private readonly List<IDomainEvent> _domainEvents = new();
        public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

        protected void AddDomainEvent(IDomainEvent domainEvent)
        {
            _domainEvents.Add(domainEvent);
        }

        public void ClearDomainEvents()
        {
            _domainEvents.Clear();
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
