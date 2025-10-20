using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Domain.Common;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.EventBus
{
    /// <summary>
    /// Bridges domain events from Clean Architecture (CQRS) to legacy event system for plugin consumption.
    /// This allows plugins to subscribe to domain events through the working EventBus.
    /// </summary>
    public class DomainEventBridge : INotificationHandler<DomainEventNotification>
    {
        private readonly NoteNest.Core.Services.IEventBus _pluginEventBus;
        private readonly IAppLogger _logger;

        public DomainEventBridge(NoteNest.Core.Services.IEventBus pluginEventBus, IAppLogger logger)
        {
            _pluginEventBus = pluginEventBus ?? throw new ArgumentNullException(nameof(pluginEventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task Handle(DomainEventNotification notification, CancellationToken cancellationToken)
        {
            try
            {
                _logger.Info($"[DomainEventBridge] ⚡ RECEIVED notification - Event type: {notification.DomainEvent.GetType().Name}");
                _logger.Debug($"[DomainEventBridge] About to forward to _pluginEventBus.PublishAsync...");
                _logger.Debug($"[DomainEventBridge] Event property type: {notification.DomainEvent.GetType().FullName}");
                
                // Forward domain event to plugin event bus
                await _pluginEventBus.PublishAsync(notification.DomainEvent);
                
                _logger.Info($"[DomainEventBridge] ✅ Bridged domain event to plugins: {notification.DomainEvent.GetType().Name}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[DomainEventBridge] ❌ EXCEPTION bridging domain event: {notification.DomainEvent.GetType().Name}");
                _logger.Error(ex, $"[DomainEventBridge] Exception message: {ex.Message}");
                _logger.Error(ex, $"[DomainEventBridge] Stack trace: {ex.StackTrace}");
                // Don't throw - event bridge failures shouldn't crash the application
            }
        }
    }

    /// <summary>
    /// Wrapper notification for MediatR to dispatch domain events.
    /// Allows domain events to flow through MediatR pipeline to plugins.
    /// </summary>
    public class DomainEventNotification : INotification
    {
        public IDomainEvent DomainEvent { get; }

        public DomainEventNotification(IDomainEvent domainEvent)
        {
            DomainEvent = domainEvent ?? throw new ArgumentNullException(nameof(domainEvent));
        }
    }
}

