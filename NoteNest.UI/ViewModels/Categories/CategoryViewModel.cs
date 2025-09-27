using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using NoteNest.Domain.Categories;
using NoteNest.UI.ViewModels.Common;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Notes;
using System.Collections.Generic;
using NoteNest.Core.Commands;

namespace NoteNest.UI.ViewModels.Categories
{
    public class CategoryViewModel : ViewModelBase
    {
        private readonly Category _category;
        private readonly INoteRepository _noteRepository;
        private readonly IAppLogger _logger;
        private bool _isExpanded;
        private bool _isLoading;
        private bool _notesLoaded;

        public CategoryViewModel(Category category, INoteRepository noteRepository = null, IAppLogger logger = null)
        {
            _category = category ?? throw new ArgumentNullException(nameof(category));
            _noteRepository = noteRepository;
            _logger = logger;
            
            Children = new ObservableCollection<CategoryViewModel>();
            Notes = new ObservableCollection<NoteItemViewModel>();
            
            // Initialize commands
            ExpandCommand = new RelayCommand(async () => await ExpandAsync(), () => HasChildren);
            CollapseCommand = new RelayCommand(() => Collapse());
            ToggleExpandCommand = new RelayCommand(async () => await ToggleExpandAsync());
        }

        public string Id => _category.Id.Value;
        public string Name => _category.Name;
        public string Path => _category.Path;
        public string ParentId => _category.ParentId?.Value;
        public bool IsRoot => _category.ParentId == null;
        
        public ObservableCollection<CategoryViewModel> Children { get; }
        public ObservableCollection<NoteItemViewModel> Notes { get; }
        
        public bool HasChildren => Children.Any();
        public bool HasNotes => Notes.Any();
        public bool HasContent => HasChildren || HasNotes;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value))
                {
                    OnPropertyChanged(nameof(ExpanderVisibility));
                    if (value && !_notesLoaded)
                    {
                        _ = LoadNotesAsync();
                    }
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string ExpanderVisibility => HasContent ? "Visible" : "Collapsed";
        public string ExpanderIcon => IsExpanded ? "▼" : "▶";
        public string CategoryIcon => IsExpanded ? "[Open]" : "[Folder]";

        // Commands for tree interaction
        public ICommand ExpandCommand { get; }
        public ICommand CollapseCommand { get; }
        public ICommand ToggleExpandCommand { get; }

        public async Task ExpandAsync()
        {
            if (IsExpanded) return;
            
            IsExpanded = true;
            
            if (!_notesLoaded)
            {
                await LoadNotesAsync();
            }
        }

        public void Collapse()
        {
            IsExpanded = false;
        }

        public async Task ToggleExpandAsync()
        {
            if (IsExpanded)
            {
                Collapse();
            }
            else
            {
                await ExpandAsync();
            }
        }

        private async Task LoadNotesAsync()
        {
            if (_noteRepository == null || _notesLoaded) return;
            
            try
            {
                IsLoading = true;
                _logger?.Info($"Loading notes for category: {Name}");
                
                var categoryId = NoteNest.Domain.Categories.CategoryId.From(Id);
                var notes = await _noteRepository.GetByCategoryAsync(categoryId);
                
                Notes.Clear();
                foreach (var note in notes)
                {
                    Notes.Add(new NoteItemViewModel(note));
                }
                
                _notesLoaded = true;
                _logger?.Info($"Loaded {Notes.Count} notes for category: {Name}");
                
                OnPropertyChanged(nameof(HasNotes));
                OnPropertyChanged(nameof(HasContent));
                OnPropertyChanged(nameof(ExpanderVisibility));
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to load notes for category: {Name}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void RefreshNotes()
        {
            _notesLoaded = false;
            Notes.Clear();
            if (IsExpanded)
            {
                _ = LoadNotesAsync();
            }
        }
    }

    // Simple ViewModel for notes within categories
    public class NoteItemViewModel : ViewModelBase
    {
        private readonly Note _note;

        public NoteItemViewModel(Note note)
        {
            _note = note ?? throw new ArgumentNullException(nameof(note));
        }

        public string Id => _note.Id.Value;
        public string Title => _note.Title;
        public string FilePath => _note.FilePath;
        public bool IsPinned => _note.IsPinned;
        public string NoteIcon => IsPinned ? "[Pinned]" : "[Note]";
        public DateTime CreatedAt => _note.CreatedAt;
        public DateTime UpdatedAt => _note.UpdatedAt;
    }
}