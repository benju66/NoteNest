using System;
using System.Collections.ObjectModel;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Services
{
    /// <summary>
    /// Interface for Category data store.
    /// </summary>
    public interface ICategoryStore
    {
        ObservableCollection<Category> Categories { get; }
        Category? GetById(Guid id);
        void Add(Category category);
        void Update(Category category);
        void Delete(Guid id);
    }
}
