using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Common.Interfaces;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.SetDueDate
{
    public class SetDueDateHandler : IRequestHandler<SetDueDateCommand, Result<SetDueDateResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly IAppLogger _logger;

        public SetDueDateHandler(
            IEventStore eventStore,
            IAppLogger logger)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<SetDueDateResult>> Handle(SetDueDateCommand request, CancellationToken cancellationToken)
        {
            try
            {
                // Load from event store
                var aggregate = await _eventStore.LoadAsync<TodoAggregate>(request.TodoId);
                if (aggregate == null)
                    return Result.Fail<SetDueDateResult>("Todo not found");
                
                // Set due date (domain logic)
                var setDueDateResult = aggregate.SetDueDate(request.DueDate);
                if (setDueDateResult.IsFailure)
                    return Result.Fail<SetDueDateResult>(setDueDateResult.Error);
                
                // Save to event store
                await _eventStore.SaveAsync(aggregate);
                
                return Result.Ok(new SetDueDateResult
                {
                    TodoId = request.TodoId,
                    DueDate = request.DueDate,
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

