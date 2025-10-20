using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.CompleteTodo
{
    public class CompleteTodoHandler : IRequestHandler<CompleteTodoCommand, Result<CompleteTodoResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly NoteNest.Application.Common.Interfaces.IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public CompleteTodoHandler(
            IEventStore eventStore,
            NoteNest.Application.Common.Interfaces.IEventBus eventBus,
            IAppLogger logger)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<CompleteTodoResult>> Handle(CompleteTodoCommand request, CancellationToken cancellationToken)
        {
            try
            {
                // Load aggregate from event store
                var aggregate = await _eventStore.LoadAsync<TodoAggregate>(request.TodoId);
                if (aggregate == null)
                    return Result.Fail<CompleteTodoResult>("Todo not found");
                
                // Toggle completion (domain logic)
                if (request.IsCompleted)
                {
                    var result = aggregate.Complete();
                    if (result.IsFailure)
                        return Result.Fail<CompleteTodoResult>(result.Error);
                }
                else
                {
                    var result = aggregate.Uncomplete();
                    if (result.IsFailure)
                        return Result.Fail<CompleteTodoResult>(result.Error);
                }
                
                // Capture events BEFORE SaveAsync (SaveAsync clears DomainEvents)
                var events = new List<IDomainEvent>(aggregate.DomainEvents);
                
                // Save to event store (persists events, updates projections)
                await _eventStore.SaveAsync(aggregate);
                
                _logger.Info($"[CompleteTodoHandler] ✅ Todo completion toggled: {request.TodoId}");
                
                // Publish captured events for real-time UI updates
                foreach (var domainEvent in events)
                {
                    await _eventBus.PublishAsync(domainEvent);
                    _logger.Debug($"[CompleteTodoHandler] Published event: {domainEvent.GetType().Name}");
                }
                
                return Result.Ok(new CompleteTodoResult
                {
                    TodoId = request.TodoId,
                    IsCompleted = request.IsCompleted,
                    CompletedDate = aggregate.CompletedDate,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[CompleteTodoHandler] Error toggling completion");
                return Result.Fail<CompleteTodoResult>($"Error updating todo: {ex.Message}");
            }
        }
    }
}

