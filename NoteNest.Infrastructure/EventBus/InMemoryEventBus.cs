using System.Threading.Tasks;
using MediatR;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Common;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.EventBus
{
    /// <summary>
    /// Event bus implementation that bridges domain events to both MediatR notifications and legacy event system.
    /// Enables plugins to subscribe to domain events while maintaining Clean Architecture principles.
    /// </summary>
    public class InMemoryEventBus : NoteNest.Application.Common.Interfaces.IEventBus
    {
        private readonly IMediator _mediator;
        private readonly IAppLogger _logger;

        public InMemoryEventBus(IMediator mediator, IAppLogger logger)
        {
            _mediator = mediator ?? throw new System.ArgumentNullException(nameof(mediator));
            _logger = logger;
        }

        public async Task PublishAsync<T>(T domainEvent) where T : IDomainEvent
        {
            try
            {
                // Publish to MediatR notification pipeline
                // This flows through to DomainEventBridge → Plugin EventBus
                var notification = new Infrastructure.EventBus.DomainEventNotification(domainEvent);
                await _mediator.Publish(notification);
                
                _logger.Debug($"Published domain event: {typeof(T).Name}");
            }
            catch (System.Exception ex)
            {
                _logger.Error(ex, $"Failed to publish domain event: {typeof(T).Name}");
                // Don't throw - event publishing failures shouldn't crash CQRS handlers
            }
        }
    }
}
