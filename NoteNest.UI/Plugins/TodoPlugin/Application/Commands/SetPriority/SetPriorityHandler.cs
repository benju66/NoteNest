using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.SetPriority
{
    public class SetPriorityHandler : IRequestHandler<SetPriorityCommand, Result<SetPriorityResult>>
    {
        private readonly ITodoRepository _repository;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public SetPriorityHandler(
            ITodoRepository repository,
            IEventBus eventBus,
            IAppLogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<SetPriorityResult>> Handle(SetPriorityCommand request, CancellationToken cancellationToken)
        {
            try
            {
                var todo = await _repository.GetByIdAsync(request.TodoId);
                if (todo == null)
                    return Result.Fail<SetPriorityResult>("Todo not found");

                var aggregate = todo.ToAggregate();
                
                // Set priority
                aggregate.SetPriority((Domain.Aggregates.Priority)(int)request.Priority);
                
                var updatedTodo = Models.TodoItem.FromAggregate(aggregate);
                
                var success = await _repository.UpdateAsync(updatedTodo);
                if (!success)
                    return Result.Fail<SetPriorityResult>("Failed to update todo in database");
                
                // Publish events
                foreach (var domainEvent in aggregate.DomainEvents)
                {
                    await _eventBus.PublishAsync(domainEvent);
                }
                aggregate.ClearDomainEvents();
                
                return Result.Ok(new SetPriorityResult
                {
                    TodoId = updatedTodo.Id,
                    Priority = request.Priority,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[SetPriorityHandler] Error setting priority");
                return Result.Fail<SetPriorityResult>($"Error setting priority: {ex.Message}");
            }
        }
    }
}

