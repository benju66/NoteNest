using FluentValidation;

namespace NoteNest.Application.Categories.Commands.RenameCategory
{
    /// <summary>
    /// Validates RenameCategoryCommand before execution.
    /// </summary>
    public class RenameCategoryValidator : AbstractValidator<RenameCategoryCommand>
    {
        public RenameCategoryValidator()
        {
            RuleFor(x => x.CategoryId)
                .NotEmpty()
                .WithMessage("Category ID is required");

            RuleFor(x => x.NewName)
                .NotEmpty()
                .WithMessage("Category name is required")
                .MaximumLength(255)
                .WithMessage("Category name cannot exceed 255 characters")
                .Must(BeValidDirectoryName)
                .WithMessage("Category name contains invalid characters");
        }

        private bool BeValidDirectoryName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                if (name.Contains(c))
                    return false;
            }

            return true;
        }
    }
}

