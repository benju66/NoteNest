using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates;
using NoteNest.UI.Plugins.TodoPlugin.Services;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.MoveTodoCategory
{
    public class MoveTodoCategoryHandler : IRequestHandler<MoveTodoCategoryCommand, Result<MoveTodoCategoryResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly ITagInheritanceService _tagInheritanceService;
        private readonly NoteNest.Application.Common.Interfaces.IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public MoveTodoCategoryHandler(
            IEventStore eventStore,
            ITagInheritanceService tagInheritanceService,
            NoteNest.Application.Common.Interfaces.IEventBus eventBus,
            IAppLogger logger)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _tagInheritanceService = tagInheritanceService ?? throw new ArgumentNullException(nameof(tagInheritanceService));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<MoveTodoCategoryResult>> Handle(MoveTodoCategoryCommand request, CancellationToken cancellationToken)
        {
            try
            {
                // Load from event store
                var aggregate = await _eventStore.LoadAsync<TodoAggregate>(request.TodoId);
                if (aggregate == null)
                    return Result.Fail<MoveTodoCategoryResult>("Todo not found");

                var oldCategoryId = aggregate.CategoryId;
                
                // Check if already in target category
                if (oldCategoryId == request.TargetCategoryId)
                {
                    return Result.Ok(new MoveTodoCategoryResult
                    {
                        TodoId = request.TodoId,
                        OldCategoryId = oldCategoryId,
                        NewCategoryId = request.TargetCategoryId,
                        Success = true
                    });
                }
                
                // Move to new category (domain logic)
                aggregate.SetCategory(request.TargetCategoryId);
                
                // Capture events BEFORE SaveAsync (SaveAsync clears DomainEvents)
                var events = new List<IDomainEvent>(aggregate.DomainEvents);
                
                // Save to event store
                await _eventStore.SaveAsync(aggregate);
                
                _logger.Info($"[MoveTodoCategoryHandler] Moved todo {request.TodoId} from {oldCategoryId} to {request.TargetCategoryId}");
                
                // Publish captured events for real-time UI updates
                foreach (var domainEvent in events)
                {
                    await _eventBus.PublishAsync(domainEvent);
                    _logger.Debug($"[MoveTodoCategoryHandler] Published event: {domainEvent.GetType().Name}");
                }
                
                // Update inherited tags based on new location
                // TODO: This will be event-driven in future (CategoryMoved event triggers tag recalculation)
                await _tagInheritanceService.UpdateTodoTagsAsync(request.TodoId, oldCategoryId, request.TargetCategoryId);
                
                return Result.Ok(new MoveTodoCategoryResult
                {
                    TodoId = request.TodoId,
                    OldCategoryId = oldCategoryId,
                    NewCategoryId = request.TargetCategoryId,
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
