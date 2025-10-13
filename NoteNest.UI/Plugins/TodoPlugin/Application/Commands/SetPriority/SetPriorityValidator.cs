using System;
using FluentValidation;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.SetPriority
{
    public class SetPriorityValidator : AbstractValidator<SetPriorityCommand>
    {
        public SetPriorityValidator()
        {
            RuleFor(x => x.TodoId)
                .NotEqual(Guid.Empty).WithMessage("Todo ID cannot be empty");
            
            RuleFor(x => x.Priority)
                .IsInEnum().WithMessage("Invalid priority value");
        }
    }
}

