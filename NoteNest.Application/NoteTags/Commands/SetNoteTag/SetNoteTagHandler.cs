using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Application.Common.Interfaces;

namespace NoteNest.Application.NoteTags.Commands.SetNoteTag;

/// <summary>
/// Handler for SetNoteTagCommand.
/// Sets tags on a note via event sourcing.
/// </summary>
public class SetNoteTagHandler : IRequestHandler<SetNoteTagCommand, Result<SetNoteTagResult>>
{
    private readonly IEventStore _eventStore;
    private readonly IProjectionOrchestrator _projectionOrchestrator;
    private readonly IAppLogger _logger;

    public SetNoteTagHandler(
        IEventStore eventStore,
        IProjectionOrchestrator projectionOrchestrator,
        IAppLogger logger)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _projectionOrchestrator = projectionOrchestrator ?? throw new ArgumentNullException(nameof(projectionOrchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<SetNoteTagResult>> Handle(SetNoteTagCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info($"Setting {request.Tags.Count} tags on note {request.NoteId}");

            // Load note aggregate from event store
            var noteAggregate = await _eventStore.LoadAsync<NoteNest.Domain.Notes.Note>(request.NoteId);
            
            if (noteAggregate == null)
            {
                _logger.Warning($"Note {request.NoteId} not found in event store");
                return Result<SetNoteTagResult>.Fail("Note not found");
            }

            // Set tags on aggregate (generates NoteTagsSet event)
            noteAggregate.SetTags(request.Tags);

            // Save to event store (persists event to events.db)
            await _eventStore.SaveAsync(noteAggregate);
            
            // Trigger immediate projection update (so tags appear right away)
            await _projectionOrchestrator.CatchUpAsync();

            var result = new SetNoteTagResult
            {
                NoteId = request.NoteId,
                AppliedTags = request.Tags,
                Success = true
            };

            _logger.Info($"✅ Successfully set {request.Tags.Count} tags on note {request.NoteId} via event sourcing.");
            return Result<SetNoteTagResult>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to set note tags for {request.NoteId}", ex);
            return Result<SetNoteTagResult>.Fail($"Failed to set note tags: {ex.Message}");
        }
    }
}

