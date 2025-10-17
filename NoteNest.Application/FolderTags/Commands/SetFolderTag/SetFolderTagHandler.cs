using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Application.Common.Interfaces;

namespace NoteNest.Application.FolderTags.Commands.SetFolderTag;

/// <summary>
/// Handler for SetFolderTagCommand.
/// Sets tags on a folder via event sourcing.
/// </summary>
public class SetFolderTagHandler : IRequestHandler<SetFolderTagCommand, Result<SetFolderTagResult>>
{
    private readonly IEventStore _eventStore;
    private readonly IProjectionOrchestrator _projectionOrchestrator;
    private readonly IAppLogger _logger;

    public SetFolderTagHandler(
        IEventStore eventStore,
        IProjectionOrchestrator projectionOrchestrator,
        IAppLogger logger)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _projectionOrchestrator = projectionOrchestrator ?? throw new ArgumentNullException(nameof(projectionOrchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<SetFolderTagResult>> Handle(SetFolderTagCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info($"Setting {request.Tags.Count} tags on folder {request.FolderId}");

            // Load category aggregate from event store
            var categoryAggregate = await _eventStore.LoadAsync<NoteNest.Domain.Categories.CategoryAggregate>(request.FolderId);
            
            if (categoryAggregate == null)
            {
                _logger.Warning($"Category {request.FolderId} not found in event store");
                return Result<SetFolderTagResult>.Fail("Category not found");
            }

            // Set tags on aggregate (generates CategoryTagsSet event)
            categoryAggregate.SetTags(request.Tags, request.InheritToChildren);

            // Save to event store (persists event to events.db)
            await _eventStore.SaveAsync(categoryAggregate);
            
            // Trigger immediate projection update (so tags appear right away)
            await _projectionOrchestrator.CatchUpAsync();

            var result = new SetFolderTagResult
            {
                FolderId = request.FolderId,
                AppliedTags = request.Tags,
                Success = true
            };

            _logger.Info($"✅ Successfully set {request.Tags.Count} tags on folder {request.FolderId} via event sourcing.");
            return Result<SetFolderTagResult>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to set folder tags for {request.FolderId}", ex);
            return Result<SetFolderTagResult>.Fail($"Failed to set folder tags: {ex.Message}");
        }
    }
}

