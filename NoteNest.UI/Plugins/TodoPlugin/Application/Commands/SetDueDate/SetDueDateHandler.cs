using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.SetDueDate
{
    public class SetDueDateHandler : IRequestHandler<SetDueDateCommand, Result<SetDueDateResult>>
    {
        private readonly ITodoRepository _repository;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public SetDueDateHandler(
            ITodoRepository repository,
            IEventBus eventBus,
            IAppLogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<SetDueDateResult>> Handle(SetDueDateCommand request, CancellationToken cancellationToken)
        {
            try
            {
                var todo = await _repository.GetByIdAsync(request.TodoId);
                if (todo == null)
                    return Result.Fail<SetDueDateResult>("Todo not found");

                var aggregate = todo.ToAggregate();
                
                // Set due date (null = clear)
                var setDueDateResult = aggregate.SetDueDate(request.DueDate);
                if (setDueDateResult.IsFailure)
                    return Result.Fail<SetDueDateResult>(setDueDateResult.Error);
                
                var updatedTodo = Models.TodoItem.FromAggregate(aggregate);
                
                var success = await _repository.UpdateAsync(updatedTodo);
                if (!success)
                    return Result.Fail<SetDueDateResult>("Failed to update todo in database");
                
                // Publish events
                foreach (var domainEvent in aggregate.DomainEvents)
                {
                    await _eventBus.PublishAsync(domainEvent);
                }
                aggregate.ClearDomainEvents();
                
                return Result.Ok(new SetDueDateResult
                {
                    TodoId = updatedTodo.Id,
                    DueDate = updatedTodo.DueDate,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[SetDueDateHandler] Error setting due date");
                return Result.Fail<SetDueDateResult>($"Error setting due date: {ex.Message}");
            }
        }
    }
}

