using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using NoteNest.Domain.Categories;
using NoteNest.UI.ViewModels.Common;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Notes;
using System.Collections.Generic;
using NoteNest.Core.Commands;
using System.Collections.Specialized;
using System.IO;
using NoteNest.UI.Interfaces;

namespace NoteNest.UI.ViewModels.Categories
{
    public class CategoryViewModel : ViewModelBase
    {
        private readonly Category _category;
        private readonly INoteRepository _noteRepository;
        private readonly IAppLogger _logger;
        private readonly IIconService _iconService;
        private bool _isExpanded;
        private bool _isLoading;
        private bool _notesLoaded;

        public CategoryViewModel(Category category, INoteRepository noteRepository = null, IAppLogger logger = null, IIconService iconService = null)
        {
            _category = category ?? throw new ArgumentNullException(nameof(category));
            _noteRepository = noteRepository;
            _logger = logger;
            _iconService = iconService;
            
            Children = new ObservableCollection<CategoryViewModel>();
            Notes = new ObservableCollection<NoteItemViewModel>();
            TreeItems = new ObservableCollection<object>();
            
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
        
        public ObservableCollection<CategoryViewModel> Children { get; }
        public ObservableCollection<NoteItemViewModel> Notes { get; }
        
        // Composite collection for TreeView binding - combines children and notes
        public ObservableCollection<object> TreeItems { get; private set; }
        
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
                    OnPropertyChanged(nameof(ExpanderVisibility));
                    OnPropertyChanged(nameof(ExpanderIcon));
                    OnPropertyChanged(nameof(CategoryIcon));
                    OnPropertyChanged(nameof(FolderIconGeometry)); // 🎨 Update Lucide geometry
                    
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
        
        // 🎨 LUCIDE SVG ICONS - Modern geometry-based icons
        public string ExpanderIcon => IsExpanded ? "▼" : "▶"; // Keep text expander for now
        public Geometry FolderIconGeometry => _iconService?.GetTreeIconGeometry(
            TreeIconType.Folder, 
            IsExpanded ? TreeIconState.Expanded : TreeIconState.Default) ?? GetFallbackFolderGeometry();

        // Legacy string property for backward compatibility (can be removed later)
        public string CategoryIcon => IsExpanded ? "[Open]" : "[Folder]";

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
                
                Notes.Clear();
                foreach (var note in notes)
                {
                    var noteViewModel = new NoteItemViewModel(note, _iconService);
                    
                    // Wire up note events to bubble up to tree level
                    noteViewModel.OpenRequested += OnNoteOpenRequested;
                    noteViewModel.SelectionRequested += OnNoteSelectionRequested;
                    
                    Notes.Add(noteViewModel);
                }
                
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

        public void RefreshNotes()
        {
            _notesLoaded = false;
            Notes.Clear();
            UpdateTreeItems();
            if (IsExpanded)
            {
                _ = LoadNotesAsync();
            }
        }

        private void UpdateTreeItems()
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

        #region Icon Fallback Handling

        /// <summary>
        /// Provides simple fallback folder geometry when IconService is unavailable
        /// </summary>
        private Geometry GetFallbackFolderGeometry()
        {
            try
            {
                // Simple folder rectangle as fallback
                var geometry = Geometry.Parse("M2 4h4l2 2h12v12H2V4z");
                geometry.Freeze();
                return geometry;
            }
            catch
            {
                // Ultra-simple rectangle if even fallback fails
                var fallback = Geometry.Parse("M2 2h20v20H2V2z");
                fallback.Freeze();
                return fallback;
            }
        }

        #endregion
    }

    // Enhanced ViewModel for notes within categories with interaction support
    public class NoteItemViewModel : ViewModelBase
    {
        private readonly Note _note;
        private readonly IIconService _iconService;

        public NoteItemViewModel(Note note, IIconService iconService = null)
        {
            _note = note ?? throw new ArgumentNullException(nameof(note));
            _iconService = iconService;
            
            // Initialize commands
            OpenCommand = new RelayCommand(() => OnOpenRequested());
            SelectCommand = new RelayCommand(() => OnSelectionRequested());
        }

        public string Id => _note.Id.Value;
        public string Title => _note.Title;
        public string FilePath => _note.FilePath;
        public bool IsPinned => _note.IsPinned;
        
        // 🎨 LUCIDE SVG ICONS - Modern geometry-based icons
        public Geometry DocumentIconGeometry => _iconService?.GetTreeIconGeometry(
            IsPinned ? TreeIconType.Pin : TreeIconType.Document, 
            IsPinned ? TreeIconState.Pinned : TreeIconState.Default) ?? GetFallbackDocumentGeometry();
        
        // Legacy string property for backward compatibility (can be removed later)  
        public string NoteIcon => IsPinned ? "[Pinned]" : "[Note]";
        
        public DateTime CreatedAt => _note.CreatedAt;
        public DateTime UpdatedAt => _note.UpdatedAt;
        public Note Note => _note; // Expose underlying note for workspace operations

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

        #region Icon Fallback Handling

        /// <summary>
        /// Provides simple fallback document geometry when IconService is unavailable
        /// </summary>
        private Geometry GetFallbackDocumentGeometry()
        {
            try
            {
                // Simple document rectangle as fallback
                var geometry = Geometry.Parse("M4 2h12v20H4V2z M8 6h8 M8 10h8 M8 14h5");
                geometry.Freeze();
                return geometry;
            }
            catch
            {
                // Ultra-simple rectangle if even fallback fails
                var fallback = Geometry.Parse("M2 2h16v20H2V2z");
                fallback.Freeze();
                return fallback;
            }
        }

        #endregion
    }
}