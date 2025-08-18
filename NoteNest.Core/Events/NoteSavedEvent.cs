using System;

namespace NoteNest.Core.Events
{
    public class NoteSavedEvent
    {
        public string FilePath { get; set; } = string.Empty;
        public DateTime SavedAt { get; set; }
    }
}


