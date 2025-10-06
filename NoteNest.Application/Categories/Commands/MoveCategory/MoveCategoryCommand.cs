using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Categories.Commands.MoveCategory
{
	/// <summary>
	/// Command to move a category to a new parent location.
	/// Updates database parent_id relationship (does NOT move physical folder by default).
	/// 
	/// Design Decision: Physical folders stay in place, only logical hierarchy changes.
	/// This matches the rename behavior where display name != folder name.
	/// </summary>
	public class MoveCategoryCommand : IRequest<Result<MoveCategoryResult>>
	{
		/// <summary>
		/// ID of the category to move
		/// </summary>
		public string CategoryId { get; set; }
		
		/// <summary>
		/// ID of the new parent category (null = move to root)
		/// </summary>
		public string NewParentId { get; set; }
	}

	public class MoveCategoryResult
	{
		public bool Success { get; set; }
		public string CategoryId { get; set; }
		public string CategoryName { get; set; }
		public string OldParentId { get; set; }
		public string NewParentId { get; set; }
		public int AffectedDescendantCount { get; set; }
	}
}
