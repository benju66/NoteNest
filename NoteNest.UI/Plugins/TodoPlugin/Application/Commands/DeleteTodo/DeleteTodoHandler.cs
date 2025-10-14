using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Events;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.DeleteTodo
{
    public class DeleteTodoHandler : IRequestHandler<DeleteTodoCommand, Result<DeleteTodoResult>>
    {
        private readonly ITodoRepository _repository;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public DeleteTodoHandler(
            ITodoRepository repository,
            IEventBus eventBus,
            IAppLogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<DeleteTodoResult>> Handle(DeleteTodoCommand request, CancellationToken cancellationToken)
        {
            try
            {
                // Verify todo exists before deleting
                var todo = await _repository.GetByIdAsync(request.TodoId);
                if (todo == null)
                    return Result.Fail<DeleteTodoResult>("Todo not found");

                // Delete from database
                var success = await _repository.DeleteAsync(request.TodoId);
                
                if (!success)
                    return Result.Fail<DeleteTodoResult>("Failed to delete todo from database");
                
                // Publish deletion event (TodoStore will remove from UI)
                // ✨ CRITICAL: Cast to IDomainEvent to match TodoStore subscription
                var deletedEvent = new TodoDeletedEvent(Domain.ValueObjects.TodoId.From(request.TodoId));
                await _eventBus.PublishAsync<Domain.Common.IDomainEvent>(deletedEvent);
                
                _logger.Info($"[DeleteTodoHandler] ✅ Todo deleted: {request.TodoId}");
                
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

