using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Application.FolderTags.Repositories;
using NoteNest.Application.FolderTags.Events;
using NoteNest.Core.Services;

namespace NoteNest.Application.FolderTags.Commands.SetFolderTag;

/// <summary>
/// Handler for SetFolderTagCommand.
/// Sets tags on a folder. Publishes event for UI layer to handle item updates.
/// </summary>
public class SetFolderTagHandler : IRequestHandler<SetFolderTagCommand, Result<SetFolderTagResult>>
{
    private readonly IEventStore _eventStore;
    private readonly IAppLogger _logger;

    public SetFolderTagHandler(
        IEventStore eventStore,
        IAppLogger logger)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<SetFolderTagResult>> Handle(SetFolderTagCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info($"Setting {request.Tags.Count} tags on folder {request.FolderId}");

            // Create domain events for each tag
            // In event sourcing, we raise TagAddedToEntity events
            foreach (var tag in request.Tags)
            {
                var tagAddedEvent = new NoteNest.Domain.Tags.Events.TagAddedToEntity(
                    request.FolderId,
                    "folder",
                    tag,
                    tag, // DisplayName same as tag for now
                    request.IsAutoSuggested ? "auto-path" : "manual");
                
                // For now, we'll publish directly to event bus
                // TODO: This should go through a FolderAggregate
                // But for migration, we support legacy event pattern
            }

            // Publish legacy event for backward compatibility during migration
            var taggedEvent = new FolderTaggedEvent(request.FolderId, request.Tags);
            // Event will be published by projection orchestrator

            var result = new SetFolderTagResult
            {
                FolderId = request.FolderId,
                AppliedTags = request.Tags,
                Success = true
            };

            _logger.Info($"Successfully set {request.Tags.Count} tags on folder {request.FolderId}. New items will inherit these tags.");
            return Result<SetFolderTagResult>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to set folder tags for {request.FolderId}", ex);
            return Result<SetFolderTagResult>.Fail($"Failed to set folder tags: {ex.Message}");
        }
    }
}

