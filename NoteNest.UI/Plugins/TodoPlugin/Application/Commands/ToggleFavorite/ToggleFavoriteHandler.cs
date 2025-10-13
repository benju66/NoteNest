using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.ToggleFavorite
{
    public class ToggleFavoriteHandler : IRequestHandler<ToggleFavoriteCommand, Result<ToggleFavoriteResult>>
    {
        private readonly ITodoRepository _repository;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public ToggleFavoriteHandler(
            ITodoRepository repository,
            IEventBus eventBus,
            IAppLogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<ToggleFavoriteResult>> Handle(ToggleFavoriteCommand request, CancellationToken cancellationToken)
        {
            try
            {
                var todo = await _repository.GetByIdAsync(request.TodoId);
                if (todo == null)
                    return Result.Fail<ToggleFavoriteResult>("Todo not found");

                var aggregate = todo.ToAggregate();
                
                // Set favorite state
                // If already in desired state, no-op
                if (aggregate.IsFavorite != request.IsFavorite)
                {
                    aggregate.ToggleFavorite();
                }
                
                var updatedTodo = Models.TodoItem.FromAggregate(aggregate);
                
                var success = await _repository.UpdateAsync(updatedTodo);
                if (!success)
                    return Result.Fail<ToggleFavoriteResult>("Failed to update todo in database");
                
                // Publish events
                foreach (var domainEvent in aggregate.DomainEvents)
                {
                    await _eventBus.PublishAsync(domainEvent);
                }
                aggregate.ClearDomainEvents();
                
                return Result.Ok(new ToggleFavoriteResult
                {
                    TodoId = updatedTodo.Id,
                    IsFavorite = updatedTodo.IsFavorite,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[ToggleFavoriteHandler] Error toggling favorite");
                return Result.Fail<ToggleFavoriteResult>($"Error toggling favorite: {ex.Message}");
            }
        }
    }
}

