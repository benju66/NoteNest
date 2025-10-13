using System;
using FluentValidation;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.MoveTodoCategory
{
    public class MoveTodoCategoryValidator : AbstractValidator<MoveTodoCategoryCommand>
    {
        public MoveTodoCategoryValidator()
        {
            RuleFor(x => x.TodoId)
                .NotEqual(Guid.Empty).WithMessage("Todo ID cannot be empty");
            
            // TargetCategoryId is optional (null = uncategorized)
        }
    }
}

