using System;
using System.Collections.ObjectModel;
using System.Linq;
using NoteNest.Domain.Categories;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.UI.ViewModels.Categories
{
    public class CategoryViewModel : ViewModelBase
    {
        private readonly Category _category;

        public CategoryViewModel(Category category)
        {
            _category = category ?? throw new ArgumentNullException(nameof(category));
            Children = new ObservableCollection<CategoryViewModel>();
        }

        public string Id => _category.Id.Value;
        public string Name => _category.Name;
        public string Path => _category.Path;
        public string ParentId => _category.ParentId?.Value;
        public bool IsRoot => _category.ParentId == null;
        
        public ObservableCollection<CategoryViewModel> Children { get; }
    }
}