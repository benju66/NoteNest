using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Common.Interfaces
{
    /// <summary>
    /// Event store for persisting domain events.
    /// Core of the event sourcing implementation.
    /// </summary>
    public interface IEventStore
    {
        /// <summary>
        /// Save aggregate's uncommitted events to the event store.
        /// </summary>
        Task SaveAsync(AggregateRoot aggregate);
        
        /// <summary>
        /// Save aggregate with expected version for optimistic concurrency.
        /// </summary>
        Task SaveAsync(AggregateRoot aggregate, int expectedVersion);
        
        /// <summary>
        /// Load aggregate from event stream.
        /// </summary>
        Task<T> LoadAsync<T>(Guid aggregateId) where T : AggregateRoot, new();
        
        /// <summary>
        /// Get all events for an aggregate.
        /// </summary>
        Task<IReadOnlyList<IDomainEvent>> GetEventsAsync(Guid aggregateId);
        
        /// <summary>
        /// Get events for an aggregate since a specific version.
        /// </summary>
        Task<IReadOnlyList<IDomainEvent>> GetEventsSinceAsync(Guid aggregateId, int fromVersion);
        
        /// <summary>
        /// Get all events globally (for rebuilding projections).
        /// </summary>
        Task<IReadOnlyList<StoredEvent>> GetAllEventsAsync();
        
        /// <summary>
        /// Get events since a specific stream position (for projection catch-up).
        /// </summary>
        Task<IReadOnlyList<StoredEvent>> GetEventsSincePositionAsync(long fromPosition, int batchSize = 1000);
        
        /// <summary>
        /// Create a snapshot for performance.
        /// </summary>
        Task SaveSnapshotAsync(AggregateRoot aggregate);
        
        /// <summary>
        /// Load snapshot if available.
        /// </summary>
        Task<AggregateSnapshot> LoadSnapshotAsync(Guid aggregateId);
        
        /// <summary>
        /// Get current stream position.
        /// </summary>
        Task<long> GetCurrentStreamPositionAsync();
    }
    
    /// <summary>
    /// Stored event with metadata.
    /// </summary>
    public class StoredEvent
    {
        public long EventId { get; set; }
        public Guid AggregateId { get; set; }
        public string AggregateType { get; set; }
        public string EventType { get; set; }
        public string EventData { get; set; }
        public string Metadata { get; set; }
        public int SequenceNumber { get; set; }
        public long StreamPosition { get; set; }
        public DateTime CreatedAt { get; set; }
        
        public IDomainEvent DeserializeEvent()
        {
            // Will be implemented in the store
            return null;
        }
    }
    
    /// <summary>
    /// Aggregate snapshot for performance.
    /// </summary>
    public class AggregateSnapshot
    {
        public Guid AggregateId { get; set; }
        public string AggregateType { get; set; }
        public int Version { get; set; }
        public string State { get; set; }
        public DateTime CreatedAt { get; set; }
    }
    
    /// <summary>
    /// Exception thrown when there's a concurrency conflict.
    /// </summary>
    public class ConcurrencyException : Exception
    {
        public Guid AggregateId { get; }
        public int ExpectedVersion { get; }
        public int ActualVersion { get; }
        
        public ConcurrencyException(Guid aggregateId, int expectedVersion, int actualVersion)
            : base($"Concurrency conflict for aggregate {aggregateId}. Expected version {expectedVersion}, but actual version is {actualVersion}.")
        {
            AggregateId = aggregateId;
            ExpectedVersion = expectedVersion;
            ActualVersion = actualVersion;
        }
    }
}

