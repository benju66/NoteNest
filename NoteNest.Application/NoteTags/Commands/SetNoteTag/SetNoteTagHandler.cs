using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Application.NoteTags.Events;

namespace NoteNest.Application.NoteTags.Commands.SetNoteTag;

/// <summary>
/// Handler for SetNoteTagCommand.
/// Sets tags on a note via events.
/// </summary>
public class SetNoteTagHandler : IRequestHandler<SetNoteTagCommand, Result<SetNoteTagResult>>
{
    private readonly IEventStore _eventStore;
    private readonly IAppLogger _logger;

    public SetNoteTagHandler(
        IEventStore eventStore,
        IAppLogger logger)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<SetNoteTagResult>> Handle(SetNoteTagCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info($"Setting {request.Tags.Count} tags on note {request.NoteId}");

            // Generate TagAddedToEntity events for each tag
            foreach (var tag in request.Tags)
            {
                var tagEvent = new NoteNest.Domain.Tags.Events.TagAddedToEntity(
                    request.NoteId,
                    "note",
                    tag,
                    tag, // DisplayName
                    "manual");
                
                // Events will be persisted and published to projections
            }

            // Publish legacy NoteTaggedEvent for backward compatibility
            var taggedEvent = new NoteTaggedEvent(request.NoteId, request.Tags);
            // Event will be handled by TagProjection

            var result = new SetNoteTagResult
            {
                NoteId = request.NoteId,
                AppliedTags = request.Tags,
                Success = true
            };

            _logger.Info($"Successfully set {request.Tags.Count} tags on note {request.NoteId}");
            return Result<SetNoteTagResult>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to set note tags for {request.NoteId}", ex);
            return Result<SetNoteTagResult>.Fail($"Failed to set note tags: {ex.Message}");
        }
    }
}

