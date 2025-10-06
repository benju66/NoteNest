using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Notes.Commands.MoveNote
{
	/// <summary>
	/// Command to move a note to a different category.
	/// Follows CQRS pattern, updates physical file location and database.
	/// </summary>
	public class MoveNoteCommand : IRequest<Result<MoveNoteResult>>
	{
		/// <summary>
		/// ID of the note to move
		/// </summary>
		public string NoteId { get; set; }
		
		/// <summary>
		/// ID of the target category (null = move to root/uncategorized)
		/// </summary>
		public string TargetCategoryId { get; set; }
	}

	public class MoveNoteResult
	{
		public bool Success { get; set; }
		public string NoteId { get; set; }
		public string OldCategoryId { get; set; }
		public string NewCategoryId { get; set; }
		public string OldPath { get; set; }
		public string NewPath { get; set; }
	}
}
