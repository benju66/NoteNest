using System;

namespace NoteNest.Core.Events
{
	public class CategoryRenamedEvent
	{
		public string OldName { get; set; } = string.Empty;
		public string NewName { get; set; } = string.Empty;
		public string CategoryId { get; set; } = string.Empty;
		public DateTime Timestamp { get; set; } = DateTime.UtcNow;
	}
}


