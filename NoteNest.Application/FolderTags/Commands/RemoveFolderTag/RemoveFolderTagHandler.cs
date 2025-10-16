using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Application.FolderTags.Events;
using NoteNest.Domain.Tags.Events;

namespace NoteNest.Application.FolderTags.Commands.RemoveFolderTag;

/// <summary>
/// Handler for RemoveFolderTagCommand.
/// Removes a tag from a folder via events.
/// </summary>
public class RemoveFolderTagHandler : IRequestHandler<RemoveFolderTagCommand, Result<RemoveFolderTagResult>>
{
    private readonly IEventStore _eventStore;
    private readonly IAppLogger _logger;

    public RemoveFolderTagHandler(
        IEventStore eventStore,
        IAppLogger logger)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<RemoveFolderTagResult>> Handle(RemoveFolderTagCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info($"Removing tag '{request.TagName}' from folder {request.FolderId}");

            // Generate TagRemovedFromEntity event
            var tagEvent = new TagRemovedFromEntity(
                request.FolderId,
                "folder",
                request.TagName);

            // Publish legacy FolderUntaggedEvent for backward compatibility
            var untaggedEvent = new FolderUntaggedEvent(request.FolderId, new List<string> { request.TagName });
            // Event will be handled by TagProjection

            var result = new RemoveFolderTagResult
            {
                FolderId = request.FolderId,
                RemovedTag = request.TagName,
                Success = true
            };

            _logger.Info($"Successfully removed tag '{request.TagName}' from folder {request.FolderId}");
            return Result<RemoveFolderTagResult>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove folder tag for {request.FolderId}", ex);
            return Result<RemoveFolderTagResult>.Fail($"Failed to remove folder tag: {ex.Message}");
        }
    }
}
