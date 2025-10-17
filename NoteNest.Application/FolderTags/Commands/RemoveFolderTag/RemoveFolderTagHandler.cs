using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Application.Common.Interfaces;

namespace NoteNest.Application.FolderTags.Commands.RemoveFolderTag;

/// <summary>
/// Handler for RemoveFolderTagCommand.
/// Removes all tags from a folder via event sourcing.
/// </summary>
public class RemoveFolderTagHandler : IRequestHandler<RemoveFolderTagCommand, Result<RemoveFolderTagResult>>
{
    private readonly IEventStore _eventStore;
    private readonly IProjectionOrchestrator _projectionOrchestrator;
    private readonly IAppLogger _logger;

    public RemoveFolderTagHandler(
        IEventStore eventStore,
        IProjectionOrchestrator projectionOrchestrator,
        IAppLogger logger)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _projectionOrchestrator = projectionOrchestrator ?? throw new ArgumentNullException(nameof(projectionOrchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<RemoveFolderTagResult>> Handle(RemoveFolderTagCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info($"Removing all tags from folder {request.FolderId}");

            // Load category aggregate from event store
            var categoryAggregate = await _eventStore.LoadAsync<NoteNest.Domain.Categories.CategoryAggregate>(request.FolderId);
            
            if (categoryAggregate == null)
            {
                _logger.Warning($"Category {request.FolderId} not found in event store");
                return Result<RemoveFolderTagResult>.Fail("Category not found");
            }

            // Clear tags (generates CategoryTagsSet event with empty list)
            categoryAggregate.ClearTags();

            // Save to event store
            await _eventStore.SaveAsync(categoryAggregate);
            
            // Trigger immediate projection update
            await _projectionOrchestrator.CatchUpAsync();

            var result = new RemoveFolderTagResult
            {
                FolderId = request.FolderId,
                Success = true
            };

            _logger.Info($"✅ Successfully removed all tags from folder {request.FolderId} via event sourcing.");
            return Result<RemoveFolderTagResult>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove folder tags for {request.FolderId}", ex);
            return Result<RemoveFolderTagResult>.Fail($"Failed to remove folder tags: {ex.Message}");
        }
    }
}
