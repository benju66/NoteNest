using System;
using FluentValidation;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.SetDueDate
{
    public class SetDueDateValidator : AbstractValidator<SetDueDateCommand>
    {
        public SetDueDateValidator()
        {
            RuleFor(x => x.TodoId)
                .NotEqual(Guid.Empty).WithMessage("Todo ID cannot be empty");
            
            // DueDate is optional (null = clear due date)
            // Allow past dates (user might be logging completed work with historical due dates)
        }
    }
}

