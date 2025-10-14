using System;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence.Models
{
    /// <summary>
    /// DTO for todo_tags table.
    /// Represents the many-to-many relationship between todos and tags.
    /// </summary>
    public class TodoTag
    {
        public Guid TodoId { get; set; }
        public string Tag { get; set; } = string.Empty;
        public bool IsAuto { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}

