using FluentValidation;

namespace NoteNest.Application.NoteTags.Commands.SetNoteTag;

/// <summary>
/// Validator for SetNoteTagCommand.
/// </summary>
public class SetNoteTagValidator : AbstractValidator<SetNoteTagCommand>
{
    public SetNoteTagValidator()
    {
        RuleFor(x => x.NoteId)
            .NotEqual(Guid.Empty)
            .WithMessage("Note ID cannot be empty");

        RuleFor(x => x.Tags)
            .NotEmpty()
            .WithMessage("At least one tag is required");

        RuleForEach(x => x.Tags)
            .NotEmpty()
            .WithMessage("Tag cannot be empty")
            .MaximumLength(50)
            .WithMessage("Tag cannot exceed 50 characters")
            .Matches(@"^[\w&\s-]+$")
            .WithMessage("Tag can only contain letters, numbers, spaces, hyphens, ampersands, and underscores");
    }
}

