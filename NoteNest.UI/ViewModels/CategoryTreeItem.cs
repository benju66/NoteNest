using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Diagnostics;

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
        private bool _disposed;

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
            #if DEBUG
            await EnhancedMemoryTracker.TrackServiceOperationAsync<CategoryTreeItem>("LoadChildren", async () =>
            {
            #endif
                if (_isLoaded || _isLoading || _noteService == null) return;

                await _loadLock.WaitAsync();
                try
                {
                    if (_isLoaded || _isLoading || _noteService == null) return;
                    IsLoading = true;

                    // Load notes for this category
                    var notes = await _noteService.GetNotesInCategoryAsync(_model);
                    // Note pinning removed - will be reimplemented with better architecture later

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
            #if DEBUG
            });
            #endif
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

        private bool _eventsuspended = false;

        private void OnChildrenCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (!_eventsuspended)
            {
                UpdateChildren();
            }
        }

        /// <summary>
        /// Temporarily suspends collection change events to prevent UI storms during bulk operations
        /// </summary>
        public void SuspendCollectionEvents()
        {
            _eventsuspended = true;
        }

        /// <summary>
        /// Resumes collection change events and triggers one update to sync everything
        /// </summary>
        public void ResumeCollectionEvents()
        {
            _eventsuspended = false;
            UpdateChildren(); // Single update after all bulk operations complete
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            try
            {
                // Dispose children first (recursive)
                if (_subCategories != null)
                {
                    foreach (var child in _subCategories.ToList())
                    {
                        child?.Dispose();
                    }
                    _subCategories.CollectionChanged -= OnChildrenCollectionChanged;
                    _subCategories.Clear();
                }
                
                // Dispose notes and unsubscribe from collection events
                if (_notes != null)
                {
                    foreach (var n in _notes.ToList())
                    {
                        try { n?.Dispose(); } catch { }
                    }
                    _notes.CollectionChanged -= OnChildrenCollectionChanged;
                    _notes.Clear();
                }
                
                // Clear combined children collection
                _children?.Clear();
                
                // Dispose synchronization primitives
                _loadLock?.Dispose();
                
                _disposed = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing CategoryTreeItem: {ex.Message}");
            }
        }
    }

    public class NoteTreeItem : ViewModelBase, IDisposable
    {
        private readonly NoteModel _model;
        private readonly PropertyChangedEventHandler _modelPropertyChangedHandler;
        private readonly IPinService _pinService;
        private bool _isVisible = true;
        private bool _isSelected;
        private bool _isDirty;
        private bool _isPinned;

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

        public bool IsPinned
        {
            get => _isPinned;
            private set => SetProperty(ref _isPinned, value);
        }

        public NoteTreeItem(NoteModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            
            // Get pin service from DI container
            var app = Application.Current as App;
            _pinService = app?.ServiceProvider?.GetService<IPinService>();
            
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
            
            // Initialize pin state and subscribe to changes
            if (_pinService != null)
            {
                _pinService.PinChanged += OnPinChanged;
                _ = LoadInitialPinStateAsync();
            }
        }

        // Public wrapper to notify property changes from outside this class
        public new void OnPropertyChanged(string propertyName)
        {
            base.OnPropertyChanged(propertyName);
        }

        private async Task LoadInitialPinStateAsync()
        {
            try
            {
                if (_pinService != null && !string.IsNullOrEmpty(_model.Id))
                {
                    IsPinned = await _pinService.IsPinnedAsync(_model.Id);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash - pin state is not critical for basic functionality
                System.Diagnostics.Debug.WriteLine($"Error loading initial pin state for {_model.Title}: {ex.Message}");
            }
        }

        private void OnPinChanged(object sender, PinChangedEventArgs e)
        {
            try
            {
                if (e.NoteId == _model.Id)
                {
                    // Ensure UI update happens on UI thread
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        IsPinned = e.IsPinned;
                        System.Diagnostics.Debug.WriteLine($"NoteTreeItem: Updated pin state for {_model.Title} to {e.IsPinned}");
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling pin change event for {_model.Title}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_model is INotifyPropertyChanged inpc)
            {
                inpc.PropertyChanged -= _modelPropertyChangedHandler;
            }

            if (_pinService != null)
            {
                _pinService.PinChanged -= OnPinChanged;
            }
        }
    }
}