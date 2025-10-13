using System;
using FluentValidation;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.ToggleFavorite
{
    public class ToggleFavoriteValidator : AbstractValidator<ToggleFavoriteCommand>
    {
        public ToggleFavoriteValidator()
        {
            RuleFor(x => x.TodoId)
                .NotEqual(Guid.Empty).WithMessage("Todo ID cannot be empty");
        }
    }
}

