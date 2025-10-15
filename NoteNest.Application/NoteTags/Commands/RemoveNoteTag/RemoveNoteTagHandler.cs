using MediatR;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Application.NoteTags.Repositories;
using NoteNest.Application.NoteTags.Events;
using NoteNest.Core.Services;

namespace NoteNest.Application.NoteTags.Commands.RemoveNoteTag;

/// <summary>
/// Handler for RemoveNoteTagCommand.
/// Removes all tags from a note.
/// </summary>
public class RemoveNoteTagHandler : IRequestHandler<RemoveNoteTagCommand, Result<RemoveNoteTagResult>>
{
    private readonly INoteTagRepository _noteTagRepository;
    private readonly IEventBus _eventBus;
    private readonly IAppLogger _logger;

    public RemoveNoteTagHandler(
        INoteTagRepository noteTagRepository,
        IEventBus eventBus,
        IAppLogger logger)
    {
        _noteTagRepository = noteTagRepository ?? throw new ArgumentNullException(nameof(noteTagRepository));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<RemoveNoteTagResult>> Handle(RemoveNoteTagCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info($"Removing tags from note {request.NoteId}");

            // Get tags before removal (for event and logging)
            var tagsToRemove = await _noteTagRepository.GetNoteTagsAsync(request.NoteId);

            // Remove tags from note
            await _noteTagRepository.RemoveNoteTagsAsync(request.NoteId);

            // Publish event (for potential UI refresh)
            var untaggedEvent = new NoteUntaggedEvent(
                request.NoteId, 
                tagsToRemove.Select(t => t.Tag).ToList());
            await _eventBus.PublishAsync<IDomainEvent>(untaggedEvent);

            var result = new RemoveNoteTagResult
            {
                NoteId = request.NoteId,
                Success = true
            };

            _logger.Info($"Successfully removed {tagsToRemove.Count} tags from note {request.NoteId}");
            return Result<RemoveNoteTagResult>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove note tags for {request.NoteId}", ex);
            return Result<RemoveNoteTagResult>.Fail($"Failed to remove note tags: {ex.Message}");
        }
    }
}

