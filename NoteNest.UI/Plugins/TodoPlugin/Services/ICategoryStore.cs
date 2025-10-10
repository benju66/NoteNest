using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Services
{
    /// <summary>
    /// Interface for Category data store.
    /// Now supports async initialization from tree database.
    /// </summary>
    public interface ICategoryStore
    {
        ObservableCollection<Category> Categories { get; }
        Category? GetById(Guid id);
        void Add(Category category);
        void Update(Category category);
        void Delete(Guid id);
        
        /// <summary>
        /// Initialize store by loading categories from tree database.
        /// Call once during plugin startup.
        /// </summary>
        Task InitializeAsync();
        
        /// <summary>
        /// Refresh categories from tree database.
        /// Call when tree structure changes (category created/deleted/renamed).
        /// </summary>
        Task RefreshAsync();
    }
}
