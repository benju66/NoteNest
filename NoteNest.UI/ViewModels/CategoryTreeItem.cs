using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Models;
using NoteNest.Core.Services;

namespace NoteNest.UI.ViewModels
{
    public class CategoryTreeItem : ViewModelBase, IDisposable
    {
        private readonly CategoryModel _model;
        private readonly NoteService _noteService;
        private ObservableCollection<CategoryTreeItem> _subCategories;
        private ObservableCollection<NoteTreeItem> _notes;
        private ObservableCollection<object> _children;
        private bool _isExpanded;
        private bool _isVisible = true;
        private bool _isLoaded = false;
        private bool _isLoading = false;
        private readonly System.Threading.SemaphoreSlim _loadLock = new System.Threading.SemaphoreSlim(1, 1);

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
            set
            {
                if (SetProperty(ref _isExpanded, value))
                {
                    if (value && !_isLoaded && !_isLoading && _noteService != null)
                    {
                        _ = LoadChildrenAsync();
                    }
                }
            }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsLoaded
        {
            get => _isLoaded;
            set => SetProperty(ref _isLoaded, value);
        }

        public CategoryTreeItem(CategoryModel model, NoteService noteService = null)
        {
            _model = model;
            _noteService = noteService;
            _subCategories = new ObservableCollection<CategoryTreeItem>();
            _notes = new ObservableCollection<NoteTreeItem>();
            _children = new ObservableCollection<object>();
            _isExpanded = Level < 2;
            
            _subCategories.CollectionChanged += OnChildrenCollectionChanged;
            _notes.CollectionChanged += OnChildrenCollectionChanged;
            
            // Only load immediate children if expanded by default
            if (_isExpanded && _noteService != null)
            {
                _ = LoadChildrenAsync();
            }
            else
            {
                // Update children for structure
                UpdateChildren();
            }
        }

        // Public wrapper to notify property changes from outside this class
        public new void OnPropertyChanged(string propertyName)
        {
            base.OnPropertyChanged(propertyName);
        }

        public async Task LoadChildrenAsync()
        {
            if (_isLoaded || _isLoading || _noteService == null) return;

            await _loadLock.WaitAsync();
            try
            {
                if (_isLoaded || _isLoading || _noteService == null) return;
                IsLoading = true;

                // Load notes for this category
                var notes = await _noteService.GetNotesInCategoryAsync(_model);
                // Order pinned notes first using NotePinService (if available via App DI)
                try
                {
                    var pin = (System.Windows.Application.Current as NoteNest.UI.App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.NotePinService)) as NoteNest.Core.Services.NotePinService;
                    if (pin != null)
                    {
                        notes = notes
                            .OrderByDescending(n => pin.IsPinned(n.FilePath))
                            .ThenBy(n => n.Title)
                            .ToList();
                    }
                }
                catch { }

                // Defensive: ensure we don't add duplicates
                var existingById = new HashSet<string>(Notes.Select(n => n.Model.Id), StringComparer.OrdinalIgnoreCase);
                var existingByPath = new HashSet<string>(Notes.Select(n => n.Model.FilePath ?? string.Empty), StringComparer.OrdinalIgnoreCase);

                foreach (var note in notes)
                {
                    if (IsNoteDuplicate(note, existingById, existingByPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"Prevented duplicate: id={note?.Id} path={note?.FilePath}");
                        continue;
                    }
                    Notes.Add(new NoteTreeItem(note));
                }

                _isLoaded = true;
                OnPropertyChanged(nameof(IsLoaded));
            }
            catch (Exception ex)
            {
                // Log error
                System.Diagnostics.Debug.WriteLine($"Error loading children for {Name}: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                _loadLock.Release();
            }
        }

        public async Task ReloadNotesAsync()
        {
            await _loadLock.WaitAsync();
            try
            {
                _isLoaded = false;
                OnPropertyChanged(nameof(IsLoaded));
                Notes.Clear();
            }
            finally
            {
                _loadLock.Release();
            }
            await LoadChildrenAsync();
        }

        private static bool IsNoteDuplicate(NoteNest.Core.Models.NoteModel note, ISet<string> existingIds, ISet<string> existingPaths)
        {
            if (note == null) return false;
            var path = note.FilePath ?? string.Empty;
            return existingIds.Contains(note.Id) || existingPaths.Contains(path);
        }

        private void UpdateChildren()
        {
            if (_children == null)
                _children = new ObservableCollection<object>();
            else
                _children.Clear();

            foreach (var cat in SubCategories ?? Enumerable.Empty<CategoryTreeItem>())
            {
                _children.Add(cat);
            }
            foreach (var note in Notes ?? Enumerable.Empty<NoteTreeItem>())
            {
                _children.Add(note);
            }

            OnPropertyChanged(nameof(Children));
        }

        private void OnChildrenCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateChildren();
        }

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
        private bool _isDirty;

        public NoteModel Model => _model;
        public string Title => _model.Title;
        public string FilePath => _model.FilePath;
        public string CategoryId => _model.CategoryId;
        public bool IsDirty
        {
            get => _isDirty;
            private set => SetProperty(ref _isDirty, value);
        }

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
                else if (e.PropertyName == nameof(NoteModel.IsDirty))
                {
                    IsDirty = _model.IsDirty;
                }
            };

            if (_model is INotifyPropertyChanged inpc)
            {
                inpc.PropertyChanged += _modelPropertyChangedHandler;
            }

            // Initialize dirty state
            _isDirty = _model.IsDirty;
        }

        // Public wrapper to notify property changes from outside this class
        public new void OnPropertyChanged(string propertyName)
        {
            base.OnPropertyChanged(propertyName);
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