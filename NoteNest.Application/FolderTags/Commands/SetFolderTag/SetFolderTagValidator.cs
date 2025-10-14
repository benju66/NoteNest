using FluentValidation;

namespace NoteNest.Application.FolderTags.Commands.SetFolderTag;

/// <summary>
/// Validator for SetFolderTagCommand.
/// </summary>
public class SetFolderTagValidator : AbstractValidator<SetFolderTagCommand>
{
    public SetFolderTagValidator()
    {
        RuleFor(x => x.FolderId)
            .NotEqual(Guid.Empty)
            .WithMessage("Folder ID cannot be empty");

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

