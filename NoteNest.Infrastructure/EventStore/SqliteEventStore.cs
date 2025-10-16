using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Application.Common.Interfaces;

namespace NoteNest.Infrastructure.EventStore
{
    /// <summary>
    /// SQLite-based event store implementation.
    /// Provides atomic, transactional event persistence with optimistic concurrency.
    /// </summary>
    public class SqliteEventStore : IEventStore
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;
        private readonly IEventSerializer _serializer;
        
        public SqliteEventStore(
            string connectionString,
            IAppLogger logger,
            IEventSerializer serializer)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }
        
        public async Task SaveAsync(AggregateRoot aggregate)
        {
            await SaveAsync(aggregate, expectedVersion: -1); // No version check
        }
        
        public async Task SaveAsync(AggregateRoot aggregate, int expectedVersion)
        {
            var events = aggregate.DomainEvents;
            if (!events.Any())
            {
                _logger.Debug($"No uncommitted events for aggregate {aggregate.GetType().Name}");
                return;
            }
            
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            
            try
            {
                // Get current version from database
                var currentVersion = await connection.ExecuteScalarAsync<int?>(
                    "SELECT MAX(sequence_number) FROM events WHERE aggregate_id = @AggregateId",
                    new { AggregateId = aggregate.Id.ToString() },
                    transaction) ?? 0;
                
                // Optimistic concurrency check
                if (expectedVersion >= 0 && currentVersion != expectedVersion)
                {
                    throw new ConcurrencyException(aggregate.Id, expectedVersion, currentVersion);
                }
                
                // Get next stream position
                var streamPosition = await connection.ExecuteScalarAsync<long>(
                    "SELECT current_position FROM stream_position WHERE id = 1",
                    transaction: transaction);
                
                // Save each event
                int sequenceNumber = currentVersion;
                foreach (var @event in events)
                {
                    sequenceNumber++;
                    streamPosition++;
                    
                    var eventType = @event.GetType().Name;
                    var aggregateType = aggregate.GetType().Name;
                    var eventData = _serializer.Serialize(@event);
                    var metadata = CreateMetadata(@event);
                    
                    await connection.ExecuteAsync(
                        @"INSERT INTO events (
                            aggregate_id, aggregate_type, event_type, event_data, metadata,
                            sequence_number, stream_position, created_at
                        ) VALUES (
                            @AggregateId, @AggregateType, @EventType, @EventData, @Metadata,
                            @SequenceNumber, @StreamPosition, @CreatedAt
                        )",
                        new
                        {
                            AggregateId = aggregate.Id.ToString(),
                            AggregateType = aggregateType,
                            EventType = eventType,
                            EventData = eventData,
                            Metadata = metadata,
                            SequenceNumber = sequenceNumber,
                            StreamPosition = streamPosition,
                            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                        },
                        transaction);
                    
                    _logger.Debug($"Saved event {eventType} for {aggregateType} {aggregate.Id} at position {streamPosition}");
                }
                
                // Update stream position
                await connection.ExecuteAsync(
                    "UPDATE stream_position SET current_position = @Position WHERE id = 1",
                    new { Position = streamPosition },
                    transaction);
                
                transaction.Commit();
                aggregate.MarkEventsAsCommitted();
                
                _logger.Info($"Saved {events.Count} events for aggregate {aggregate.GetType().Name} {aggregate.Id}");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.Error($"Failed to save events for aggregate {aggregate.Id}", ex);
                throw;
            }
        }
        
        public async Task<T> LoadAsync<T>(Guid aggregateId) where T : AggregateRoot, new()
        {
            // Try to load from snapshot first
            var snapshot = await LoadSnapshotAsync(aggregateId);
            
            T aggregate;
            int fromVersion;
            
            if (snapshot != null)
            {
                // Deserialize from snapshot
                aggregate = JsonSerializer.Deserialize<T>(snapshot.State);
                fromVersion = snapshot.Version + 1;
                _logger.Debug($"Loaded snapshot for {aggregateId} at version {snapshot.Version}");
            }
            else
            {
                aggregate = new T();
                fromVersion = 1;
            }
            
            // Load events since snapshot (or all events if no snapshot)
            var events = await GetEventsSinceAsync(aggregateId, fromVersion);
            
            if (!events.Any() && snapshot == null)
            {
                _logger.Debug($"No events found for aggregate {aggregateId}");
                return null; // Aggregate doesn't exist
            }
            
            // Apply events to rebuild state
            foreach (var @event in events)
            {
                aggregate.Apply(@event);
            }
            
            _logger.Info($"Loaded aggregate {typeof(T).Name} {aggregateId} with {events.Count} events (from version {fromVersion})");
            return aggregate;
        }
        
        public async Task<IReadOnlyList<IDomainEvent>> GetEventsAsync(Guid aggregateId)
        {
            return await GetEventsSinceAsync(aggregateId, fromVersion: 1);
        }
        
        public async Task<IReadOnlyList<IDomainEvent>> GetEventsSinceAsync(Guid aggregateId, int fromVersion)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var storedEvents = await connection.QueryAsync<StoredEventDto>(
                @"SELECT event_id, aggregate_id, aggregate_type, event_type, event_data, 
                         metadata, sequence_number, stream_position, created_at
                  FROM events
                  WHERE aggregate_id = @AggregateId AND sequence_number >= @FromVersion
                  ORDER BY sequence_number",
                new { AggregateId = aggregateId.ToString(), FromVersion = fromVersion });
            
            var events = new List<IDomainEvent>();
            foreach (var storedEvent in storedEvents)
            {
                var @event = _serializer.Deserialize(storedEvent.EventType, storedEvent.EventData);
                events.Add(@event);
            }
            
            return events;
        }
        
        public async Task<IReadOnlyList<StoredEvent>> GetAllEventsAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var storedEvents = await connection.QueryAsync<StoredEventDto>(
                @"SELECT event_id, aggregate_id, aggregate_type, event_type, event_data,
                         metadata, sequence_number, stream_position, created_at
                  FROM events
                  ORDER BY stream_position");
            
            return storedEvents.Select(dto => dto.ToStoredEvent()).ToList();
        }
        
        public async Task<IReadOnlyList<StoredEvent>> GetEventsSincePositionAsync(long fromPosition, int batchSize = 1000)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var storedEvents = await connection.QueryAsync<StoredEventDto>(
                @"SELECT event_id, aggregate_id, aggregate_type, event_type, event_data,
                         metadata, sequence_number, stream_position, created_at
                  FROM events
                  WHERE stream_position > @FromPosition
                  ORDER BY stream_position
                  LIMIT @BatchSize",
                new { FromPosition = fromPosition, BatchSize = batchSize });
            
            return storedEvents.Select(dto => dto.ToStoredEvent()).ToList();
        }
        
        public async Task SaveSnapshotAsync(AggregateRoot aggregate)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var state = JsonSerializer.Serialize(aggregate, aggregate.GetType());
            
            await connection.ExecuteAsync(
                @"INSERT INTO snapshots (aggregate_id, aggregate_type, version, state, created_at)
                  VALUES (@AggregateId, @AggregateType, @Version, @State, @CreatedAt)",
                new
                {
                    AggregateId = aggregate.Id.ToString(),
                    AggregateType = aggregate.GetType().Name,
                    Version = aggregate.Version,
                    State = state,
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
            
            _logger.Info($"Saved snapshot for aggregate {aggregate.GetType().Name} {aggregate.Id} at version {aggregate.Version}");
        }
        
        public async Task<AggregateSnapshot> LoadSnapshotAsync(Guid aggregateId)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var snapshot = await connection.QueryFirstOrDefaultAsync<SnapshotDto>(
                @"SELECT aggregate_id, aggregate_type, version, state, created_at
                  FROM snapshots
                  WHERE aggregate_id = @AggregateId
                  ORDER BY version DESC
                  LIMIT 1",
                new { AggregateId = aggregateId.ToString() });
            
            if (snapshot == null)
                return null;
            
            return new AggregateSnapshot
            {
                AggregateId = Guid.Parse(snapshot.AggregateId),
                AggregateType = snapshot.AggregateType,
                Version = snapshot.Version,
                State = snapshot.State,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(snapshot.CreatedAt).DateTime
            };
        }
        
        public async Task<long> GetCurrentStreamPositionAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            return await connection.ExecuteScalarAsync<long>(
                "SELECT current_position FROM stream_position WHERE id = 1");
        }
        
        private string CreateMetadata(IDomainEvent @event)
        {
            var metadata = new
            {
                Timestamp = DateTimeOffset.UtcNow,
                User = Environment.UserName,
                Machine = Environment.MachineName,
                CorrelationId = Guid.NewGuid() // For distributed tracing
            };
            
            return JsonSerializer.Serialize(metadata);
        }
        
        // DTOs for Dapper
        private class StoredEventDto
        {
            public long EventId { get; set; }
            public string AggregateId { get; set; }
            public string AggregateType { get; set; }
            public string EventType { get; set; }
            public string EventData { get; set; }
            public string Metadata { get; set; }
            public int SequenceNumber { get; set; }
            public long StreamPosition { get; set; }
            public long CreatedAt { get; set; }
            
            public StoredEvent ToStoredEvent()
            {
                return new StoredEvent
                {
                    EventId = EventId,
                    AggregateId = Guid.Parse(AggregateId),
                    AggregateType = AggregateType,
                    EventType = EventType,
                    EventData = EventData,
                    Metadata = Metadata,
                    SequenceNumber = SequenceNumber,
                    StreamPosition = StreamPosition,
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(CreatedAt).DateTime
                };
            }
        }
        
        private class SnapshotDto
        {
            public string AggregateId { get; set; }
            public string AggregateType { get; set; }
            public int Version { get; set; }
            public string State { get; set; }
            public long CreatedAt { get; set; }
        }
    }
}

