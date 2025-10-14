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
        private readonly ITagGeneratorService _tagGenerator;
        private readonly NoteNest.Infrastructure.Database.ITreeDatabaseRepository _treeRepository;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public CreateTodoHandler(
            ITodoRepository repository,
            ITodoTagRepository todoTagRepository,
            IGlobalTagRepository globalTagRepository,
            ITagGeneratorService tagGenerator,
            NoteNest.Infrastructure.Database.ITreeDatabaseRepository treeRepository,
            IEventBus eventBus,
            IAppLogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _todoTagRepository = todoTagRepository ?? throw new ArgumentNullException(nameof(todoTagRepository));
            _globalTagRepository = globalTagRepository ?? throw new ArgumentNullException(nameof(globalTagRepository));
            _tagGenerator = tagGenerator ?? throw new ArgumentNullException(nameof(tagGenerator));
            _treeRepository = treeRepository ?? throw new ArgumentNullException(nameof(treeRepository));
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
                
                // ✨ TAG MVP: Generate auto-tags for the new todo
                await GenerateAutoTagsAsync(todoItem.Id, request);
                
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
        /// Generate auto-tags for newly created todo based on source (note or category).
        /// Tags are generated from folder structure following 2-tag project-only strategy.
        /// </summary>
        private async Task GenerateAutoTagsAsync(Guid todoId, CreateTodoCommand command)
        {
            try
            {
                List<string> autoTags = new List<string>();
                
                // Case 1: Todo extracted from note (bracket in RTF)
                // TODO: Once NoteTagRepository exists, inherit tags from source note
                // For now, generate from category path if available
                
                // Case 2: Todo created via quick-add or has category
                if (command.CategoryId.HasValue)
                {
                    // Get category from tree database
                    var category = await _treeRepository.GetNodeByIdAsync(command.CategoryId.Value);
                    if (category != null)
                    {
                        // Generate auto-tags from category path
                        autoTags = _tagGenerator.GenerateFromPath(category.DisplayPath);
                        _logger.Debug($"[CreateTodoHandler] Generated {autoTags.Count} auto-tags from category path: {category.DisplayPath}");
                    }
                }
                
                // Add tags to database
                foreach (var tag in autoTags)
                {
                    var todoTag = new Infrastructure.Persistence.Models.TodoTag
                    {
                        TodoId = todoId,
                        Tag = tag,
                        IsAuto = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    
                    await _todoTagRepository.AddAsync(todoTag);
                    
                    // Update global tag registry
                    await _globalTagRepository.IncrementUsageAsync(tag);
                }
                
                if (autoTags.Any())
                {
                    _logger.Info($"[CreateTodoHandler] ✅ Added {autoTags.Count} auto-tags: {string.Join(", ", autoTags)}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CreateTodoHandler] Failed to generate auto-tags (non-fatal, todo still created)");
                // Don't throw - tag generation failure shouldn't prevent todo creation
            }
        }
    }
}

