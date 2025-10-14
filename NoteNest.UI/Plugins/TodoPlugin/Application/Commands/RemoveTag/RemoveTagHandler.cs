using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.RemoveTag
{
    public class RemoveTagHandler : IRequestHandler<RemoveTagCommand, Result<RemoveTagResult>>
    {
        private readonly ITodoTagRepository _todoTagRepository;
        private readonly IGlobalTagRepository _globalTagRepository;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public RemoveTagHandler(
            ITodoTagRepository todoTagRepository,
            IGlobalTagRepository globalTagRepository,
            IEventBus eventBus,
            IAppLogger logger)
        {
            _todoTagRepository = todoTagRepository ?? throw new ArgumentNullException(nameof(todoTagRepository));
            _globalTagRepository = globalTagRepository ?? throw new ArgumentNullException(nameof(globalTagRepository));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<RemoveTagResult>> Handle(RemoveTagCommand request, CancellationToken cancellationToken)
        {
            try
            {
                _logger.Info($"[RemoveTagHandler] Removing tag '{request.TagName}' from todo {request.TodoId}");

                // Check if tag exists
                var tags = await _todoTagRepository.GetByTodoIdAsync(request.TodoId);
                var tag = tags.FirstOrDefault(t => t.Tag == request.TagName);

                if (tag == null)
                    return Result.Fail<RemoveTagResult>($"Tag '{request.TagName}' not found on this todo");

                // Check if auto-tag (cannot remove auto-generated tags manually)
                if (tag.IsAuto)
                    return Result.Fail<RemoveTagResult>("Cannot remove auto-generated tags. Move todo to change auto-tags.");

                // Remove tag
                await _todoTagRepository.DeleteAsync(request.TodoId, request.TagName);
                _logger.Info($"[RemoveTagHandler] ✅ Tag '{request.TagName}' removed from todo");

                // Update global_tags usage count
                await _globalTagRepository.DecrementUsageAsync(request.TagName);

                // Publish domain event (for UI updates)
                // Note: We can add TodoTagRemovedEvent if needed

                return Result.Ok(new RemoveTagResult
                {
                    TodoId = request.TodoId,
                    TagName = request.TagName,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[RemoveTagHandler] Error removing tag");
                return Result.Fail<RemoveTagResult>($"Error removing tag: {ex.Message}");
            }
        }
    }
}

