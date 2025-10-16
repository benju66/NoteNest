using NoteNest.Domain.Common;

namespace NoteNest.Infrastructure.EventStore
{
    /// <summary>
    /// Handles serialization and deserialization of domain events.
    /// </summary>
    public interface IEventSerializer
    {
        /// <summary>
        /// Serialize an event to JSON.
        /// </summary>
        string Serialize(IDomainEvent @event);
        
        /// <summary>
        /// Deserialize an event from JSON with type information.
        /// </summary>
        IDomainEvent Deserialize(string eventType, string eventData);
    }
}

