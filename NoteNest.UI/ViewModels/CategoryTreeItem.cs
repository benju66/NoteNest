using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using NoteNest.Core.Models;

namespace NoteNest.UI.ViewModels
{
    public class CategoryTreeItem : ViewModelBase, IDisposable
    {
        private readonly CategoryModel _model;
        private ObservableCollection<CategoryTreeItem> _subCategories;
        private ObservableCollection<NoteTreeItem> _notes;
        private ObservableCollection<object> _children;
        private bool _isExpanded;
        private bool _isVisible = true;

        public CategoryModel Model => _model;

        public string Name => _model.Name;
        public bool Pinned => _model.Pinned;
        public string Path => _model.Path;
        public string ParentId => _model.ParentId;
        public int Level => _model.Level;

        public ObservableCollection<CategoryTreeItem> SubCategories
        {
            get => _subCategories;
            set { SetProperty(ref _subCategories, value); UpdateChildren(); }
        }
        public ObservableCollection<NoteTreeItem> Notes
        {
            get => _notes;
            set { SetProperty(ref _notes, value); UpdateChildren(); }
        }

        public ObservableCollection<object> Children
        {
            get => _children;
            private set => SetProperty(ref _children, value);
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
            _subCategories = new ObservableCollection<CategoryTreeItem>();
            _notes = new ObservableCollection<NoteTreeItem>();
            _children = new ObservableCollection<object>();
            _isExpanded = Level < 2;
            // Keep Children in sync when items change within collections
            _subCategories.CollectionChanged += OnChildrenCollectionChanged;
            _notes.CollectionChanged += OnChildrenCollectionChanged;
            UpdateChildren();
        }

        private void UpdateChildren()
        {
            var combined = new ObservableCollection<object>();
            foreach (var cat in SubCategories ?? new ObservableCollection<CategoryTreeItem>())
            {
                combined.Add(cat);
            }
            foreach (var note in Notes ?? new ObservableCollection<NoteTreeItem>())
            {
                combined.Add(note);
            }
            Children = combined;
        }

        private void OnChildrenCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateChildren();
        }

        // Level is set during tree building via the model
		public void Dispose()
		{
			if (_subCategories != null)
				_subCategories.CollectionChanged -= OnChildrenCollectionChanged;
			if (_notes != null)
				_notes.CollectionChanged -= OnChildrenCollectionChanged;
		}
    }

    public class NoteTreeItem : ViewModelBase, IDisposable
    {
        private readonly NoteModel _model;
        private readonly PropertyChangedEventHandler _modelPropertyChangedHandler;
        private bool _isVisible = true;
        private bool _isSelected;

        public NoteModel Model => _model;
        public string Title => _model.Title;
        public string FilePath => _model.FilePath;
        public string CategoryId => _model.CategoryId;

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public NoteTreeItem(NoteModel model)
        {
            _model = model;
            _modelPropertyChangedHandler = (s, e) =>
            {
                if (e.PropertyName == nameof(NoteModel.Title))
                {
                    OnPropertyChanged(nameof(Title));
                }
                else if (e.PropertyName == nameof(NoteModel.FilePath))
                {
                    OnPropertyChanged(nameof(FilePath));
                }
            };

            if (_model is INotifyPropertyChanged inpc)
            {
                inpc.PropertyChanged += _modelPropertyChangedHandler;
            }
        }

        public void Dispose()
        {
            if (_model is INotifyPropertyChanged inpc)
            {
                inpc.PropertyChanged -= _modelPropertyChangedHandler;
            }
        }
    }
}
