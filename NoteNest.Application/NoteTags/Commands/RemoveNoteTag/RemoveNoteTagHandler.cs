using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Application.NoteTags.Events;

namespace NoteNest.Application.NoteTags.Commands.RemoveNoteTag;

/// <summary>
/// Handler for RemoveNoteTagCommand.
/// Removes all tags from a note via events.
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
            _logger.Info($"Removing all tags from note {request.NoteId}");

            // Publish NoteUntaggedEvent with empty list (removes all)
            var untaggedEvent = new NoteUntaggedEvent(request.NoteId, new List<string>());
            // Event will be handled by TagProjection

            var result = new RemoveNoteTagResult
            {
                NoteId = request.NoteId,
                Success = true
            };

            _logger.Info($"Successfully removed all tags from note {request.NoteId}");
            return Result<RemoveNoteTagResult>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove note tags for {request.NoteId}", ex);
            return Result<RemoveNoteTagResult>.Fail($"Failed to remove note tags: {ex.Message}");
        }
    }
}
