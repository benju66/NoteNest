using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Notes;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.UI.ViewModels.Workspace
{
    /// <summary>
    /// NEW: Clean workspace ViewModel with proper MVVM separation
    /// Replaces ModernWorkspaceViewModel with better architecture
    /// </summary>
    public class WorkspaceViewModel : ViewModelBase
    {
        private readonly ISaveManager _saveManager;
        private readonly IAppLogger _logger;
        private PaneViewModel _activePane;
        private bool _isLoading;
        private string _statusMessage;
        
        public ObservableCollection<PaneViewModel> Panes { get; }
        
        public PaneViewModel ActivePane
        {
            get => _activePane;
            set
            {
                if (_activePane != value)
                {
                    // Unsubscribe from old pane
                    if (_activePane != null)
                    {
                        _activePane.IsActive = false;
                        _activePane.PropertyChanged -= OnActivePanePropertyChanged;
                    }
                    
                    _activePane = value;
                    
                    // Subscribe to new pane
                    if (_activePane != null)
                    {
                        _activePane.IsActive = true;
                        _activePane.PropertyChanged += OnActivePanePropertyChanged;
                    }
                    
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedTab));
                    OnPropertyChanged(nameof(OpenTabs));
                    OnPropertyChanged(nameof(HasOpenTabs));
                }
            }
        }
        
        public TabViewModel SelectedTab
        {
            get => ActivePane?.SelectedTab;
            set
            {
                if (ActivePane != null && ActivePane.SelectedTab != value)
                {
                    ActivePane.SelectedTab = value;
                    OnPropertyChanged();
                    TabSelected?.Invoke(value);
                }
            }
        }
        
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }
        
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
        
        // Convenience properties
        public ObservableCollection<TabViewModel> OpenTabs => ActivePane?.Tabs ?? new ObservableCollection<TabViewModel>();
        public bool HasOpenTabs => ActivePane?.HasTabs ?? false;
        
        // Commands
        public ICommand SaveTabCommand { get; private set; }
        public ICommand SaveAllTabsCommand { get; private set; }
        public ICommand CloseTabCommand { get; private set; }
        
        // Events for coordination
        public event Action<TabViewModel> TabSelected;
        public event Action<TabViewModel> TabClosed;
        public event Action<string> NoteOpened;
        
        public WorkspaceViewModel(ISaveManager saveManager, IAppLogger logger)
        {
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            Panes = new ObservableCollection<PaneViewModel>();
            
            // Initialize with single pane
            var initialPane = new PaneViewModel();
            Panes.Add(initialPane);
            ActivePane = initialPane;
            
            InitializeCommands();
            
            _logger.Info("[WorkspaceViewModel] Initialized with single pane");
        }
        
        private void InitializeCommands()
        {
            SaveTabCommand = new AsyncRelayCommand<TabViewModel>(ExecuteSaveTab, CanSaveTab);
            SaveAllTabsCommand = new AsyncRelayCommand(ExecuteSaveAllTabs, CanSaveAllTabs);
            CloseTabCommand = new AsyncRelayCommand<TabViewModel>(ExecuteCloseTab);
        }
        
        /// <summary>
        /// Handle property changes from the active pane to propagate to UI
        /// </summary>
        private void OnActivePanePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Propagate important property changes from pane to workspace
            if (e.PropertyName == nameof(PaneViewModel.SelectedTab))
            {
                OnPropertyChanged(nameof(SelectedTab));
                TabSelected?.Invoke(SelectedTab);
                System.Diagnostics.Debug.WriteLine($"[WorkspaceViewModel] Tab selected: {SelectedTab?.Title ?? "null"}");
            }
            else if (e.PropertyName == nameof(PaneViewModel.Tabs) || e.PropertyName == nameof(PaneViewModel.HasTabs))
            {
                OnPropertyChanged(nameof(OpenTabs));
                OnPropertyChanged(nameof(HasOpenTabs));
            }
        }
        
        /// <summary>
        /// Open a note in the active pane
        /// </summary>
        public async Task OpenNoteAsync(Note domainNote)
        {
            if (domainNote == null)
                throw new ArgumentNullException(nameof(domainNote));
            
            try
            {
                IsLoading = true;
                StatusMessage = $"Opening {domainNote.Title}...";
                
                // Check if already open
                var existingTab = FindTabByPath(domainNote.FilePath);
                if (existingTab != null)
                {
                    ActivePane = FindPaneContainingTab(existingTab);
                    SelectedTab = existingTab;
                    StatusMessage = $"Switched to {domainNote.Title}";
                    _logger.Debug($"Tab already open: {domainNote.Title}");
                    return;
                }
                
                // CRITICAL FIX: Always load content from file, not from database
                // Database Content field is metadata only - actual content is in the file
                string noteContent = "";
                if (!string.IsNullOrEmpty(domainNote.FilePath) && System.IO.File.Exists(domainNote.FilePath))
                {
                    noteContent = await System.IO.File.ReadAllTextAsync(domainNote.FilePath);
                    _logger.Debug($"Loaded content from file: {domainNote.FilePath} ({noteContent.Length} chars)");
                }
                else
                {
                    _logger.Warning($"File not found for note: {domainNote.Title} at {domainNote.FilePath}");
                }
                
                // Convert to NoteModel
                var noteModel = new NoteModel
                {
                    Id = domainNote.Id.Value,
                    Title = domainNote.Title,
                    Content = noteContent,
                    FilePath = domainNote.FilePath ?? "",
                    CategoryId = domainNote.CategoryId.Value,
                    LastModified = domainNote.UpdatedAt
                };
                
                // Register with SaveManager FIRST
                var noteId = await _saveManager.OpenNoteAsync(noteModel.FilePath);
                noteModel.Id = noteId; // Use SaveManager's hash-based ID
                
                _logger.Info($"Note registered with SaveManager: {noteId}");
                
                // Update SaveManager with initial content
                if (!string.IsNullOrEmpty(noteContent))
                {
                    _saveManager.UpdateContent(noteId, noteContent);
                }
                
                // Create TabViewModel
                var tabVm = new TabViewModel(noteId, noteModel, _saveManager);
                
                // Add to active pane
                ActivePane.AddTab(tabVm, select: true);
                
                // Trigger content load in UI
                tabVm.RequestContentLoad();
                
                StatusMessage = $"Opened {noteModel.Title}";
                NoteOpened?.Invoke(domainNote.Id.Value);
                
                _logger.Info($"Tab opened: {noteModel.Title} (ID: {noteId})");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to open note: {ex.Message}";
                _logger.Error(ex, $"Failed to open note: {domainNote.Title}");
                throw;
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        private async Task ExecuteSaveTab(TabViewModel tab)
        {
            if (tab == null) return;
            
            try
            {
                IsLoading = true;
                StatusMessage = $"Saving {tab.Title}...";
                
                await tab.SaveAsync();
                
                StatusMessage = $"Saved {tab.Title}";
                _logger.Info($"Tab saved: {tab.Title}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving {tab.Title}: {ex.Message}";
                _logger.Error(ex, $"Failed to save tab: {tab.Title}");
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        private async Task ExecuteSaveAllTabs()
        {
            var dirtyTabs = ActivePane.Tabs.Where(t => t.IsDirty).ToList();
            if (!dirtyTabs.Any())
            {
                StatusMessage = "No changes to save";
                return;
            }
            
            try
            {
                IsLoading = true;
                StatusMessage = $"Saving {dirtyTabs.Count} tab(s)...";
                
                int savedCount = 0;
                int failedCount = 0;
                
                foreach (var tab in dirtyTabs)
                {
                    try
                    {
                        await tab.SaveAsync();
                        savedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        _logger.Error(ex, $"Failed to save tab: {tab.Title}");
                    }
                }
                
                StatusMessage = failedCount > 0
                    ? $"Saved {savedCount} tab(s), {failedCount} failed"
                    : $"Saved {savedCount} tab(s)";
                
                _logger.Info($"Save all completed: {savedCount} succeeded, {failedCount} failed");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during save all: {ex.Message}";
                _logger.Error(ex, "Save all failed");
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        private async Task ExecuteCloseTab(TabViewModel tab)
        {
            if (tab == null) return;
            
            try
            {
                // Auto-save if dirty
                if (tab.IsDirty)
                {
                    try
                    {
                        await tab.SaveAsync();
                        _logger.Info($"Auto-saved on close: {tab.Title}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Failed to auto-save on close: {tab.Title}");
                        // Continue with close even if save failed
                    }
                }
                
                // Close note in SaveManager
                await _saveManager.CloseNoteAsync(tab.TabId);
                
                // Remove from pane
                var pane = FindPaneContainingTab(tab);
                pane?.RemoveTab(tab);
                
                TabClosed?.Invoke(tab);
                StatusMessage = $"Closed {tab.Title}";
                
                _logger.Info($"Tab closed: {tab.Title}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error closing tab: {ex.Message}";
                _logger.Error(ex, $"Failed to close tab: {tab.Title}");
            }
        }
        
        private bool CanSaveTab(TabViewModel tab) => !IsLoading && tab?.IsDirty == true;
        private bool CanSaveAllTabs() => !IsLoading && ActivePane?.Tabs.Any(t => t.IsDirty) == true;
        
        /// <summary>
        /// Find tab by file path across all panes
        /// </summary>
        public TabViewModel FindTabByPath(string filePath)
        {
            foreach (var pane in Panes)
            {
                var tab = pane.FindTabByPath(filePath);
                if (tab != null) return tab;
            }
            return null;
        }
        
        /// <summary>
        /// Find which pane contains a specific tab
        /// </summary>
        public PaneViewModel FindPaneContainingTab(TabViewModel tab)
        {
            return Panes.FirstOrDefault(p => p.Tabs.Contains(tab));
        }
    }
}

