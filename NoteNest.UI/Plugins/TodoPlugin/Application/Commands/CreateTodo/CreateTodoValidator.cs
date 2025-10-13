using FluentValidation;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.CreateTodo
{
    public class CreateTodoValidator : AbstractValidator<CreateTodoCommand>
    {
        public CreateTodoValidator()
        {
            RuleFor(x => x.Text)
                .NotEmpty().WithMessage("Todo text is required")
                .MaximumLength(500).WithMessage("Todo text cannot exceed 500 characters");
            
            // CategoryId is optional (null = uncategorized)
            // SourceNoteId is optional (null = manual creation)
        }
    }
}

