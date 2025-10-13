using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.CompleteTodo
{
    public class CompleteTodoHandler : IRequestHandler<CompleteTodoCommand, Result<CompleteTodoResult>>
    {
        private readonly ITodoRepository _repository;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public CompleteTodoHandler(
            ITodoRepository repository,
            IEventBus eventBus,
            IAppLogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<CompleteTodoResult>> Handle(CompleteTodoCommand request, CancellationToken cancellationToken)
        {
            try
            {
                // Get existing todo
                var todo = await _repository.GetByIdAsync(request.TodoId);
                if (todo == null)
                    return Result.Fail<CompleteTodoResult>("Todo not found");

                // Convert to aggregate
                var aggregate = todo.ToAggregate();
                
                // Toggle completion
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
                
                // Convert back to UI model
                var updatedTodo = Models.TodoItem.FromAggregate(aggregate);
                
                // Persist
                var success = await _repository.UpdateAsync(updatedTodo);
                
                if (!success)
                    return Result.Fail<CompleteTodoResult>("Failed to update todo in database");
                
                // Publish domain events
                foreach (var domainEvent in aggregate.DomainEvents)
                {
                    await _eventBus.PublishAsync(domainEvent);
                }
                
                aggregate.ClearDomainEvents();
                
                return Result.Ok(new CompleteTodoResult
                {
                    TodoId = updatedTodo.Id,
                    IsCompleted = updatedTodo.IsCompleted,
                    CompletedDate = updatedTodo.CompletedDate,
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

