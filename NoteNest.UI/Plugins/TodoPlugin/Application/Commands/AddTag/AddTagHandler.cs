using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.AddTag
{
    public class AddTagHandler : IRequestHandler<AddTagCommand, Result<AddTagResult>>
    {
        private readonly ITodoTagRepository _todoTagRepository;
        private readonly IGlobalTagRepository _globalTagRepository;
        private readonly ITodoRepository _todoRepository;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public AddTagHandler(
            ITodoTagRepository todoTagRepository,
            IGlobalTagRepository globalTagRepository,
            ITodoRepository todoRepository,
            IEventBus eventBus,
            IAppLogger logger)
        {
            _todoTagRepository = todoTagRepository ?? throw new ArgumentNullException(nameof(todoTagRepository));
            _globalTagRepository = globalTagRepository ?? throw new ArgumentNullException(nameof(globalTagRepository));
            _todoRepository = todoRepository ?? throw new ArgumentNullException(nameof(todoRepository));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<AddTagResult>> Handle(AddTagCommand request, CancellationToken cancellationToken)
        {
            try
            {
                _logger.Info($"[AddTagHandler] Adding tag '{request.TagName}' to todo {request.TodoId}");

                // Verify todo exists
                var todo = await _todoRepository.GetByIdAsync(request.TodoId);
                if (todo == null)
                    return Result.Fail<AddTagResult>("Todo not found");

                // Check if tag already exists
                var exists = await _todoTagRepository.ExistsAsync(request.TodoId, request.TagName);
                if (exists)
                    return Result.Fail<AddTagResult>($"Tag '{request.TagName}' already exists on this todo");

                // Add tag
                var todoTag = new TodoTag
                {
                    TodoId = request.TodoId,
                    Tag = request.TagName,
                    IsAuto = false,  // Manual tag!
                    CreatedAt = DateTime.UtcNow
                };

                await _todoTagRepository.AddAsync(todoTag);
                _logger.Info($"[AddTagHandler] ✅ Tag '{request.TagName}' added to todo");

                // Update global_tags usage count
                await _globalTagRepository.IncrementUsageAsync(request.TagName);

                // Publish domain event (for UI updates)
                // Note: We can add TodoTagAddedEvent if needed
                // For now, relying on repository to trigger any necessary updates

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

