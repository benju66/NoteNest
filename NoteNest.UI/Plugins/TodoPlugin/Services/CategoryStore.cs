using System;
using System.Collections.ObjectModel;
using System.Linq;
using NoteNest.UI.Collections;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Services
{
    /// <summary>
    /// Simple in-memory implementation of ICategoryStore.
    /// </summary>
    public class CategoryStore : ICategoryStore
    {
        private readonly SmartObservableCollection<Category> _categories;

        public CategoryStore()
        {
            _categories = new SmartObservableCollection<Category>();
            
            // Add some default categories
            Add(new Category { Name = "Personal" });
            Add(new Category { Name = "Work" });
            Add(new Category { Name = "Shopping" });
        }

        public ObservableCollection<Category> Categories => _categories;

        public Category? GetById(Guid id)
        {
            return _categories.FirstOrDefault(c => c.Id == id);
        }

        public void Add(Category category)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));
            _categories.Add(category);
        }

        public void Update(Category category)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));
            
            var existing = GetById(category.Id);
            if (existing != null)
            {
                var index = _categories.IndexOf(existing);
                _categories[index] = category;
            }
        }

        public void Delete(Guid id)
        {
            var category = GetById(id);
            if (category != null)
            {
                _categories.Remove(category);
            }
        }
    }
}
