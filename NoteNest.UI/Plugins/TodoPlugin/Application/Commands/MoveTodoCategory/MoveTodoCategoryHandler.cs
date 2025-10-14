using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;
using NoteNest.UI.Plugins.TodoPlugin.Services;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.MoveTodoCategory
{
    public class MoveTodoCategoryHandler : IRequestHandler<MoveTodoCategoryCommand, Result<MoveTodoCategoryResult>>
    {
        private readonly ITodoRepository _repository;
        private readonly ITodoTagRepository _todoTagRepository;
        private readonly IGlobalTagRepository _globalTagRepository;
        private readonly ITagGeneratorService _tagGenerator;
        private readonly NoteNest.Infrastructure.Database.ITreeDatabaseRepository _treeRepository;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;

        public MoveTodoCategoryHandler(
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

        public async Task<Result<MoveTodoCategoryResult>> Handle(MoveTodoCategoryCommand request, CancellationToken cancellationToken)
        {
            try
            {
                var todo = await _repository.GetByIdAsync(request.TodoId);
                if (todo == null)
                    return Result.Fail<MoveTodoCategoryResult>("Todo not found");

                var oldCategoryId = todo.CategoryId;
                
                // Check if already in target category
                if (oldCategoryId == request.TargetCategoryId)
                {
                    return Result.Ok(new MoveTodoCategoryResult
                    {
                        TodoId = todo.Id,
                        OldCategoryId = oldCategoryId,
                        NewCategoryId = request.TargetCategoryId,
                        Success = true
                    });
                }

                var aggregate = todo.ToAggregate();
                
                // Move to new category (null = uncategorized)
                aggregate.SetCategory(request.TargetCategoryId);
                
                var updatedTodo = Models.TodoItem.FromAggregate(aggregate);
                
                var success = await _repository.UpdateAsync(updatedTodo);
                if (!success)
                    return Result.Fail<MoveTodoCategoryResult>("Failed to update todo in database");
                
                // Publish events
                foreach (var domainEvent in aggregate.DomainEvents)
                {
                    await _eventBus.PublishAsync(domainEvent);
                }
                aggregate.ClearDomainEvents();
                
                _logger.Info($"[MoveTodoCategoryHandler] Moved todo {request.TodoId} from {oldCategoryId} to {request.TargetCategoryId}");
                
                // ✨ TAG MVP: Update auto-tags based on new category location
                await UpdateAutoTagsAsync(request.TodoId, request.TargetCategoryId);
                
                return Result.Ok(new MoveTodoCategoryResult
                {
                    TodoId = updatedTodo.Id,
                    OldCategoryId = oldCategoryId,
                    NewCategoryId = updatedTodo.CategoryId,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MoveTodoCategoryHandler] Error moving todo to category");
                return Result.Fail<MoveTodoCategoryResult>($"Error moving todo: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Update auto-tags when todo is moved to a different category.
        /// Removes old auto-tags and generates new ones based on the new category path.
        /// Manual tags are preserved.
        /// </summary>
        private async Task UpdateAutoTagsAsync(Guid todoId, Guid? newCategoryId)
        {
            try
            {
                _logger.Debug($"[MoveTodoCategoryHandler] Updating auto-tags for todo {todoId}");
                
                // Step 1: Get existing auto-tags (for cleanup)
                var existingTags = await _todoTagRepository.GetByTodoIdAsync(todoId);
                var existingAutoTags = existingTags.Where(t => t.IsAuto).Select(t => t.Tag).ToList();
                
                // Step 2: Remove all auto-tags
                await _todoTagRepository.DeleteAutoTagsAsync(todoId);
                _logger.Debug($"[MoveTodoCategoryHandler] Removed {existingAutoTags.Count} auto-tags");
                
                // Step 3: Decrement usage count for removed auto-tags
                foreach (var tag in existingAutoTags)
                {
                    await _globalTagRepository.DecrementUsageAsync(tag);
                }
                
                // Step 4: Generate new auto-tags from new category path
                List<string> newAutoTags = new List<string>();
                if (newCategoryId.HasValue)
                {
                    var category = await _treeRepository.GetNodeByIdAsync(newCategoryId.Value);
                    if (category != null)
                    {
                        newAutoTags = _tagGenerator.GenerateFromPath(category.DisplayPath);
                        _logger.Debug($"[MoveTodoCategoryHandler] Generated {newAutoTags.Count} new auto-tags from path: {category.DisplayPath}");
                    }
                }
                
                // Step 5: Add new auto-tags
                foreach (var tag in newAutoTags)
                {
                    await _todoTagRepository.AddAsync(new Infrastructure.Persistence.Models.TodoTag
                    {
                        TodoId = todoId,
                        Tag = tag,
                        IsAuto = true,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _globalTagRepository.IncrementUsageAsync(tag);
                }
                
                if (newAutoTags.Any())
                {
                    _logger.Info($"[MoveTodoCategoryHandler] ✅ Updated auto-tags: removed {existingAutoTags.Count}, added {newAutoTags.Count}: {string.Join(", ", newAutoTags)}");
                }
                else
                {
                    _logger.Info($"[MoveTodoCategoryHandler] ✅ Removed {existingAutoTags.Count} auto-tags, no new tags for this location");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MoveTodoCategoryHandler] Failed to update auto-tags (non-fatal, move still succeeded)");
                // Don't throw - tag update failure shouldn't prevent move
            }
        }
    }
}

