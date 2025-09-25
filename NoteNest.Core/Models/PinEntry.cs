using System;

namespace NoteNest.Core.Models
{
    /// <summary>
    /// Represents a single pinned note entry
    /// </summary>
    public class PinEntry
    {
        public string NoteId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime PinnedAt { get; set; }
        
        public PinEntry()
        {
            PinnedAt = DateTime.UtcNow;
        }
        
        public PinEntry(string noteId, string filePath) : this()
        {
            NoteId = noteId ?? throw new ArgumentNullException(nameof(noteId));
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }
    }
}
