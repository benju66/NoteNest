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
    private readonly IFolderTagRepository _repository;
    private readonly NoteNest.Application.Common.Interfaces.IEventBus _eventBus;
    private readonly IAppLogger _logger;

    public SetFolderTagHandler(
        IFolderTagRepository repository,
        NoteNest.Application.Common.Interfaces.IEventBus eventBus,
        IAppLogger logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<SetFolderTagResult>> Handle(SetFolderTagCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info($"Setting {request.Tags.Count} tags on folder {request.FolderId}");

            // Save tags to database (tree.db folder_tags table)
            await _repository.SetFolderTagsAsync(
                request.FolderId,
                request.Tags,
                request.IsAutoSuggested,
                request.InheritToChildren);

            // Publish event for UI refresh and tag inheritance
            var taggedEvent = new FolderTaggedEvent(request.FolderId, request.Tags);
            await _eventBus.PublishAsync(taggedEvent);

            var result = new SetFolderTagResult
            {
                FolderId = request.FolderId,
                AppliedTags = request.Tags,
                Success = true
            };

            _logger.Info($"✅ Successfully set {request.Tags.Count} tags on folder {request.FolderId}. Tags will inherit to new todos.");
            return Result<SetFolderTagResult>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to set folder tags for {request.FolderId}", ex);
            return Result<SetFolderTagResult>.Fail($"Failed to set folder tags: {ex.Message}");
        }
    }
}

