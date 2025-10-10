using System;

namespace NoteNest.UI.Plugins.TodoPlugin.Models
{
    /// <summary>
    /// Todo category model with support for both flat and hierarchical display.
    /// Designed for easy migration between display modes.
    /// </summary>
    public class Category
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// Current parent ID for display purposes.
        /// NULL = show at root level (flat mode).
        /// Set to OriginalParentId to enable hierarchical mode.
        /// </summary>
        public Guid? ParentId { get; set; }
        
        /// <summary>
        /// Original parent ID from note tree structure.
        /// Preserved for future hierarchical reorganization (Option 3).
        /// Never modified - represents true tree relationship.
        /// </summary>
        public Guid? OriginalParentId { get; set; }
        
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Full breadcrumb path for rich display.
        /// Example: "Work > Projects > ProjectAlpha"
        /// Useful in both flat and hierarchical modes.
        /// </summary>
        public string DisplayPath { get; set; } = string.Empty;
        
        public int Order { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
    }
}
