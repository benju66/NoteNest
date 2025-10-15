using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Application.FolderTags.Repositories;
using NoteNest.Application.FolderTags.Events;
using NoteNest.Core.Services;

namespace NoteNest.Application.FolderTags.Commands.RemoveFolderTag;

/// <summary>
/// Handler for RemoveFolderTagCommand.
/// Removes all tags from a folder. Publishes event for UI layer to handle item updates.
/// </summary>
public class RemoveFolderTagHandler : IRequestHandler<RemoveFolderTagCommand, Result<RemoveFolderTagResult>>
{
    private readonly IFolderTagRepository _folderTagRepository;
    private readonly IEventBus _eventBus;
    private readonly IAppLogger _logger;

    public RemoveFolderTagHandler(
        IFolderTagRepository folderTagRepository,
        IEventBus eventBus,
        IAppLogger logger)
    {
        _folderTagRepository = folderTagRepository ?? throw new ArgumentNullException(nameof(folderTagRepository));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<RemoveFolderTagResult>> Handle(RemoveFolderTagCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info($"Removing tags from folder {request.FolderId}");

            // Get tags before removal (for event and logging)
            var tagsToRemove = await _folderTagRepository.GetFolderTagsAsync(request.FolderId);

            // Remove tags from folder
            await _folderTagRepository.RemoveFolderTagsAsync(request.FolderId);

            // Publish event (UI layer can refresh visual indicators)
            var untaggedEvent = new FolderUntaggedEvent(
                request.FolderId, 
                tagsToRemove.Select(t => t.Tag).ToList());
            await _eventBus.PublishAsync<IDomainEvent>(untaggedEvent);

            var result = new RemoveFolderTagResult
            {
                FolderId = request.FolderId,
                Success = true
            };

            _logger.Info($"Successfully removed {tagsToRemove.Count} tags from folder {request.FolderId}. Existing items keep their tags.");
            return Result<RemoveFolderTagResult>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove folder tags for {request.FolderId}", ex);
            return Result<RemoveFolderTagResult>.Fail($"Failed to remove folder tags: {ex.Message}");
        }
    }
}

