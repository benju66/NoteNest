using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.MarkOrphaned
{
    public class MarkOrphanedHandler : IRequestHandler<MarkOrphanedCommand, Result<MarkOrphanedResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly NoteNest.Application.Common.Interfaces.IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public MarkOrphanedHandler(
            IEventStore eventStore,
            NoteNest.Application.Common.Interfaces.IEventBus eventBus,
            IAppLogger logger)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<MarkOrphanedResult>> Handle(MarkOrphanedCommand request, CancellationToken cancellationToken)
        {
            try
            {
                // Load from event store
                var aggregate = await _eventStore.LoadAsync<TodoAggregate>(request.TodoId);
                if (aggregate == null)
                    return Result.Fail<MarkOrphanedResult>("Todo not found");
                
                // Mark as orphaned (domain logic)
                if (request.IsOrphaned && !aggregate.IsOrphaned)
                {
                    aggregate.MarkAsOrphaned();
                }
                
                // Capture events BEFORE SaveAsync (SaveAsync clears DomainEvents)
                var events = new List<IDomainEvent>(aggregate.DomainEvents);
                
                // Save to event store
                await _eventStore.SaveAsync(aggregate);
                
                _logger.Info($"[MarkOrphanedHandler] Todo marked as orphaned: {request.TodoId}");
                
                // Publish captured events for real-time UI updates
                foreach (var domainEvent in events)
                {
                    await _eventBus.PublishAsync(domainEvent);
                    _logger.Debug($"[MarkOrphanedHandler] Published event: {domainEvent.GetType().Name}");
                }
                
                return Result.Ok(new MarkOrphanedResult
                {
                    TodoId = request.TodoId,
                    IsOrphaned = aggregate.IsOrphaned,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MarkOrphanedHandler] Error marking todo as orphaned");
                return Result.Fail<MarkOrphanedResult>($"Error marking orphaned: {ex.Message}");
            }
        }
    }
}
