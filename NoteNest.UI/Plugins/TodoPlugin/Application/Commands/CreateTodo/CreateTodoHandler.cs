using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly NoteNest.Application.Common.Interfaces.IEventBus _eventBus;
        private readonly NoteNest.Application.Common.Interfaces.IProjectionOrchestrator _projectionOrchestrator;
        private readonly IAppLogger _logger;

        public CreateTodoHandler(
            IEventStore eventStore,
            ITagInheritanceService tagInheritanceService,
            NoteNest.Application.Common.Interfaces.IEventBus eventBus,
            NoteNest.Application.Common.Interfaces.IProjectionOrchestrator projectionOrchestrator,
            IAppLogger logger)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _tagInheritanceService = tagInheritanceService ?? throw new ArgumentNullException(nameof(tagInheritanceService));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _projectionOrchestrator = projectionOrchestrator ?? throw new ArgumentNullException(nameof(projectionOrchestrator));
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
                    // Todo from RTF extraction - pass categoryId to factory so it's in the event
                    var result = TodoAggregate.CreateFromNote(
                        request.Text,
                        request.SourceNoteId.Value,
                        request.SourceFilePath,
                        request.SourceLineNumber,
                        request.SourceCharOffset,
                        request.CategoryId);
                    
                    if (result.IsFailure)
                        return Result.Fail<CreateTodoResult>(result.Error);
                    
                    aggregate = result.Value;
                    
                    // CategoryId now set in event - no need for SetCategory()
                }
                else
                {
                    // Manual todo creation
                    var result = TodoAggregate.Create(request.Text, request.CategoryId);
                    
                    if (result.IsFailure)
                        return Result.Fail<CreateTodoResult>(result.Error);
                    
                    aggregate = result.Value;
                }
                
                // ✨ CRITICAL FIX: Capture events BEFORE SaveAsync (SaveAsync clears DomainEvents)
                var creationEvents = new List<IDomainEvent>(aggregate.DomainEvents);
                
                // Save to event store (persists TodoCreated event)
                await _eventStore.SaveAsync(aggregate);
                
                _logger.Info($"[CreateTodoHandler] ✅ Todo persisted to event store: {aggregate.Id}");
                
                // ✨ CRITICAL: Update projections BEFORE publishing events
                // This ensures todo_view is updated so TodoStore can load the todo when it receives the event
                try
                {
                    _logger.Debug($"[CreateTodoHandler] Updating projections before event publication...");
                    await _projectionOrchestrator.CatchUpAsync();
                    _logger.Debug($"[CreateTodoHandler] ✅ Projections updated - database ready");
                }
                catch (Exception projEx)
                {
                    _logger.Error(projEx, "[CreateTodoHandler] Failed to update projections, continuing anyway");
                    // Don't fail the operation - projections will eventually catch up
                }
                
                // Publish captured events to InMemoryEventBus for UI updates
                // Database is now ready, so TodoStore can successfully load the todo
                foreach (var domainEvent in creationEvents)
                {
                    await _eventBus.PublishAsync(domainEvent);
                    _logger.Debug($"[CreateTodoHandler] Published event: {domainEvent.GetType().Name}");
                }
                
                // Apply folder + note inherited tags (returns tag events to publish)
                var tagEvents = await ApplyAllTagsAsync(aggregate.Id, request.CategoryId, request.SourceNoteId);
                
                // Publish tag events to InMemoryEventBus
                foreach (var domainEvent in tagEvents)
                {
                    await _eventBus.PublishAsync(domainEvent);
                    _logger.Debug($"[CreateTodoHandler] Published tag event: {domainEvent.GetType().Name}");
                }
                
                // ✨ FIX #1: Update projections again to process tag events
                // This ensures entity_tags is updated before TodoTagDialog queries it
                try
                {
                    _logger.Debug($"[CreateTodoHandler] Updating projections for tag events...");
                    await _projectionOrchestrator.CatchUpAsync();
                    _logger.Debug($"[CreateTodoHandler] ✅ Tag projections updated");
                }
                catch (Exception projEx)
                {
                    _logger.Error(projEx, "[CreateTodoHandler] Failed to update tag projections (non-fatal)");
                    // Don't fail the operation - projections will eventually catch up
                }
                
                // Mark all events as committed
                aggregate.MarkEventsAsCommitted();
                
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
        /// EVENT-SOURCED: Tags applied via TodoAggregate.AddTag() which emits TagAddedToEntity events.
        /// TagProjection writes to projections.db/entity_tags (not todos.db).
        /// Returns the tag events for the caller to publish.
        /// </summary>
        private async Task<List<IDomainEvent>> ApplyAllTagsAsync(Guid todoId, Guid? categoryId, Guid? sourceNoteId)
        {
            try
            {
                // Get inherited tags from folder and note
                var folderTags = new List<string>();
                if (categoryId.HasValue && categoryId.Value != Guid.Empty)
                {
                    folderTags = await _tagInheritanceService.GetApplicableTagsAsync(categoryId.Value);
                }
                
                var noteTags = new List<string>();
                if (sourceNoteId.HasValue && sourceNoteId.Value != Guid.Empty)
                {
                    // Query note tags from projections (event-sourced)
                    var noteAggregate = await _eventStore.LoadAsync<NoteNest.Domain.Notes.Note>(sourceNoteId.Value);
                    if (noteAggregate != null && noteAggregate.Tags != null)
                    {
                        noteTags = noteAggregate.Tags.ToList();
                    }
                }
                
                // Merge tags (no duplicates)
                var allTags = folderTags
                    .Union(noteTags, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                
                if (allTags.Count == 0)
                {
                    _logger.Debug($"[CreateTodoHandler] No tags to apply to todo {todoId}");
                    return new List<IDomainEvent>();
                }
                
                // Load aggregate and add tags (generates TagAddedToEntity events)
                var aggregate = await _eventStore.LoadAsync<TodoAggregate>(todoId);
                if (aggregate == null)
                {
                    _logger.Warning($"[CreateTodoHandler] Can't apply tags - aggregate not found: {todoId}");
                    return new List<IDomainEvent>();
                }
                
                foreach (var tag in allTags)
                {
                    aggregate.AddTag(tag);  // Emits TagAddedToEntity event
                }
                
                // Capture tag events BEFORE SaveAsync (SaveAsync clears DomainEvents)
                var tagEvents = new List<IDomainEvent>(aggregate.DomainEvents);
                
                // Save aggregate (persists tag events to events.db)
                await _eventStore.SaveAsync(aggregate);
                
                _logger.Info($"[CreateTodoHandler] ✅ Applied {allTags.Count} inherited tags to todo {todoId} via events (folder: {folderTags.Count}, note: {noteTags.Count})");
                
                return tagEvents;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CreateTodoHandler] Failed to apply tags (non-fatal)");
                // Tag inheritance failure shouldn't prevent todo creation
                return new List<IDomainEvent>();
            }
        }
    }
}
