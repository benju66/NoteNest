using System;

namespace NoteNest.UI.Plugins.TodoPlugin.Events
{
    /// <summary>
    /// Event published when a category is deleted from the todo category store.
    /// Allows TodoStore to clean up orphaned todos by setting their category_id = NULL.
    /// </summary>
    public class CategoryDeletedEvent
    {
        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; }
        
        public CategoryDeletedEvent(Guid categoryId, string categoryName)
        {
            CategoryId = categoryId;
            CategoryName = categoryName;
        }
    }
    
    /// <summary>
    /// Event published when a category is added to the todo category store.
    /// Can be used for UI refresh or analytics.
    /// </summary>
    public class CategoryAddedEvent
    {
        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; }
        
        public CategoryAddedEvent(Guid categoryId, string categoryName)
        {
            CategoryId = categoryId;
            CategoryName = categoryName;
        }
    }
}

