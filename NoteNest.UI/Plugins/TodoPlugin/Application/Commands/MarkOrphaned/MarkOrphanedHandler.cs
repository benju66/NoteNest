using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.MarkOrphaned
{
    public class MarkOrphanedHandler : IRequestHandler<MarkOrphanedCommand, Result<MarkOrphanedResult>>
    {
        private readonly ITodoRepository _repository;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public MarkOrphanedHandler(
            ITodoRepository repository,
            IEventBus eventBus,
            IAppLogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<MarkOrphanedResult>> Handle(MarkOrphanedCommand request, CancellationToken cancellationToken)
        {
            try
            {
                var todo = await _repository.GetByIdAsync(request.TodoId);
                if (todo == null)
                    return Result.Fail<MarkOrphanedResult>("Todo not found");

                var aggregate = todo.ToAggregate();
                
                // Mark as orphaned
                if (request.IsOrphaned && !aggregate.IsOrphaned)
                {
                    aggregate.MarkAsOrphaned();
                }
                // Note: No method to un-orphan (once orphaned, stays orphaned)
                
                var updatedTodo = Models.TodoItem.FromAggregate(aggregate);
                
                var success = await _repository.UpdateAsync(updatedTodo);
                if (!success)
                    return Result.Fail<MarkOrphanedResult>("Failed to update todo in database");
                
                // Publish events
                foreach (var domainEvent in aggregate.DomainEvents)
                {
                    await _eventBus.PublishAsync(domainEvent);
                }
                aggregate.ClearDomainEvents();
                
                _logger.Info($"[MarkOrphanedHandler] Todo marked as orphaned: {request.TodoId}");
                
                return Result.Ok(new MarkOrphanedResult
                {
                    TodoId = updatedTodo.Id,
                    IsOrphaned = updatedTodo.IsOrphaned,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MarkOrphanedHandler] Error marking todo as orphaned");
                return Result.Fail<MarkOrphanedResult>($"Error marking orphaned: {ex.Message}");
            }
        }
    }
}

