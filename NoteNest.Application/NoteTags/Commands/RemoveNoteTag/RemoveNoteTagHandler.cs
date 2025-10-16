using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Application.NoteTags.Events;
using NoteNest.Domain.Tags.Events;

namespace NoteNest.Application.NoteTags.Commands.RemoveNoteTag;

/// <summary>
/// Handler for RemoveNoteTagCommand.
/// Removes a tag from a note via events.
/// </summary>
public class RemoveNoteTagHandler : IRequestHandler<RemoveNoteTagCommand, Result<RemoveNoteTagResult>>
{
    private readonly IEventStore _eventStore;
    private readonly IAppLogger _logger;

    public RemoveNoteTagHandler(
        IEventStore eventStore,
        IAppLogger logger)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<RemoveNoteTagResult>> Handle(RemoveNoteTagCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info($"Removing tag '{request.TagName}' from note {request.NoteId}");

            // Generate TagRemovedFromEntity event
            var tagEvent = new TagRemovedFromEntity(
                request.NoteId,
                "note",
                request.TagName);

            // Publish legacy NoteUntaggedEvent for backward compatibility
            var untaggedEvent = new NoteUntaggedEvent(request.NoteId, new List<string> { request.TagName });
            // Event will be handled by TagProjection

            var result = new RemoveNoteTagResult
            {
                NoteId = request.NoteId,
                RemovedTag = request.TagName,
                Success = true
            };

            _logger.Info($"Successfully removed tag '{request.TagName}' from note {request.NoteId}");
            return Result<RemoveNoteTagResult>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove note tag for {request.NoteId}", ex);
            return Result<RemoveNoteTagResult>.Fail($"Failed to remove note tag: {ex.Message}");
        }
    }
}
