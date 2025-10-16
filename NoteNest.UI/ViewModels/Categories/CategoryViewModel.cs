using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using NoteNest.Domain.Categories;
using NoteNest.UI.ViewModels.Common;
using NoteNest.UI.Collections;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Notes;
using System.Collections.Generic;
using NoteNest.Core.Commands;
using System.Collections.Specialized;
using System.IO;

namespace NoteNest.UI.ViewModels.Categories
{
    public class CategoryViewModel : ViewModelBase
    {
        private readonly Category _category;
        private readonly INoteRepository _noteRepository;
        private readonly CategoryTreeViewModel _parentTree;
        private readonly IAppLogger _logger;
        private bool _isExpanded;
        private bool _isLoading;
        private bool _notesLoaded;

        public CategoryViewModel(
            Category category, 
            INoteRepository noteRepository = null, 
            CategoryTreeViewModel parentTree = null,
            IAppLogger logger = null)
        {
            _category = category ?? throw new ArgumentNullException(nameof(category));
            _noteRepository = noteRepository;
            _parentTree = parentTree;
            _logger = logger;
            
            Children = new SmartObservableCollection<CategoryViewModel>();
            Notes = new SmartObservableCollection<NoteItemViewModel>();
            TreeItems = new SmartObservableCollection<object>();
            
            // Subscribe to collection changes to update TreeItems
            Children.CollectionChanged += (s, e) => UpdateTreeItems();
            Notes.CollectionChanged += (s, e) => UpdateTreeItems();
            
            // Initialize commands
            ExpandCommand = new RelayCommand(async () => await ExpandAsync(), () => true);
            CollapseCommand = new RelayCommand(() => Collapse());
            ToggleExpandCommand = new RelayCommand(async () => await ToggleExpandAsync());
            
            // DON'T load notes immediately - use lazy loading only when expanded
        }

        public string Id => _category.Id.Value;
        public string Name => _category.Name;
        public string Path => _category.Path;
        public string ParentId => _category.ParentId?.Value;
        public bool IsRoot => _category.ParentId == null;
        
        /// <summary>
        /// Formatted breadcrumb path for tooltips (e.g., "Notes > Projects > 25-117")
        /// </summary>
        public string BreadcrumbPath
        {
            get
            {
                try
                {
                    if (string.IsNullOrEmpty(Path)) return Name;
                    
                    // Get the relative path components from the notes root
                    var parts = Path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    // Find where "Notes" starts or use last 3-4 parts for reasonable display
                    var notesIndex = Array.FindIndex(parts, p => p.Equals("Notes", StringComparison.OrdinalIgnoreCase));
                    var startIndex = notesIndex >= 0 ? notesIndex : Math.Max(0, parts.Length - 4);
                    
                    var relevantParts = parts.Skip(startIndex).ToArray();
                    return string.Join(" > ", relevantParts);
                }
                catch
                {
                    return Name;
                }
            }
        }
        
        public SmartObservableCollection<CategoryViewModel> Children { get; }
        public SmartObservableCollection<NoteItemViewModel> Notes { get; }
        
        // Composite collection for TreeView binding - combines children and notes
        public SmartObservableCollection<object> TreeItems { get; private set; }
        
        public bool HasChildren => Children.Any();
        public bool HasNotes => Notes.Any();
        public bool HasContent => HasChildren || HasNotes || HasPotentialContent;
        
        // Check if directory might have content without scanning
        public bool HasPotentialContent
        {
            get
            {
                try
                {
                    if (!Directory.Exists(this.Path)) return false;
                    
                    // Quick check for subdirectories
                    var hasSubdirs = Directory.GetDirectories(this.Path).Any(d => !System.IO.Path.GetFileName(d).StartsWith("."));
                    
                    // Quick check for note files without full scan
                    var hasFiles = Directory.GetFiles(this.Path, "*.txt").Any() || 
                                   Directory.GetFiles(this.Path, "*.rtf").Any() || 
                                   Directory.GetFiles(this.Path, "*.md").Any();
                    
                    return hasSubdirs || hasFiles;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value))
                {
                    // Notify parent tree to persist state (debounced)
                    _parentTree?.OnCategoryExpandedChanged(Id, value);
                    
                    OnPropertyChanged(nameof(ExpanderVisibility));
                    OnPropertyChanged(nameof(ExpanderIcon));
                    
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

        public string ExpanderVisibility => HasPotentialContent ? "Visible" : "Collapsed";
        public string ExpanderIcon => IsExpanded ? "▼" : "▶";

        // Commands for tree interaction
        public ICommand ExpandCommand { get; }
        public ICommand CollapseCommand { get; }
        public ICommand ToggleExpandCommand { get; }

        // Events for note interaction (bubble up to tree level)
        public event Action<NoteItemViewModel> NoteOpenRequested;
        public event Action<NoteItemViewModel> NoteSelectionRequested;

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
                
                var noteViewModels = notes.Select(note => {
                    var noteViewModel = new NoteItemViewModel(note);
                    // Wire up note events to bubble up to tree level
                    noteViewModel.OpenRequested += OnNoteOpenRequested;
                    noteViewModel.SelectionRequested += OnNoteSelectionRequested;
                    return noteViewModel;
                }).ToList();

                Notes.ReplaceAll(noteViewModels);
                
                _notesLoaded = true;
                _logger?.Info($"Loaded {Notes.Count} notes for category: {Name}");
                
                OnPropertyChanged(nameof(HasNotes));
                OnPropertyChanged(nameof(HasContent));
                OnPropertyChanged(nameof(ExpanderVisibility));
                
                UpdateTreeItems();
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

        public async Task RefreshNotesAsync()
        {
            _notesLoaded = false;
            using (Notes.BatchUpdate())
            {
                Notes.Clear();
                if (IsExpanded)
                {
                    await LoadNotesAsync();
                }
            }
            UpdateTreeItems();
        }

        private void UpdateTreeItems()
        {
            using (TreeItems.BatchUpdate())
            {
                TreeItems.Clear();
                
                // Add child categories first
                foreach (var child in Children)
                {
                    TreeItems.Add(child);
                }
                
                // Add notes second
                foreach (var note in Notes)
                {
                    TreeItems.Add(note);
                }
            }
            
            // Update UI binding properties
            OnPropertyChanged(nameof(HasChildren));
            OnPropertyChanged(nameof(HasNotes));
            OnPropertyChanged(nameof(HasContent));
        }

        // =============================================================================
        // NOTE EVENT HANDLERS - Bubble events up to tree level
        // =============================================================================

        private void OnNoteOpenRequested(NoteItemViewModel note)
        {
            NoteOpenRequested?.Invoke(note);
        }

        private void OnNoteSelectionRequested(NoteItemViewModel note)
        {
            NoteSelectionRequested?.Invoke(note);
        }
    }

    // Enhanced ViewModel for notes within categories with interaction support
    public class NoteItemViewModel : ViewModelBase
    {
        private readonly Note _note;

        public NoteItemViewModel(Note note)
        {
            _note = note ?? throw new ArgumentNullException(nameof(note));
            
            // Initialize commands
            OpenCommand = new RelayCommand(() => OnOpenRequested());
            SelectCommand = new RelayCommand(() => OnSelectionRequested());
        }

        public string Id => _note.NoteId.Value;
        public string Title => _note.Title;
        public string FilePath => _note.FilePath;
        public bool IsPinned => _note.IsPinned;
        public DateTime CreatedAt => _note.CreatedAt;
        public DateTime UpdatedAt => _note.UpdatedAt;
        public Note Note => _note; // Expose underlying note for workspace operations
        
        /// <summary>
        /// Formatted category path for tooltips (e.g., "Notes > Projects > 25-117")
        /// </summary>
        public string CategoryPath
        {
            get
            {
                try
                {
                    if (string.IsNullOrEmpty(FilePath)) return string.Empty;
                    
                    var directoryPath = System.IO.Path.GetDirectoryName(FilePath);
                    if (string.IsNullOrEmpty(directoryPath)) return string.Empty;
                    
                    var parts = directoryPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                    var notesIndex = Array.FindIndex(parts, p => p.Equals("Notes", StringComparison.OrdinalIgnoreCase));
                    var startIndex = notesIndex >= 0 ? notesIndex : Math.Max(0, parts.Length - 4);
                    
                    var relevantParts = parts.Skip(startIndex).ToArray();
                    return string.Join(" > ", relevantParts);
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        // Commands for UI interaction
        public ICommand OpenCommand { get; }
        public ICommand SelectCommand { get; }

        // Events for parent coordination
        public event Action<NoteItemViewModel> OpenRequested;
        public event Action<NoteItemViewModel> SelectionRequested;

        private void OnOpenRequested()
        {
            OpenRequested?.Invoke(this);
        }

        private void OnSelectionRequested()
        {
            SelectionRequested?.Invoke(this);
        }
    }
}