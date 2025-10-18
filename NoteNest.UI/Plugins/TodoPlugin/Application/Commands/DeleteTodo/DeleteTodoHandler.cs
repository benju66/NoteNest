using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Events;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates;
using NoteNest.UI.Plugins.TodoPlugin.Domain.ValueObjects;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.DeleteTodo
{
    public class DeleteTodoHandler : IRequestHandler<DeleteTodoCommand, Result<DeleteTodoResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly IAppLogger _logger;

        public DeleteTodoHandler(
            IEventStore eventStore,
            IAppLogger logger)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<DeleteTodoResult>> Handle(DeleteTodoCommand request, CancellationToken cancellationToken)
        {
            try
            {
                // Load aggregate from event store
                var aggregate = await _eventStore.LoadAsync<TodoAggregate>(request.TodoId);
                if (aggregate == null)
                    return Result.Fail<DeleteTodoResult>("Todo not found");
                
                // Delete todo (raises TodoDeletedEvent)
                aggregate.Delete();
                
                // Save to event store (TodoDeletedEvent will be persisted)
                await _eventStore.SaveAsync(aggregate);
                
                _logger.Info($"[DeleteTodoHandler] ✅ Todo deleted via events: {request.TodoId}");
                
                return Result.Ok(new DeleteTodoResult
                {
                    TodoId = request.TodoId,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[DeleteTodoHandler] Error deleting todo");
                return Result.Fail<DeleteTodoResult>($"Error deleting todo: {ex.Message}");
            }
        }
    }
}
