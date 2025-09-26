using System.Threading.Tasks;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Common;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.EventBus
{
    public class InMemoryEventBus : NoteNest.Application.Common.Interfaces.IEventBus
    {
        private readonly IAppLogger _logger;

        public InMemoryEventBus(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task PublishAsync<T>(T domainEvent) where T : IDomainEvent
        {
            // Simple in-memory event publishing
            // In a more complex system, this would dispatch to event handlers
            _logger.Debug($"Published domain event: {typeof(T).Name}");
            
            // For now, just log the event
            await Task.CompletedTask;
        }
    }
}
