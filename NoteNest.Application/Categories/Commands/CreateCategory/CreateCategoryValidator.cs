using FluentValidation;

namespace NoteNest.Application.Categories.Commands.CreateCategory
{
    /// <summary>
    /// Validates CreateCategoryCommand before execution.
    /// Ensures category name meets business rules.
    /// </summary>
    public class CreateCategoryValidator : AbstractValidator<CreateCategoryCommand>
    {
        public CreateCategoryValidator()
        {
            RuleFor(x => x.Name)
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

            // Check for invalid directory name characters
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

