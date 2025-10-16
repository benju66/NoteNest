using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Common.Interfaces;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates;
using NoteNest.Domain.Tags.Events;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.AddTag
{
    public class AddTagHandler : IRequestHandler<AddTagCommand, Result<AddTagResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly IAppLogger _logger;

        public AddTagHandler(
            IEventStore eventStore,
            IAppLogger logger)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<AddTagResult>> Handle(AddTagCommand request, CancellationToken cancellationToken)
        {
            try
            {
                _logger.Info($"[AddTagHandler] Adding tag '{request.TagName}' to todo {request.TodoId}");

                // Load todo aggregate
                var aggregate = await _eventStore.LoadAsync<TodoAggregate>(request.TodoId);
                if (aggregate == null)
                    return Result.Fail<AddTagResult>("Todo not found");

                // Check if tag already exists
                if (aggregate.Tags.Any(t => StringComparer.OrdinalIgnoreCase.Equals(t, request.TagName)))
                    return Result.Fail<AddTagResult>($"Tag '{request.TagName}' already exists on this todo");

                // Add tag (domain logic)
                aggregate.AddTag(request.TagName);
                
                // Save to event store (AddTag modifies the aggregate)
                await _eventStore.SaveAsync(aggregate);
                
                // TODO: Generate TagAddedToEntity event for projection
                // For now, tags stored in TodoAggregate.Tags list
                
                _logger.Info($"[AddTagHandler] ✅ Tag '{request.TagName}' added to todo");

                return Result.Ok(new AddTagResult
                {
                    TodoId = request.TodoId,
                    TagName = request.TagName,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[AddTagHandler] Error adding tag");
                return Result.Fail<AddTagResult>($"Error adding tag: {ex.Message}");
            }
        }
    }
}
