using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.SetPriority
{
    public class SetPriorityHandler : IRequestHandler<SetPriorityCommand, Result<SetPriorityResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly IAppLogger _logger;

        public SetPriorityHandler(
            IEventStore eventStore,
            IAppLogger logger)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<SetPriorityResult>> Handle(SetPriorityCommand request, CancellationToken cancellationToken)
        {
            try
            {
                // Load from event store
                var aggregate = await _eventStore.LoadAsync<TodoAggregate>(request.TodoId);
                if (aggregate == null)
                    return Result.Fail<SetPriorityResult>("Todo not found");
                
                // Set priority (domain logic)
                aggregate.SetPriority((Priority)(int)request.Priority);
                
                // Save to event store
                await _eventStore.SaveAsync(aggregate);
                
                return Result.Ok(new SetPriorityResult
                {
                    TodoId = request.TodoId,
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
