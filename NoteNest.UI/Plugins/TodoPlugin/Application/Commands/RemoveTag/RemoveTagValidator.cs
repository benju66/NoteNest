using System;
using FluentValidation;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.RemoveTag
{
    public class RemoveTagValidator : AbstractValidator<RemoveTagCommand>
    {
        public RemoveTagValidator()
        {
            RuleFor(x => x.TodoId)
                .NotEqual(Guid.Empty).WithMessage("Todo ID cannot be empty");

            RuleFor(x => x.TagName)
                .NotEmpty().WithMessage("Tag name is required");
        }
    }
}

