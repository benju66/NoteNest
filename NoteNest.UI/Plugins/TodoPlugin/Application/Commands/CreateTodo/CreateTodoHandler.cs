using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Common.Interfaces;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates;
using NoteNest.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Services;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.CreateTodo
{
    /// <summary>
    /// Handler for creating new todos.
    /// Event sourcing implementation - persists todo creation as events.
    /// Tag inheritance handled via events (TagAddedToEntity).
    /// </summary>
    public class CreateTodoHandler : IRequestHandler<CreateTodoCommand, Result<CreateTodoResult>>
    {
        private readonly IEventStore _eventStore;
        private readonly ITagInheritanceService _tagInheritanceService;
        private readonly IAppLogger _logger;

        public CreateTodoHandler(
            IEventStore eventStore,
            ITagInheritanceService tagInheritanceService,
            IAppLogger logger)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _tagInheritanceService = tagInheritanceService ?? throw new ArgumentNullException(nameof(tagInheritanceService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<CreateTodoResult>> Handle(CreateTodoCommand request, CancellationToken cancellationToken)
        {
            try
            {
                _logger.Info($"[CreateTodoHandler] Creating todo: '{request.Text}'");
                
                // Create domain aggregate
                TodoAggregate aggregate;
                
                if (request.SourceNoteId.HasValue)
                {
                    // Todo from RTF extraction
                    var result = TodoAggregate.CreateFromNote(
                        request.Text,
                        request.SourceNoteId.Value,
                        request.SourceFilePath,
                        request.SourceLineNumber,
                        request.SourceCharOffset);
                    
                    if (result.IsFailure)
                        return Result.Fail<CreateTodoResult>(result.Error);
                    
                    aggregate = result.Value;
                    
                    // Set category if note is in a category
                    if (request.CategoryId.HasValue)
                    {
                        aggregate.SetCategory(request.CategoryId.Value);
                    }
                }
                else
                {
                    // Manual todo creation
                    var result = TodoAggregate.Create(request.Text, request.CategoryId);
                    
                    if (result.IsFailure)
                        return Result.Fail<CreateTodoResult>(result.Error);
                    
                    aggregate = result.Value;
                }
                
                // Save to event store (persists TodoCreated event)
                await _eventStore.SaveAsync(aggregate);
                
                _logger.Info($"[CreateTodoHandler] ✅ Todo persisted to event store: {aggregate.Id}");
                
                // Apply folder + note inherited tags
                // TODO: This will be event-driven in future via TodoCreated event subscription
                await ApplyAllTagsAsync(aggregate.Id, request.CategoryId, request.SourceNoteId);
                
                return Result.Ok(new CreateTodoResult
                {
                    TodoId = aggregate.Id,
                    Text = request.Text,
                    CategoryId = request.CategoryId,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[CreateTodoHandler] Exception creating todo");
                return Result.Fail<CreateTodoResult>($"Error creating todo: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Apply folder + note inherited tags to newly created todo.
        /// </summary>
        private async Task ApplyAllTagsAsync(Guid todoId, Guid? categoryId, Guid? sourceNoteId)
        {
            try
            {
                // Use TagInheritanceService to apply folder + note tags
                await _tagInheritanceService.UpdateTodoTagsAsync(todoId, null, categoryId, sourceNoteId);
                
                _logger.Info($"[CreateTodoHandler] ✅ Applied inherited tags to todo {todoId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CreateTodoHandler] Failed to apply tags (non-fatal)");
                // Tag inheritance failure shouldn't prevent todo creation
            }
        }
    }
}
