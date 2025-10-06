using FluentValidation;

namespace NoteNest.Application.Notes.Commands.MoveNote
{
	public class MoveNoteValidator : AbstractValidator<MoveNoteCommand>
	{
		public MoveNoteValidator()
		{
			RuleFor(x => x.NoteId)
				.NotEmpty()
				.WithMessage("Note ID is required");

			RuleFor(x => x.TargetCategoryId)
				.NotEmpty()
				.WithMessage("Target category ID is required");
		}
	}
}
