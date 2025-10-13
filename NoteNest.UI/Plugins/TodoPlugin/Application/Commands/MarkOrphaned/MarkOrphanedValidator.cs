using System;
using FluentValidation;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.MarkOrphaned
{
    public class MarkOrphanedValidator : AbstractValidator<MarkOrphanedCommand>
    {
        public MarkOrphanedValidator()
        {
            RuleFor(x => x.TodoId)
                .NotEqual(Guid.Empty).WithMessage("Todo ID cannot be empty");
        }
    }
}

