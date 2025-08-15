using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Implementation
{
    public class WorkspaceService : IWorkspaceService, INotifyPropertyChanged
    {
        private readonly ContentCache _contentCache;
        private readonly NoteService _noteService;
        private readonly IServiceErrorHandler _errorHandler;
        private readonly IAppLogger _logger;
        private readonly INoteOperationsService _noteOperationsService;
        
        private ObservableCollection<ITabItem> _openTabs;
        private ITabItem? _selectedTab;
        
        public ObservableCollection<ITabItem> OpenTabs
        {
            get => _openTabs;
            private set
            {
                _openTabs = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
        }
        
        public ITabItem? SelectedTab
        {
            get => _selectedTab;
            set
            {
                var oldTab = _selectedTab;
                _selectedTab = value;
                OnPropertyChanged();
                TabSelectionChanged?.Invoke(this, new TabChangedEventArgs 
                { 
                    OldTab = oldTab, 
                    NewTab = value 
                });
            }
        }
        
        public bool HasUnsavedChanges => OpenTabs?.Any(t => t.IsDirty) ?? false;
        
        public event EventHandler<TabChangedEventArgs>? TabSelectionChanged;
        public event EventHandler<TabEventArgs>? TabOpened;
        public event EventHandler<TabEventArgs>? TabClosed;
        public event PropertyChangedEventHandler? PropertyChanged;
        
        public WorkspaceService(
            ContentCache contentCache,
            NoteService noteService,
            IServiceErrorHandler errorHandler,
            IAppLogger logger,
            INoteOperationsService noteOperationsService)
        {
            _contentCache = contentCache ?? throw new ArgumentNullException(nameof(contentCache));
            _noteService = noteService ?? throw new ArgumentNullException(nameof(noteService));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _noteOperationsService = noteOperationsService ?? throw new ArgumentNullException(nameof(noteOperationsService));
            
            _openTabs = new ObservableCollection<ITabItem>();
            
            // Subscribe to collection changes for tracking
            _openTabs.CollectionChanged += OnOpenTabsCollectionChanged;
            
            _logger.Debug("WorkspaceService initialized");
        }
        
        public async Task<ITabItem> OpenNoteAsync(NoteModel note)
        {
            if (note == null)
                throw new ArgumentNullException(nameof(note));
            
            return await _errorHandler.SafeExecuteAsync(async () =>
            {
                // Check if already open
                var existingTab = FindTabByNote(note);
                if (existingTab != null)
                {
                    SelectedTab = existingTab;
                    _logger.Debug($"Note already open, switching to tab: {note.Title}");
                    return existingTab;
                }
                
                // Load content if needed
                if (string.IsNullOrEmpty(note.Content))
                {
                    note.Content = await _contentCache.GetContentAsync(
                        note.FilePath,
                        async (path) => 
                        {
                            var loadedNote = await _noteService.LoadNoteAsync(path);
                            return loadedNote.Content;
                        });
                }
                
                // Create new tab
                // Note: The actual tab creation is delegated to UI layer
                // This method returns a placeholder that will be replaced
                var tab = new WorkspaceTabItem(note);
                
                _logger.Info($"Opened note: {note.Title}");
                
                TabOpened?.Invoke(this, new TabEventArgs { Tab = tab });
                return tab;
            }, "Open Note");
        }
        
        public async Task<bool> CloseTabAsync(ITabItem tab)
        {
            if (tab == null)
                throw new ArgumentNullException(nameof(tab));
            
            return await _errorHandler.SafeExecuteAsync(async () =>
            {
                // Note: Save prompt is handled by UI layer
                // This just removes the tab
                
                if (OpenTabs.Contains(tab))
                {
                    OpenTabs.Remove(tab);
                    
                    // Untrack from note operations
                    _noteOperationsService?.UntrackOpenNote(tab.Note);
                    
                    TabClosed?.Invoke(this, new TabEventArgs { Tab = tab });
                    
                    _logger.Debug($"Closed tab: {tab.Title}");
                    return true;
                }
                
                return false;
            }, "Close Tab");
        }
        
        public async Task<bool> CloseAllTabsAsync()
        {
            return await _errorHandler.SafeExecuteAsync(async () =>
            {
                var tabsToClose = OpenTabs.ToList();
                
                foreach (var tab in tabsToClose)
                {
                    await CloseTabAsync(tab);
                }
                
                _logger.Info("Closed all tabs");
                return true;
            }, "Close All Tabs");
        }
        
        public async Task SaveAllTabsAsync()
        {
            await _errorHandler.SafeExecuteAsync(async () =>
            {
                var dirtyTabs = OpenTabs.Where(t => t.IsDirty).ToList();
                
                foreach (var tab in dirtyTabs)
                {
                    await _noteOperationsService.SaveNoteAsync(tab.Note);
                    tab.IsDirty = false;
                }
                
                _logger.Info($"Saved {dirtyTabs.Count} tabs");
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }, "Save All Tabs");
        }
        
        public ITabItem? FindTabByNote(NoteModel note)
        {
            if (note == null) return null;
            
            // Compare by file path for reliability
            return OpenTabs.FirstOrDefault(t => 
                t.Note?.FilePath?.Equals(note.FilePath, StringComparison.OrdinalIgnoreCase) == true);
        }
        
        public ITabItem? FindTabByPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            
            return OpenTabs.FirstOrDefault(t => 
                t.Note?.FilePath?.Equals(filePath, StringComparison.OrdinalIgnoreCase) == true);
        }
        
        private void OnOpenTabsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Track notes with operations service
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (ITabItem tab in e.NewItems)
                {
                    _noteOperationsService?.TrackOpenNote(tab.Note);
                }
            }
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (ITabItem tab in e.OldItems)
                {
                    _noteOperationsService?.UntrackOpenNote(tab.Note);
                }
            }
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                _noteOperationsService?.ClearTrackedNotes();
            }
            
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
        
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    // Internal implementation of ITabItem for workspace
    internal class WorkspaceTabItem : ITabItem
    {
        private bool _isDirty;
        private string _content;
        
        public string Id { get; }
        public string Title => Note?.Title ?? "Untitled";
        public NoteModel Note { get; }
        
        public bool IsDirty
        {
            get => _isDirty;
            set => _isDirty = value;
        }
        
        public string Content
        {
            get => _content ?? Note?.Content ?? string.Empty;
            set
            {
                _content = value;
                if (Note != null)
                {
                    Note.Content = value;
                    IsDirty = true;
                }
            }
        }
        
        public WorkspaceTabItem(NoteModel note)
        {
            Note = note ?? throw new ArgumentNullException(nameof(note));
            Id = Guid.NewGuid().ToString();
            _content = note.Content;
            _isDirty = false;
        }
    }
}