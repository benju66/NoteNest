using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Aggregates;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;
using NoteNest.UI.Plugins.TodoPlugin.Models;
using NoteNest.UI.Plugins.TodoPlugin.Services;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.CreateTodo
{
    /// <summary>
    /// Handler for creating new todos.
    /// Implements event-driven CQRS pattern:
    /// - Saves to repository (write side)
    /// - Publishes domain events
    /// - TodoStore subscribes to events and updates UI (read side)
    /// </summary>
    public class CreateTodoHandler : IRequestHandler<CreateTodoCommand, Result<CreateTodoResult>>
    {
        private readonly ITodoRepository _repository;
        private readonly ITodoTagRepository _todoTagRepository;
        private readonly IGlobalTagRepository _globalTagRepository;
        private readonly ITagInheritanceService _tagInheritanceService;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public CreateTodoHandler(
            ITodoRepository repository,
            ITodoTagRepository todoTagRepository,
            IGlobalTagRepository globalTagRepository,
            ITagInheritanceService tagInheritanceService,
            IEventBus eventBus,
            IAppLogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _todoTagRepository = todoTagRepository ?? throw new ArgumentNullException(nameof(todoTagRepository));
            _globalTagRepository = globalTagRepository ?? throw new ArgumentNullException(nameof(globalTagRepository));
            _tagInheritanceService = tagInheritanceService ?? throw new ArgumentNullException(nameof(tagInheritanceService));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
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
                
                // Convert to UI model for persistence
                var todoItem = TodoItem.FromAggregate(aggregate);
                
                // Persist to database
                var success = await _repository.InsertAsync(todoItem);
                
                if (!success)
                {
                    _logger.Error($"[CreateTodoHandler] Failed to insert todo to database");
                    return Result.Fail<CreateTodoResult>("Failed to save todo to database");
                }
                
                _logger.Info($"[CreateTodoHandler] ✅ Todo persisted: {todoItem.Id}");
                
                // Publish domain events (TodoStore will subscribe and update UI)
                _logger.Info($"[CreateTodoHandler] 📢 About to publish {aggregate.DomainEvents.Count} domain events");
                foreach (var domainEvent in aggregate.DomainEvents)
                {
                    _logger.Info($"[CreateTodoHandler] 📢 Publishing: {domainEvent.GetType().Name} for TodoId={todoItem.Id}");
                    await _eventBus.PublishAsync(domainEvent);
                    _logger.Info($"[CreateTodoHandler] ✅ Event published successfully: {domainEvent.GetType().Name}");
                }
                
                _logger.Info($"[CreateTodoHandler] ✅ All {aggregate.DomainEvents.Count} domain events published");
                aggregate.ClearDomainEvents();
                
                // ✨ HYBRID FOLDER TAGGING: Apply folder-inherited tags to the new todo
                await ApplyFolderTagsAsync(todoItem.Id, request.CategoryId);
                
                return Result.Ok(new CreateTodoResult
                {
                    TodoId = todoItem.Id,
                    Text = todoItem.Text,
                    CategoryId = todoItem.CategoryId,
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
        /// Apply folder-inherited tags to newly created todo.
        /// Uses Hybrid Folder Tagging system - tags come from user-set folder tags.
        /// </summary>
        private async Task ApplyFolderTagsAsync(Guid todoId, Guid? categoryId)
        {
            try
            {
                if (!categoryId.HasValue || categoryId.Value == Guid.Empty)
                {
                    _logger.Debug($"[CreateTodoHandler] No category for todo {todoId}, skipping folder tag inheritance");
                    return;
                }
                
                // Use TagInheritanceService to apply folder tags
                await _tagInheritanceService.UpdateTodoTagsAsync(todoId, null, categoryId.Value);
                
                _logger.Info($"[CreateTodoHandler] ✅ Applied folder-inherited tags to todo {todoId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CreateTodoHandler] Failed to apply folder tags (non-fatal, todo still created)");
                // Don't throw - tag inheritance failure shouldn't prevent todo creation
            }
        }
    }
}

