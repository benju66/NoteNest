using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.MoveTodoCategory
{
    public class MoveTodoCategoryHandler : IRequestHandler<MoveTodoCategoryCommand, Result<MoveTodoCategoryResult>>
    {
        private readonly ITodoRepository _repository;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public MoveTodoCategoryHandler(
            ITodoRepository repository,
            IEventBus eventBus,
            IAppLogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<MoveTodoCategoryResult>> Handle(MoveTodoCategoryCommand request, CancellationToken cancellationToken)
        {
            try
            {
                var todo = await _repository.GetByIdAsync(request.TodoId);
                if (todo == null)
                    return Result.Fail<MoveTodoCategoryResult>("Todo not found");

                var oldCategoryId = todo.CategoryId;
                
                // Check if already in target category
                if (oldCategoryId == request.TargetCategoryId)
                {
                    return Result.Ok(new MoveTodoCategoryResult
                    {
                        TodoId = todo.Id,
                        OldCategoryId = oldCategoryId,
                        NewCategoryId = request.TargetCategoryId,
                        Success = true
                    });
                }

                var aggregate = todo.ToAggregate();
                
                // Move to new category (null = uncategorized)
                aggregate.SetCategory(request.TargetCategoryId);
                
                var updatedTodo = Models.TodoItem.FromAggregate(aggregate);
                
                var success = await _repository.UpdateAsync(updatedTodo);
                if (!success)
                    return Result.Fail<MoveTodoCategoryResult>("Failed to update todo in database");
                
                // Publish events
                foreach (var domainEvent in aggregate.DomainEvents)
                {
                    await _eventBus.PublishAsync(domainEvent);
                }
                aggregate.ClearDomainEvents();
                
                _logger.Info($"[MoveTodoCategoryHandler] Moved todo {request.TodoId} from {oldCategoryId} to {request.TargetCategoryId}");
                
                return Result.Ok(new MoveTodoCategoryResult
                {
                    TodoId = updatedTodo.Id,
                    OldCategoryId = oldCategoryId,
                    NewCategoryId = updatedTodo.CategoryId,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MoveTodoCategoryHandler] Error moving todo to category");
                return Result.Fail<MoveTodoCategoryResult>($"Error moving todo: {ex.Message}");
            }
        }
    }
}

