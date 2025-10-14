using FluentValidation;

namespace NoteNest.Application.FolderTags.Commands.RemoveFolderTag;

/// <summary>
/// Validator for RemoveFolderTagCommand.
/// </summary>
public class RemoveFolderTagValidator : AbstractValidator<RemoveFolderTagCommand>
{
    public RemoveFolderTagValidator()
    {
        RuleFor(x => x.FolderId)
            .NotEqual(Guid.Empty)
            .WithMessage("Folder ID cannot be empty");
    }
}

