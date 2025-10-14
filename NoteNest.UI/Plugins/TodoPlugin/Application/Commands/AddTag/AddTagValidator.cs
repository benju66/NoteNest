using System;
using FluentValidation;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.AddTag
{
    public class AddTagValidator : AbstractValidator<AddTagCommand>
    {
        public AddTagValidator()
        {
            RuleFor(x => x.TodoId)
                .NotEqual(Guid.Empty).WithMessage("Todo ID cannot be empty");

            RuleFor(x => x.TagName)
                .NotEmpty().WithMessage("Tag name is required")
                .MaximumLength(50).WithMessage("Tag name cannot exceed 50 characters")
                .Matches(@"^[\w&-]+$").WithMessage("Tag name can only contain letters, numbers, hyphens, and ampersands");
        }
    }
}

