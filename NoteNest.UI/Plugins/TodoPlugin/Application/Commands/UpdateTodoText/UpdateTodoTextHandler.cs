using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.UpdateTodoText
{
    public class UpdateTodoTextHandler : IRequestHandler<UpdateTodoTextCommand, Result<UpdateTodoTextResult>>
    {
        private readonly ITodoRepository _repository;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public UpdateTodoTextHandler(
            ITodoRepository repository,
            IEventBus eventBus,
            IAppLogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<UpdateTodoTextResult>> Handle(UpdateTodoTextCommand request, CancellationToken cancellationToken)
        {
            try
            {
                var todo = await _repository.GetByIdAsync(request.TodoId);
                if (todo == null)
                    return Result.Fail<UpdateTodoTextResult>("Todo not found");

                var aggregate = todo.ToAggregate();
                
                // Update text
                var updateResult = aggregate.UpdateText(request.NewText);
                if (updateResult.IsFailure)
                    return Result.Fail<UpdateTodoTextResult>(updateResult.Error);
                
                var updatedTodo = Models.TodoItem.FromAggregate(aggregate);
                
                var success = await _repository.UpdateAsync(updatedTodo);
                if (!success)
                    return Result.Fail<UpdateTodoTextResult>("Failed to update todo in database");
                
                // Publish events
                foreach (var domainEvent in aggregate.DomainEvents)
                {
                    await _eventBus.PublishAsync(domainEvent);
                }
                aggregate.ClearDomainEvents();
                
                return Result.Ok(new UpdateTodoTextResult
                {
                    TodoId = updatedTodo.Id,
                    NewText = updatedTodo.Text,
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

