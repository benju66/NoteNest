using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces.Split;

namespace NoteNest.Core.Services.Implementation
{
    public class WorkspaceService : IWorkspaceService, INotifyPropertyChanged
    {
        private readonly ContentCache _contentCache;
        private readonly NoteService _noteService;
        private readonly IServiceErrorHandler _errorHandler;
        private readonly IAppLogger _logger;
        private readonly INoteOperationsService _noteOperationsService;
        private readonly NoteNest.Core.Services.IWorkspaceStateService? _stateService;
        
        private ObservableCollection<ITabItem> _openTabs;
        private ITabItem? _selectedTab;
        private readonly ObservableCollection<SplitPane> _panes;
        private SplitPane? _activePane;
        
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

        #region Split View Support

        /// <summary>
        /// Gets all workspace panes
        /// </summary>
        public ObservableCollection<SplitPane> Panes => _panes;

        /// <summary>
        /// Gets or sets the active pane
        /// </summary>
        public SplitPane? ActivePane
        {
            get => _activePane;
            set
            {
                if (_activePane != value)
                {
                    if (_activePane != null)
                        _activePane.IsActive = false;
                    
                    _activePane = value;
                    
                    if (_activePane != null)
                        _activePane.IsActive = true;
                    
                    OnPropertyChanged(nameof(ActivePane));
                }
            }
        }

        #endregion
        
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
            INoteOperationsService noteOperationsService,
            NoteNest.Core.Services.IWorkspaceStateService workspaceStateService)
        {
            _contentCache = contentCache ?? throw new ArgumentNullException(nameof(contentCache));
            _noteService = noteService ?? throw new ArgumentNullException(nameof(noteService));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _noteOperationsService = noteOperationsService ?? throw new ArgumentNullException(nameof(noteOperationsService));
            _stateService = workspaceStateService ?? throw new ArgumentNullException(nameof(workspaceStateService));
            
            _openTabs = new ObservableCollection<ITabItem>();
            _panes = new ObservableCollection<SplitPane>();
            
            // Create initial pane with existing tabs
            var initialPane = new SplitPane();
            foreach (var tab in OpenTabs)
            {
                initialPane.Tabs.Add(tab);
            }
            _panes.Add(initialPane);
            ActivePane = initialPane;
            
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
                    System.Diagnostics.Debug.WriteLine($"[WS] OpenNoteAsync SKIP already-open id={note.Id} title={note.Title}");
                    return existingTab;
                }
                
                // Load content if needed
                if (string.IsNullOrEmpty(note.Content))
                {
                    System.Diagnostics.Debug.WriteLine($"[WS] Loading content for {note.Title} from {note.FilePath}");
                    note.Content = await _contentCache.GetContentAsync(
                        note.FilePath,
                        async (path) => 
                        {
                            var loadedNote = await _noteService.LoadNoteAsync(path);
                            System.Diagnostics.Debug.WriteLine($"[WS] Loaded content len={loadedNote?.Content?.Length ?? 0} for {note.Title}");
                            return loadedNote.Content;
                        });
                }
                
                // CRITICAL FIX: Register note with WorkspaceStateService
                var wn = await _stateService.OpenNoteAsync(note);
                _logger.Debug($"Registered note with state service: {note.Title}");
                System.Diagnostics.Debug.WriteLine($"[WS] Registered note id={note.Id} title={note.Title} contentLen={wn?.CurrentContent?.Length ?? 0}");
                
                // Create new tab
                // Note: The actual tab creation is delegated to UI layer
                // This method returns a placeholder that will be replaced
                var tab = new WorkspaceTabItem(note);
                
                _logger.Info($"Opened note: {note.Title}");
                System.Diagnostics.Debug.WriteLine($"[WS] Opened note id={note.Id} title={note.Title}");
                
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
                // Ensure unregistration from state even if the tab is not in OpenTabs

                // Always attempt to unregister the note from state service
                if (tab.Note != null)
                {
                    var ok = await _stateService.CloseNoteAsync(tab.Note.Id);
                    _logger.Debug($"Unregistered note from state service: {tab.Title}");
                    System.Diagnostics.Debug.WriteLine($"[WS] Unregister note id={tab.Note.Id} ok={ok}");
                }

                // Remove from service OpenTabs if present
                if (OpenTabs.Contains(tab))
                {
                    OpenTabs.Remove(tab);
                }

                // Untrack from note operations
                _noteOperationsService?.UntrackOpenNote(tab.Note);

                TabClosed?.Invoke(this, new TabEventArgs { Tab = tab });
                _logger.Debug($"Closed tab: {tab.Title}");
                return true;
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
                // Save all dirty notes via state service
                var result = await _stateService.SaveAllDirtyNotesAsync();
                _logger.Info($"Saved {result.SuccessCount} notes via state service");
                System.Diagnostics.Debug.WriteLine($"[WS] SaveAllTabsAsync completed success={result.SuccessCount} fail={result.FailureCount}");
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

        public async Task<SplitPane> SplitPaneAsync(SplitPane pane, SplitOrientation orientation)
        {
            if (pane == null)
                throw new ArgumentNullException(nameof(pane));
            
            var newPane = new SplitPane();
            
            // Move half of the tabs to new pane (optional)
            var tabsToMove = pane.Tabs.Skip(pane.Tabs.Count / 2).ToList();
            foreach (var tab in tabsToMove)
            {
                pane.Tabs.Remove(tab);
                newPane.Tabs.Add(tab);
            }
            
            _panes.Add(newPane);
            
            _logger.Debug($"Split pane {pane.Id} {orientation}");
            
            return await Task.FromResult(newPane);
        }

        public async Task ClosePaneAsync(SplitPane pane)
        {
            if (pane == null || _panes.Count <= 1)
                return;
            
            // Move tabs to another pane before closing
            var targetPane = _panes.FirstOrDefault(p => p != pane);
            if (targetPane != null)
            {
                foreach (var tab in pane.Tabs.ToList())
                {
                    pane.Tabs.Remove(tab);
                    targetPane.Tabs.Add(tab);
                }
            }
            
            _panes.Remove(pane);
            
            if (ActivePane == pane)
            {
                ActivePane = _panes.FirstOrDefault();
            }
            
            _logger.Debug($"Closed pane {pane.Id}");
            await Task.CompletedTask;
        }

        public async Task MoveTabToPaneAsync(ITabItem tab, SplitPane targetPane)
        {
            if (tab == null || targetPane == null)
                return;
            
            // Find source pane
            var sourcePane = _panes.FirstOrDefault(p => p.Tabs.Contains(tab));
            if (sourcePane != null && sourcePane != targetPane)
            {
                sourcePane.Tabs.Remove(tab);
                targetPane.Tabs.Add(tab);
                
                // Close source pane if empty
                if (!sourcePane.Tabs.Any())
                {
                    await ClosePaneAsync(sourcePane);
                }
            }
        }

        public async Task MoveTabToPaneAsync(ITabItem tab, SplitPane targetPane, int targetIndex)
        {
            if (tab == null || targetPane == null)
                return;
            var sourcePane = _panes.FirstOrDefault(p => p.Tabs.Contains(tab));
            if (sourcePane == null)
                return;
            if (sourcePane == targetPane)
            {
                // Reorder within the same pane
                var currentIndex = sourcePane.Tabs.IndexOf(tab);
                if (currentIndex < 0) return;
                var insertIndex = targetIndex;
                if (insertIndex > currentIndex) insertIndex--;
                if (insertIndex == currentIndex) return;
                sourcePane.Tabs.RemoveAt(currentIndex);
                if (insertIndex < 0) insertIndex = 0;
                if (insertIndex > sourcePane.Tabs.Count) insertIndex = sourcePane.Tabs.Count;
                sourcePane.Tabs.Insert(insertIndex, tab);
                return;
            }
            // Move across panes at index
            sourcePane.Tabs.Remove(tab);
            var boundedIndex = Math.Max(0, Math.Min(targetIndex, targetPane.Tabs.Count));
            targetPane.Tabs.Insert(boundedIndex, tab);
            if (!sourcePane.Tabs.Any())
            {
                await ClosePaneAsync(sourcePane);
            }
        }

        public void SetActivePane(SplitPane pane)
        {
            ActivePane = pane;
        }

        // Future Split View Support - current single-pane implementations
        public System.Collections.Generic.IEnumerable<object> GetActivePanes()
        {
            // For now, return a single "pane" representing the current workspace
            // This will be replaced with actual split panes when split view is implemented
            yield return new { Id = "main-pane", Tabs = OpenTabs };
        }

        public async Task<bool> MoveTabToPaneAsync(ITabItem tab, object targetPane)
        {
            // For now, this is a no-op since we only have one pane
            // Will be implemented when split view is added
            await Task.CompletedTask;
            _logger.Debug($"MoveTabToPaneAsync called (no-op until split view): {tab?.Title}");
            return false; // Not yet supported
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
                var incoming = value ?? string.Empty;
                var current = _content ?? Note?.Content ?? string.Empty;
                if (!string.Equals(incoming, current, StringComparison.Ordinal))
                {
                    _content = incoming;
                    if (Note != null)
                    {
                        Note.Content = incoming;
                    }
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