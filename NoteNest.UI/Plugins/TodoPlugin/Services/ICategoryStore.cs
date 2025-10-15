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
        
        /// <summary>
        /// Add a category asynchronously (properly waits for DB save and event publish).
        /// Use this for new code to avoid race conditions.
        /// </summary>
        Task AddAsync(Category category);
        
        /// <summary>
        /// Add a category synchronously (legacy - uses fire-and-forget).
        /// Prefer AddAsync for new code.
        /// </summary>
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
