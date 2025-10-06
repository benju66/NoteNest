using FluentValidation;

namespace NoteNest.Application.Categories.Commands.MoveCategory
{
	public class MoveCategoryValidator : AbstractValidator<MoveCategoryCommand>
	{
		public MoveCategoryValidator()
		{
			RuleFor(x => x.CategoryId)
				.NotEmpty()
				.WithMessage("Category ID is required");

			// NewParentId can be null (move to root)
		}
	}
}
