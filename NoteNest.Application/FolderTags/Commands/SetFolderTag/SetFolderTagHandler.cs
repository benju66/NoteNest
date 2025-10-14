using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
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
    private readonly IFolderTagRepository _folderTagRepository;
    private readonly IEventBus _eventBus;
    private readonly IAppLogger _logger;

    public SetFolderTagHandler(
        IFolderTagRepository folderTagRepository,
        IEventBus eventBus,
        IAppLogger logger)
    {
        _folderTagRepository = folderTagRepository ?? throw new ArgumentNullException(nameof(folderTagRepository));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<SetFolderTagResult>> Handle(SetFolderTagCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info($"Setting {request.Tags.Count} tags on folder {request.FolderId}");

            // Validate and set tags
            await _folderTagRepository.SetFolderTagsAsync(
                request.FolderId,
                request.Tags,
                request.IsAutoSuggested,
                request.InheritToChildren
            );

            // Publish event (UI layer event handlers will update todos if needed)
            var taggedEvent = new FolderTaggedEvent(request.FolderId, request.Tags, request.ApplyToExistingItems);
            await _eventBus.PublishAsync<IDomainEvent>(taggedEvent);

            var result = new SetFolderTagResult
            {
                FolderId = request.FolderId,
                AppliedTags = request.Tags,
                TodosUpdated = 0 // UI layer event handler will update this
            };

            _logger.Info($"Successfully set tags on folder {request.FolderId}");
            return Result<SetFolderTagResult>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to set folder tags for {request.FolderId}", ex);
            return Result<SetFolderTagResult>.Fail($"Failed to set folder tags: {ex.Message}");
        }
    }
}

