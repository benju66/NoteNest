using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Application.Queries;
using NoteNest.Core.Services.Logging;
using NoteNest.Infrastructure.Projections;

namespace NoteNest.Infrastructure.Behaviors
{
    /// <summary>
    /// MediatR pipeline behavior that automatically updates projections after each command.
    /// Ensures read models are immediately consistent with write models.
    /// 
    /// Flow:
    /// 1. Command executes (writes to event store)
    /// 2. This behavior runs
    /// 3. ProjectionOrchestrator.CatchUpAsync() processes new events
    /// 4. Projections updated
    /// 5. Cache invalidated (UI will reload on next query)
    /// 6. UI queries see latest data
    /// 
    /// Performance: Adds ~50-100ms per command (acceptable for interactive operations)
    /// </summary>
    public class ProjectionSyncBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly ProjectionOrchestrator _projectionOrchestrator;
        private readonly ITreeQueryService _treeQueryService;
        private readonly NoteNest.Core.Services.IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public ProjectionSyncBehavior(
            ProjectionOrchestrator projectionOrchestrator,
            ITreeQueryService treeQueryService,
            NoteNest.Core.Services.IEventBus eventBus,
            IAppLogger logger)
        {
            _projectionOrchestrator = projectionOrchestrator;
            _treeQueryService = treeQueryService;
            _eventBus = eventBus;
            _logger = logger;
        }

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            // Execute the command (event gets saved to event store)
            var response = await next();

            // Immediately update projections to process the new event
            // This ensures read models are consistent with write models
            try
            {
                _logger.Debug($"Synchronizing projections after {typeof(TRequest).Name}...");
                
                // Process new events into projections
                await _projectionOrchestrator.CatchUpAsync();
                
                // Invalidate query service cache so UI reloads fresh data
                _treeQueryService.InvalidateCache();
                
                _logger.Info($"✅ Projections synchronized and cache invalidated after {typeof(TRequest).Name}");
                
                // Notify subscribers that projections have been updated (for TodoStore to reload)
                await _eventBus.PublishAsync(new ProjectionsSynchronizedEvent
                {
                    CommandType = typeof(TRequest).Name,
                    SynchronizedAt = System.DateTime.UtcNow
                });
            }
            catch (System.Exception ex)
            {
                // Don't fail the command if projection update fails
                // Projections can catch up later via background service
                _logger.Warning($"⚠️ Projection sync failed after {typeof(TRequest).Name}: {ex.Message}");
            }

            return response;
        }
    }
    
    /// <summary>
    /// Event published when projections have been synchronized.
    /// TodoStore can subscribe to this to reload data.
    /// </summary>
    public class ProjectionsSynchronizedEvent
    {
        public string CommandType { get; set; }
        public System.DateTime SynchronizedAt { get; set; }
    }
}

