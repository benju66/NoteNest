using System;
using FluentValidation;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.DeleteTodo
{
    public class DeleteTodoValidator : AbstractValidator<DeleteTodoCommand>
    {
        public DeleteTodoValidator()
        {
            RuleFor(x => x.TodoId)
                .NotEqual(Guid.Empty).WithMessage("Todo ID cannot be empty");
        }
    }
}

