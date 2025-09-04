using System;

namespace NoteNest.Core.Events
{
	public class NoteRenamedEvent
	{
		public string NoteId { get; set; } = string.Empty;
		public string OldPath { get; set; } = string.Empty;
		public string NewPath { get; set; } = string.Empty;
		public string OldTitle { get; set; } = string.Empty;
		public string NewTitle { get; set; } = string.Empty;
		public DateTime RenamedAt { get; set; } = DateTime.UtcNow;
	}
}


