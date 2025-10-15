using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Application.NoteTags.Repositories;
using NoteNest.Application.NoteTags.Events;
using NoteNest.Core.Services;

namespace NoteNest.Application.NoteTags.Commands.SetNoteTag;

/// <summary>
/// Handler for SetNoteTagCommand.
/// Sets tags on a note.
/// </summary>
public class SetNoteTagHandler : IRequestHandler<SetNoteTagCommand, Result<SetNoteTagResult>>
{
    private readonly INoteTagRepository _noteTagRepository;
    private readonly IEventBus _eventBus;
    private readonly IAppLogger _logger;

    public SetNoteTagHandler(
        INoteTagRepository noteTagRepository,
        IEventBus eventBus,
        IAppLogger logger)
    {
        _noteTagRepository = noteTagRepository ?? throw new ArgumentNullException(nameof(noteTagRepository));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<SetNoteTagResult>> Handle(SetNoteTagCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info($"Setting {request.Tags.Count} tags on note {request.NoteId}");

            // Set tags in database
            await _noteTagRepository.SetNoteTagsAsync(request.NoteId, request.Tags);

            // Publish event (for potential UI refresh)
            var taggedEvent = new NoteTaggedEvent(request.NoteId, request.Tags);
            await _eventBus.PublishAsync<IDomainEvent>(taggedEvent);

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

