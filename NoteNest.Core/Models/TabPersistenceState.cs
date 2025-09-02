using System;
using System.Collections.Generic;

namespace NoteNest.Core.Models
{
	public class TabPersistenceState
	{
		public int Version { get; set; } = 1;
		public List<TabInfo> Tabs { get; set; } = new();
		public string? ActiveTabId { get; set; }
		public string? ActiveTabContent { get; set; }
		public DateTime LastSaved { get; set; }
	}

	public class TabInfo
	{
		public string Id { get; set; }
		public string Path { get; set; }
		public string Title { get; set; }
		public bool IsDirty { get; set; }
	}
}


