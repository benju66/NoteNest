using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.UpdateTodoText
{
    public class UpdateTodoTextHandler : IRequestHandler<UpdateTodoTextCommand, Result<UpdateTodoTextResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly NoteNest.Application.Common.Interfaces.IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public UpdateTodoTextHandler(
            IEventStore eventStore,
            NoteNest.Application.Common.Interfaces.IEventBus eventBus,
            IAppLogger logger)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<UpdateTodoTextResult>> Handle(UpdateTodoTextCommand request, CancellationToken cancellationToken)
        {
            try
            {
                // Load from event store
                var aggregate = await _eventStore.LoadAsync<TodoAggregate>(request.TodoId);
                if (aggregate == null)
                    return Result.Fail<UpdateTodoTextResult>("Todo not found");
                
                // Update text (domain logic)
                var updateResult = aggregate.UpdateText(request.NewText);
                if (updateResult.IsFailure)
                    return Result.Fail<UpdateTodoTextResult>(updateResult.Error);
                
                // Capture events BEFORE SaveAsync (SaveAsync clears DomainEvents)
                var events = new List<IDomainEvent>(aggregate.DomainEvents);
                
                // Save to event store
                await _eventStore.SaveAsync(aggregate);
                
                _logger.Info($"[UpdateTodoTextHandler] ✅ Text updated: {request.TodoId}");
                
                // Publish captured events for real-time UI updates
                foreach (var domainEvent in events)
                {
                    await _eventBus.PublishAsync(domainEvent);
                    _logger.Debug($"[UpdateTodoTextHandler] Published event: {domainEvent.GetType().Name}");
                }
                
                return Result.Ok(new UpdateTodoTextResult
                {
                    TodoId = request.TodoId,
                    NewText = request.NewText,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[UpdateTodoTextHandler] Error updating todo text");
                return Result.Fail<UpdateTodoTextResult>($"Error updating todo: {ex.Message}");
            }
        }
    }
}

