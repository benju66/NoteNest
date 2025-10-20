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
        private readonly IAppLogger _logger;

        public ProjectionSyncBehavior(
            ProjectionOrchestrator projectionOrchestrator,
            ITreeQueryService treeQueryService,
            IAppLogger logger)
        {
            _projectionOrchestrator = projectionOrchestrator;
            _treeQueryService = treeQueryService;
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
}

