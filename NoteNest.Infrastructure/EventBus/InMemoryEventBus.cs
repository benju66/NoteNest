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
                _logger.Info($"[InMemoryEventBus] ⚡ Publishing event - Compile-time type: {typeof(T).Name}, Runtime type: {domainEvent.GetType().Name}");
                
                // Publish to MediatR notification pipeline
                // This flows through to DomainEventBridge → Plugin EventBus
                var notification = new Infrastructure.EventBus.DomainEventNotification(domainEvent);
                
                _logger.Debug($"[InMemoryEventBus] Created DomainEventNotification, about to call _mediator.Publish...");
                await _mediator.Publish(notification);
                _logger.Debug($"[InMemoryEventBus] _mediator.Publish completed successfully");
                
                _logger.Debug($"Published domain event: {typeof(T).Name}");
            }
            catch (System.Exception ex)
            {
                _logger.Error(ex, $"[InMemoryEventBus] ❌ EXCEPTION publishing domain event: {typeof(T).Name}");
                _logger.Error(ex, $"[InMemoryEventBus] Exception details: {ex.Message}");
                _logger.Error(ex, $"[InMemoryEventBridge] Stack trace: {ex.StackTrace}");
                // Don't throw - event publishing failures shouldn't crash CQRS handlers
            }
        }
    }
}
