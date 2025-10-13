using System;
using FluentValidation;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.UpdateTodoText
{
    public class UpdateTodoTextValidator : AbstractValidator<UpdateTodoTextCommand>
    {
        public UpdateTodoTextValidator()
        {
            RuleFor(x => x.TodoId)
                .NotEqual(Guid.Empty).WithMessage("Todo ID cannot be empty");
            
            RuleFor(x => x.NewText)
                .NotEmpty().WithMessage("Todo text is required")
                .MaximumLength(500).WithMessage("Todo text cannot exceed 500 characters");
        }
    }
}

