using System;

namespace NoteNest.Core.Events
{
	public class NoteMovedEvent
	{
		public string NoteId { get; set; } = string.Empty;
		public string OldPath { get; set; } = string.Empty;
		public string NewPath { get; set; } = string.Empty;
		public DateTime MovedAt { get; set; } = DateTime.UtcNow;
	}
}


