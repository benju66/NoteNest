using System.Collections.ObjectModel;
using NoteNest.Core.Models;

namespace NoteNest.UI.ViewModels
{
    public class CategoryTreeItem : ViewModelBase
    {
        private readonly CategoryModel _model;
        private ObservableCollection<NoteTreeItem> _notes;
        private bool _isExpanded;
        private bool _isVisible = true;

        public CategoryModel Model => _model;

        public string Name => _model.Name;
        public bool Pinned => _model.Pinned;
        public string Path => _model.Path;

        public ObservableCollection<NoteTreeItem> Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public CategoryTreeItem(CategoryModel model)
        {
            _model = model;
            _notes = new ObservableCollection<NoteTreeItem>();
            _isExpanded = true;
        }
    }

    public class NoteTreeItem : ViewModelBase
    {
        private readonly NoteModel _model;
        private bool _isVisible = true;

        public NoteModel Model => _model;
        public string Title => _model.Title;
        public string FilePath => _model.FilePath;

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public NoteTreeItem(NoteModel model)
        {
            _model = model;
        }
    }
}
