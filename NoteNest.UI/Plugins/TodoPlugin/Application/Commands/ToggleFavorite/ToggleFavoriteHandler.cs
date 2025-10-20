using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.ToggleFavorite
{
    public class ToggleFavoriteHandler : IRequestHandler<ToggleFavoriteCommand, Result<ToggleFavoriteResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly NoteNest.Application.Common.Interfaces.IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public ToggleFavoriteHandler(
            IEventStore eventStore,
            NoteNest.Application.Common.Interfaces.IEventBus eventBus,
            IAppLogger logger)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<ToggleFavoriteResult>> Handle(ToggleFavoriteCommand request, CancellationToken cancellationToken)
        {
            try
            {
                // Load from event store
                var aggregate = await _eventStore.LoadAsync<TodoAggregate>(request.TodoId);
                if (aggregate == null)
                    return Result.Fail<ToggleFavoriteResult>("Todo not found");
                
                // Toggle favorite if needed
                if (aggregate.IsFavorite != request.IsFavorite)
                {
                    aggregate.ToggleFavorite();
                }
                
                // Capture events BEFORE SaveAsync (SaveAsync clears DomainEvents)
                var events = new List<IDomainEvent>(aggregate.DomainEvents);
                
                // Save to event store
                await _eventStore.SaveAsync(aggregate);
                
                _logger.Info($"[ToggleFavoriteHandler] ✅ Favorite toggled: {request.TodoId}");
                
                // Publish captured events for real-time UI updates
                foreach (var domainEvent in events)
                {
                    await _eventBus.PublishAsync(domainEvent);
                    _logger.Debug($"[ToggleFavoriteHandler] Published event: {domainEvent.GetType().Name}");
                }
                
                return Result.Ok(new ToggleFavoriteResult
                {
                    TodoId = request.TodoId,
                    IsFavorite = aggregate.IsFavorite,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[ToggleFavoriteHandler] Error toggling favorite");
                return Result.Fail<ToggleFavoriteResult>($"Error toggling favorite: {ex.Message}");
            }
        }
    }
}
