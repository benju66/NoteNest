using System;
using System.Collections.Generic;

namespace NoteNest.Core.Models
{
    public class NoteMetadata
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string FilePath { get; set; }
        public DateTime LastModified { get; set; }
        public string CategoryId { get; set; }
        public List<string> Tags { get; set; }
        public long FileSize { get; set; }
        public string FileHash { get; set; }

        public NoteMetadata()
        {
            Tags = new List<string>();
            Id = Guid.NewGuid().ToString();
        }
    }
}
