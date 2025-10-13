using System;
using FluentValidation;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.CompleteTodo
{
    public class CompleteTodoValidator : AbstractValidator<CompleteTodoCommand>
    {
        public CompleteTodoValidator()
        {
            RuleFor(x => x.TodoId)
                .NotEqual(Guid.Empty).WithMessage("Todo ID cannot be empty");
        }
    }
}

