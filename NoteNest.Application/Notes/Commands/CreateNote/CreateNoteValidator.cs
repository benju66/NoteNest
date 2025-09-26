using FluentValidation;

namespace NoteNest.Application.Notes.Commands.CreateNote
{
    public class CreateNoteValidator : AbstractValidator<CreateNoteCommand>
    {
        public CreateNoteValidator()
        {
            RuleFor(x => x.CategoryId)
                .NotEmpty().WithMessage("Category is required");

            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Title is required")
                .MaximumLength(255).WithMessage("Title cannot exceed 255 characters")
                .Matches(@"^[^<>:""/\\|?*]+$").WithMessage("Title contains invalid characters");
        }
    }
}
