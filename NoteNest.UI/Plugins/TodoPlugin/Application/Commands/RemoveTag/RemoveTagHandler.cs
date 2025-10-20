using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates;
using NoteNest.Domain.Tags.Events;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.RemoveTag
{
    public class RemoveTagHandler : IRequestHandler<RemoveTagCommand, Result<RemoveTagResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly NoteNest.Application.Common.Interfaces.IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public RemoveTagHandler(
            IEventStore eventStore,
            NoteNest.Application.Common.Interfaces.IEventBus eventBus,
            IAppLogger logger)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<RemoveTagResult>> Handle(RemoveTagCommand request, CancellationToken cancellationToken)
        {
            try
            {
                _logger.Info($"[RemoveTagHandler] Removing tag '{request.TagName}' from todo {request.TodoId}");

                // Load aggregate
                var aggregate = await _eventStore.LoadAsync<TodoAggregate>(request.TodoId);
                if (aggregate == null)
                    return Result.Fail<RemoveTagResult>("Todo not found");

                // Check if tag exists
                if (!aggregate.Tags.Any(t => StringComparer.OrdinalIgnoreCase.Equals(t, request.TagName)))
                    return Result.Fail<RemoveTagResult>($"Tag '{request.TagName}' not found on this todo");

                // Remove tag (domain logic)
                aggregate.RemoveTag(request.TagName);
                
                // Capture events BEFORE SaveAsync (SaveAsync clears DomainEvents)
                var events = new List<IDomainEvent>(aggregate.DomainEvents);
                
                // Save to event store (RemoveTag modifies the aggregate)
                await _eventStore.SaveAsync(aggregate);
                
                _logger.Info($"[RemoveTagHandler] ✅ Tag '{request.TagName}' removed from todo");
                
                // Publish captured events for real-time UI updates
                foreach (var domainEvent in events)
                {
                    await _eventBus.PublishAsync(domainEvent);
                    _logger.Debug($"[RemoveTagHandler] Published event: {domainEvent.GetType().Name}");
                }

                return Result.Ok(new RemoveTagResult
                {
                    TodoId = request.TodoId,
                    TagName = request.TagName,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[RemoveTagHandler] Error removing tag");
                return Result.Fail<RemoveTagResult>($"Error removing tag: {ex.Message}");
            }
        }
    }
}
