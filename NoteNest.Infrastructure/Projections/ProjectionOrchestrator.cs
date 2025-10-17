using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Application.Projections;
using NoteNest.Domain.Common;

namespace NoteNest.Infrastructure.Projections
{
    /// <summary>
    /// Orchestrates projection updates from event store.
    /// Handles catch-up, rebuilds, and real-time updates.
    /// </summary>
    public class ProjectionOrchestrator
    {
        private readonly IEventStore _eventStore;
        private readonly IEnumerable<IProjection> _projections;
        private readonly Infrastructure.EventStore.IEventSerializer _serializer;
        private readonly IAppLogger _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private bool _isRunning;
        
        public ProjectionOrchestrator(
            IEventStore eventStore,
            IEnumerable<IProjection> projections,
            Infrastructure.EventStore.IEventSerializer serializer,
            IAppLogger logger)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _projections = projections ?? throw new ArgumentNullException(nameof(projections));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Rebuild all projections from scratch.
        /// </summary>
        public async Task RebuildAllAsync()
        {
            _logger.Info("Starting rebuild of all projections...");
            var startTime = DateTime.UtcNow;
            
            // Clear all projections and reset checkpoints (needs lock)
            await _lock.WaitAsync();
            try
            {
                foreach (var projection in _projections)
                {
                    _logger.Info($"Clearing projection: {projection.Name}");
                    await projection.RebuildAsync();
                }
            }
            finally
            {
                _lock.Release();
            }
            
            // Process all events to populate projections (CatchUpAsync has its own lock)
            _logger.Info("Processing all events to populate projections...");
            await CatchUpAsync();
            
            var elapsed = DateTime.UtcNow - startTime;
            _logger.Info($"Rebuilt all projections in {elapsed.TotalSeconds:F2} seconds");
        }
        
        /// <summary>
        /// Rebuild a specific projection.
        /// </summary>
        public async Task RebuildAsync(string projectionName)
        {
            await _lock.WaitAsync();
            try
            {
                var projection = _projections.FirstOrDefault(p => p.Name == projectionName);
                if (projection == null)
                {
                    throw new InvalidOperationException($"Projection '{projectionName}' not found");
                }
                
                _logger.Info($"Rebuilding projection: {projectionName}");
                var startTime = DateTime.UtcNow;
                
                await projection.RebuildAsync();
                
                var elapsed = DateTime.UtcNow - startTime;
                _logger.Info($"Rebuilt projection {projectionName} in {elapsed.TotalSeconds:F2} seconds");
            }
            finally
            {
                _lock.Release();
            }
        }
        
        /// <summary>
        /// Catch up all projections to current event stream.
        /// Processes events they haven't seen yet.
        /// </summary>
        public async Task CatchUpAsync()
        {
            await _lock.WaitAsync();
            try
            {
                _logger.Info("Starting projection catch-up...");
                var totalProcessed = 0;
                
                foreach (var projection in _projections)
                {
                    var processed = await CatchUpProjectionAsync(projection);
                    totalProcessed += processed;
                }
                
                _logger.Info($"Catch-up complete. Processed {totalProcessed} events across {_projections.Count()} projections");
            }
            finally
            {
                _lock.Release();
            }
        }
        
        /// <summary>
        /// Catch up a specific projection.
        /// </summary>
        private async Task<int> CatchUpProjectionAsync(IProjection projection)
        {
            var lastProcessed = await projection.GetLastProcessedPositionAsync();
            var currentPosition = await _eventStore.GetCurrentStreamPositionAsync();
            
            if (lastProcessed >= currentPosition)
            {
                _logger.Debug($"Projection {projection.Name} is up to date at position {lastProcessed}");
                return 0;
            }
            
            _logger.Info($"Projection {projection.Name} catching up from {lastProcessed} to {currentPosition}");
            
            var processed = 0;
            var batchSize = 1000;
            var fromPosition = lastProcessed;
            
            while (fromPosition < currentPosition)
            {
                var events = await _eventStore.GetEventsSincePositionAsync(fromPosition, batchSize);
                
                if (!events.Any())
                    break;
                
                foreach (var storedEvent in events)
                {
                    var @event = DeserializeEvent(storedEvent);
                    if (@event != null)
                    {
                        await projection.HandleAsync(@event);
                        fromPosition = storedEvent.StreamPosition;
                        processed++;
                    }
                }
                
                // Update checkpoint after batch
                await projection.SetLastProcessedPositionAsync(fromPosition);
                
                _logger.Debug($"Projection {projection.Name} processed batch: {events.Count} events (position {fromPosition})");
            }
            
            _logger.Info($"Projection {projection.Name} caught up: {processed} events processed");
            return processed;
        }
        
        /// <summary>
        /// Start continuous catch-up mode.
        /// Polls event store and updates projections in real-time.
        /// </summary>
        public async Task StartContinuousCatchUpAsync(CancellationToken cancellationToken = default)
        {
            if (_isRunning)
            {
                _logger.Warning("Projection orchestrator is already running");
                return;
            }
            
            _isRunning = true;
            _logger.Info("Starting continuous projection catch-up...");
            
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await CatchUpAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("Error during projection catch-up", ex);
                    }
                    
                    // Poll every second for new events
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Info("Projection catch-up cancelled");
            }
            finally
            {
                _isRunning = false;
            }
        }
        
        /// <summary>
        /// Stop continuous catch-up.
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            _logger.Info("Projection orchestrator stopped");
        }
        
        /// <summary>
        /// Get projection status for all projections.
        /// </summary>
        public async Task<List<ProjectionStatus>> GetStatusAsync()
        {
            var status = new List<ProjectionStatus>();
            
            foreach (var projection in _projections)
            {
                var lastProcessed = await projection.GetLastProcessedPositionAsync();
                var currentPosition = await _eventStore.GetCurrentStreamPositionAsync();
                var lag = currentPosition - lastProcessed;
                
                status.Add(new ProjectionStatus
                {
                    Name = projection.Name,
                    LastProcessedPosition = lastProcessed,
                    CurrentStreamPosition = currentPosition,
                    Lag = lag,
                    IsUpToDate = lag == 0
                });
            }
            
            return status;
        }
        
        private IDomainEvent DeserializeEvent(StoredEvent storedEvent)
        {
            try
            {
                return _serializer.Deserialize(storedEvent.EventType, storedEvent.EventData);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to deserialize event {storedEvent.EventType} at position {storedEvent.StreamPosition}", ex);
                return null;  // Skip bad events, log error (resilient approach)
            }
        }
    }
    
    /// <summary>
    /// Status of a projection.
    /// </summary>
    public class ProjectionStatus
    {
        public string Name { get; set; }
        public long LastProcessedPosition { get; set; }
        public long CurrentStreamPosition { get; set; }
        public long Lag { get; set; }
        public bool IsUpToDate { get; set; }
    }
}

