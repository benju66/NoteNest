using System;
using System.Collections.Generic;

namespace NoteNest.Core.Models
{
    public class NoteMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        public string CategoryId { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public long FileSize { get; set; }
        public string FileHash { get; set; } = string.Empty;

        public NoteMetadata()
        {
            Id = Guid.NewGuid().ToString();
        }
    }
}
