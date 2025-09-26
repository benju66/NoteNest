using NoteNest.Application.Common.Interfaces;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.UI.ViewModels.Categories
{
    public class CategoryViewModel : ViewModelBase
    {
        private readonly Category _category;

        public CategoryViewModel(Category category)
        {
            _category = category;
        }

        public string Id => _category.Id.Value;
        public string Name => _category.Name;
        public string Path => _category.Path;
        public bool IsRoot => _category.ParentId == null;
    }
}
