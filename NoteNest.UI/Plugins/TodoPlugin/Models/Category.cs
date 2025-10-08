using System;

namespace NoteNest.UI.Plugins.TodoPlugin.Models
{
    /// <summary>
    /// Simple DTO for a Todo category.
    /// </summary>
    public class Category
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? ParentId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Order { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
    }
}
