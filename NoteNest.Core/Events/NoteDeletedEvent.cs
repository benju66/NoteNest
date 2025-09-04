using System;

namespace NoteNest.Core.Events
{
	public class NoteDeletedEvent
	{
		public string NoteId { get; set; } = string.Empty;
		public string FilePath { get; set; } = string.Empty;
		public DateTime DeletedAt { get; set; } = DateTime.UtcNow;
	}
}


