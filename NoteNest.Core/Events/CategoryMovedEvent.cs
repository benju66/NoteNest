using System;

namespace NoteNest.Core.Events
{
	/// <summary>
	/// Event published when a category is moved to a new parent location.
	/// Used to notify UI and other components of category hierarchy changes.
	/// </summary>
	public class CategoryMovedEvent
	{
		public string CategoryId { get; set; } = string.Empty;
		public string CategoryName { get; set; } = string.Empty;
		public string OldParentId { get; set; } = string.Empty;
		public string NewParentId { get; set; } = string.Empty;
		public DateTime MovedAt { get; set; } = DateTime.UtcNow;
	}
}
