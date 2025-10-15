using FluentValidation;

namespace NoteNest.Application.NoteTags.Commands.RemoveNoteTag;

/// <summary>
/// Validator for RemoveNoteTagCommand.
/// </summary>
public class RemoveNoteTagValidator : AbstractValidator<RemoveNoteTagCommand>
{
    public RemoveNoteTagValidator()
    {
        RuleFor(x => x.NoteId)
            .NotEqual(Guid.Empty)
            .WithMessage("Note ID cannot be empty");
    }
}

