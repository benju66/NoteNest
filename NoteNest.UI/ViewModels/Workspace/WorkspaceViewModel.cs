using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Diagnostics;
using NoteNest.Domain.Notes;
using NoteNest.UI.ViewModels.Common;
using NoteNest.UI.ViewModels.Windows;
using NoteNest.UI.Services;
using System.Collections.Generic;

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
        private readonly IWorkspacePersistenceService _workspacePersistence;
        private readonly IWindowManager _windowManager; // Phase 7: Tear-out functionality
        private readonly IMultiWindowThemeCoordinator _themeCoordinator; // Phase 7: Theme sync
        private readonly NoteNest.Core.Services.IMultiMonitorManager _multiMonitorManager; // Phase 7: Multi-monitor support
        
        private PaneViewModel _activePane;
        private bool _isLoading;
        private string _statusMessage;
        
        // Persistence debouncing (prevent race conditions during rapid operations)
        private bool _isSaving;
        private bool _savePending;
        
        // Move operation throttling (prevent WPF reentrancy crashes)
        private bool _isMoveInProgress;
        private readonly object _moveLock = new object();
        
        // State snapshot synchronization (prevent collection modification during enumeration)
        private readonly object _stateSnapshotLock = new object();
        
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
        public bool CanSplit => Panes.Count < 2;
        
        // Commands
        public ICommand SaveTabCommand { get; private set; }
        public ICommand SaveAllTabsCommand { get; private set; }
        public ICommand CloseTabCommand { get; private set; }
        public ICommand SplitVerticalCommand { get; private set; }
        public ICommand SwitchToPaneCommand { get; private set; }
        public ICommand ClosePaneCommand { get; private set; }
        
        // Tier 1 Features: Tab cycling
        public ICommand NextTabCommand { get; private set; }
        public ICommand PreviousTabCommand { get; private set; }
        
        // Tier 1 Features: Context menu commands
        public ICommand CloseAllTabsCommand { get; private set; }
        public ICommand CloseOtherTabsCommand { get; private set; }
        public ICommand MoveToOtherPaneCommand { get; private set; }
        
        // Phase 7: Tear-out functionality commands
        public ICommand DetachTabCommand { get; private set; }
        public ICommand DetachOtherTabsCommand { get; private set; }
        public ICommand RedockTabCommand { get; private set; }
        public ICommand RedockAllTabsCommand { get; private set; }
        
        // Events for coordination
        public event Action<TabViewModel> TabSelected;
        public event Action<TabViewModel> TabClosed;
        public event Action<string> NoteOpened;
        public event Action<string, bool> NoteSaved; // NoteId, WasAutoSave
        
        public WorkspaceViewModel(
            ISaveManager saveManager, 
            IAppLogger logger,
            IWorkspacePersistenceService workspacePersistence,
            IWindowManager windowManager = null, // Optional for tear-out functionality
            IMultiWindowThemeCoordinator themeCoordinator = null, // Optional for theme sync
            NoteNest.Core.Services.IMultiMonitorManager multiMonitorManager = null) // Optional for multi-monitor support
        {
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _workspacePersistence = workspacePersistence ?? throw new ArgumentNullException(nameof(workspacePersistence));
            _windowManager = windowManager; // Can be null (tear-out disabled)
            _themeCoordinator = themeCoordinator; // Can be null (no theme sync)
            _multiMonitorManager = multiMonitorManager; // Can be null (single monitor)
            
            // Register this workspace with the WindowManager for redocking operations
            if (_windowManager is NoteNest.UI.Services.WindowManager wm)
            {
                wm.SetMainWorkspace(this);
            }
            
            Panes = new ObservableCollection<PaneViewModel>();
            
            // Initialize with single pane
            var initialPane = new PaneViewModel();
            Panes.Add(initialPane);
            ActivePane = initialPane;
            
            InitializeCommands();
            
            // Subscribe to SaveManager's NoteSaved event and bubble it up
            _saveManager.NoteSaved += OnSaveManagerNoteSaved;
            
            // Hook up auto-save triggers
            Panes.CollectionChanged += (s, e) => _ = SaveStateAsync();
            
            _logger.Info("[WorkspaceViewModel] Initialized with single pane");
        }
        
        private void OnSaveManagerNoteSaved(object sender, NoteNest.Core.Services.NoteSavedEventArgs e)
        {
            // Bubble up the NoteSaved event for status bar updates
            NoteSaved?.Invoke(e.NoteId, e.WasAutoSave);
            _logger.Debug($"Note saved event bubbled: {e.NoteId}, AutoSave={e.WasAutoSave}");
        }
        
        private void InitializeCommands()
        {
            SaveTabCommand = new AsyncRelayCommand<TabViewModel>(ExecuteSaveTab, CanSaveTab);
            SaveAllTabsCommand = new AsyncRelayCommand(ExecuteSaveAllTabs, CanSaveAllTabs);
            CloseTabCommand = new AsyncRelayCommand<TabViewModel>(ExecuteCloseTab);
            SplitVerticalCommand = new NoteNest.Core.Commands.RelayCommand(ExecuteSplitVertical, () => CanSplit);
            SwitchToPaneCommand = new NoteNest.Core.Commands.RelayCommand<object>(ExecuteSwitchToPane);
            ClosePaneCommand = new NoteNest.Core.Commands.RelayCommand(ExecuteClosePane, () => Panes.Count > 1);
            
            // Tier 1 Features: Tab cycling commands
            NextTabCommand = new NoteNest.Core.Commands.RelayCommand(ExecuteNextTab, () => HasOpenTabs);
            PreviousTabCommand = new NoteNest.Core.Commands.RelayCommand(ExecutePreviousTab, () => HasOpenTabs);
            
            // Tier 1 Features: Context menu commands
            CloseAllTabsCommand = new AsyncRelayCommand(ExecuteCloseAllTabs, () => HasOpenTabs);
            CloseOtherTabsCommand = new AsyncRelayCommand<TabViewModel>(ExecuteCloseOtherTabs);
            MoveToOtherPaneCommand = new NoteNest.Core.Commands.RelayCommand<TabViewModel>(ExecuteMoveToOtherPane, CanMoveToOtherPane);
            
            // Phase 7: Tear-out functionality commands
            DetachTabCommand = new AsyncRelayCommand<TabViewModel>(ExecuteDetachTab, CanDetachTab);
            DetachOtherTabsCommand = new AsyncRelayCommand<TabViewModel>(ExecuteDetachOtherTabs, CanDetachOtherTabs);
            RedockTabCommand = new AsyncRelayCommand<TabViewModel>(ExecuteRedockTab, CanRedockTab);
            RedockAllTabsCommand = new AsyncRelayCommand(ExecuteRedockAllTabs, CanRedockAllTabs);
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
                    Id = domainNote.NoteId.Value,
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
                NoteOpened?.Invoke(domainNote.NoteId.Value);
                
                _logger.Info($"Tab opened: {noteModel.Title} (ID: {noteId})");
                
                // Auto-save workspace state
                _ = SaveStateAsync();
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
            // FIXED: Save tabs from ALL panes, not just active pane
            var dirtyTabs = Panes.SelectMany(p => p.Tabs).Where(t => t.IsDirty).ToList();
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
                
                // Auto-close empty pane if we have more than one pane
                if (pane != null && pane.Tabs.Count == 0 && Panes.Count > 1)
                {
                    Panes.Remove(pane);
                    
                    // Switch to remaining pane
                    if (ActivePane == pane)
                    {
                        ActivePane = Panes.FirstOrDefault();
                    }
                    
                    // Update CanSplit
                    OnPropertyChanged(nameof(CanSplit));
                    
                    _logger.Info($"[WorkspaceViewModel] Closed empty pane - Remaining: {Panes.Count}");
                }
                
                TabClosed?.Invoke(tab);
                StatusMessage = $"Closed {tab.Title}";
                
                _logger.Info($"Tab closed: {tab.Title}");
                
                // Auto-save workspace state
                _ = SaveStateAsync();
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
        
        /// <summary>
        /// Move a tab from one pane to another (used for cross-pane drag & drop)
        /// Part of Milestone 2B: Drag & Drop - Phase 2
        /// CRITICAL: Uses Dispatcher to prevent WPF reentrancy issues during rapid operations
        /// </summary>
        public void MoveTabBetweenPanes(TabViewModel tab, PaneViewModel sourcePaneVm, PaneViewModel targetPaneVm, int insertIndex)
        {
            if (tab == null || sourcePaneVm == null || targetPaneVm == null)
            {
                _logger.Warning("[WorkspaceViewModel] MoveTabBetweenPanes: null parameter");
                return;
            }
            
            // Same pane - just reorder (already handled by TabDragHandler)
            if (sourcePaneVm == targetPaneVm)
                return;
            
            try
            {
                // Validate tab still exists in source pane
                if (!sourcePaneVm.Tabs.Contains(tab))
                {
                    _logger.Warning($"[WorkspaceViewModel] Tab '{tab.Title}' no longer in source pane, skipping move");
                    return;
                }
                
                // CRITICAL FIX v3: Direct modification (simplest, most reliable)
                // Context menu commands already run on UI thread, no Dispatcher needed
                // BeginInvoke was causing async timing issues leading to crashes
                
                // STEP 1: Remove from source (WITHOUT disposing)
                sourcePaneVm.RemoveTabWithoutDispose(tab);
                
                // STEP 2: Add to target pane at specific index
                int safeIndex = Math.Min(insertIndex, targetPaneVm.Tabs.Count);
                targetPaneVm.InsertTab(safeIndex, tab);
                
                // STEP 3: Update selection and active pane
                targetPaneVm.SelectedTab = tab;
                ActivePane = targetPaneVm;
                
                _logger.Info($"[WorkspaceViewModel] Moved tab '{tab.Title}' between panes (index: {safeIndex})");
                
                // STEP 4: Save workspace state (async, fire-and-forget)
                _ = SaveStateAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to move tab '{tab.Title}' between panes");
            }
            finally
            {
                // Release throttle lock
                lock (_moveLock)
                {
                    _isMoveInProgress = false;
                }
                
                // Notify command can execute changed (re-enable context menu)
                (MoveToOtherPaneCommand as NoteNest.Core.Commands.RelayCommand<TabViewModel>)?.RaiseCanExecuteChanged();
            }
        }
        
        #region Milestone 2A: Split View Commands
        
        /// <summary>
        /// Split the workspace vertically (side-by-side panes)
        /// </summary>
        private void ExecuteSplitVertical()
        {
            if (Panes.Count >= 2)
            {
                _logger.Info("[WorkspaceViewModel] Already split - max 2 panes");
                StatusMessage = "Already split";
                return;
            }
            
            // Create new pane
            var newPane = new PaneViewModel();
            Panes.Add(newPane);
            ActivePane = newPane;
            
            // Update CanSplit property
            OnPropertyChanged(nameof(CanSplit));
            
            _logger.Info($"[WorkspaceViewModel] Split vertical - Total panes: {Panes.Count}");
            StatusMessage = "Editor split";
            
            System.Diagnostics.Debug.WriteLine($"[WorkspaceViewModel] Split created - Pane IDs: {string.Join(", ", Panes.Select(p => p.Id))}");
        }
        
        /// <summary>
        /// Switch to a specific pane by index (Ctrl+1, Ctrl+2)
        /// </summary>
        private void ExecuteSwitchToPane(object parameter)
        {
            if (parameter is int index || (parameter is string str && int.TryParse(str, out index)))
            {
                if (index >= 0 && index < Panes.Count)
                {
                    ActivePane = Panes[index];
                    _logger.Debug($"[WorkspaceViewModel] Switched to pane {index + 1}");
                    StatusMessage = $"Switched to pane {index + 1}";
                }
            }
        }
        
        /// <summary>
        /// Close the active pane (merge back to single pane)
        /// </summary>
        private void ExecuteClosePane()
        {
            if (Panes.Count <= 1)
            {
                StatusMessage = "Only one pane open";
                return;
            }
            
            // Protect pane operations with same lock as state snapshots
            lock (_stateSnapshotLock)
            {
                var paneToClose = ActivePane;
                var remainingPane = Panes.FirstOrDefault(p => p != paneToClose);
                
                if (paneToClose != null && remainingPane != null)
                {
                    // Move all tabs from closing pane to remaining pane
                    var tabsToMove = paneToClose.Tabs.ToList();
                    foreach (var tab in tabsToMove)
                    {
                        paneToClose.RemoveTab(tab);
                        remainingPane.AddTab(tab, select: false);
                    }
                    
                    // Remove the pane
                    Panes.Remove(paneToClose);
                    ActivePane = remainingPane;
                
                    OnPropertyChanged(nameof(CanSplit));
                    
                    _logger.Info($"[WorkspaceViewModel] Closed pane - Moved {tabsToMove.Count} tabs to remaining pane");
                    StatusMessage = $"Pane closed - {tabsToMove.Count} tab(s) moved";
                    
                    System.Diagnostics.Debug.WriteLine($"[WorkspaceViewModel] Pane closed - Now {Panes.Count} pane(s)");
                }
            }
        }
        
        #endregion
        
        #region Tier 1 Features: Tab Cycling Commands
        
        /// <summary>
        /// Cycle to next tab (Ctrl+Tab)
        /// Browser/IDE standard behavior
        /// </summary>
        private void ExecuteNextTab()
        {
            if (ActivePane == null || !ActivePane.HasTabs)
                return;
            
            var tabs = ActivePane.Tabs;
            var currentIndex = tabs.IndexOf(ActivePane.SelectedTab);
            
            if (currentIndex == -1)
            {
                // No selection, select first tab
                ActivePane.SelectedTab = tabs.First();
            }
            else
            {
                // Move to next tab (wrap around to first)
                var nextIndex = (currentIndex + 1) % tabs.Count;
                ActivePane.SelectedTab = tabs[nextIndex];
            }
            
            _logger.Debug($"[WorkspaceViewModel] Cycled to next tab: {ActivePane.SelectedTab?.Title}");
        }
        
        /// <summary>
        /// Cycle to previous tab (Ctrl+Shift+Tab)
        /// Browser/IDE standard behavior
        /// </summary>
        private void ExecutePreviousTab()
        {
            if (ActivePane == null || !ActivePane.HasTabs)
                return;
            
            var tabs = ActivePane.Tabs;
            var currentIndex = tabs.IndexOf(ActivePane.SelectedTab);
            
            if (currentIndex == -1)
            {
                // No selection, select last tab
                ActivePane.SelectedTab = tabs.Last();
            }
            else
            {
                // Move to previous tab (wrap around to last)
                var prevIndex = currentIndex == 0 ? tabs.Count - 1 : currentIndex - 1;
                ActivePane.SelectedTab = tabs[prevIndex];
            }
            
            _logger.Debug($"[WorkspaceViewModel] Cycled to previous tab: {ActivePane.SelectedTab?.Title}");
        }
        
        #endregion
        
        #region Tier 1 Features: Context Menu Commands
        
        /// <summary>
        /// Close all tabs in the active pane
        /// </summary>
        private async Task ExecuteCloseAllTabs()
        {
            if (ActivePane == null || !ActivePane.HasTabs)
                return;
            
            try
            {
                var tabsToClose = ActivePane.Tabs.ToList(); // Copy to avoid collection modification
                var count = tabsToClose.Count;
                
                foreach (var tab in tabsToClose)
                {
                    await ExecuteCloseTab(tab);
                }
                
                _logger.Info($"[WorkspaceViewModel] Closed all {count} tabs");
                StatusMessage = $"Closed {count} tab(s)";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to close all tabs");
                StatusMessage = "Error closing tabs";
            }
        }
        
        /// <summary>
        /// Close all tabs except the specified one
        /// </summary>
        private async Task ExecuteCloseOtherTabs(TabViewModel keepTab)
        {
            if (keepTab == null || ActivePane == null)
                return;
            
            try
            {
                var tabsToClose = ActivePane.Tabs.Where(t => t != keepTab).ToList();
                var count = tabsToClose.Count;
                
                if (count == 0)
                {
                    StatusMessage = "No other tabs to close";
                    return;
                }
                
                foreach (var tab in tabsToClose)
                {
                    await ExecuteCloseTab(tab);
                }
                
                _logger.Info($"[WorkspaceViewModel] Closed {count} other tabs, kept '{keepTab.Title}'");
                StatusMessage = $"Closed {count} other tab(s)";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to close other tabs");
                StatusMessage = "Error closing tabs";
            }
        }
        
        /// <summary>
        /// Move tab to the other pane (smart: auto-splits if needed)
        /// CRITICAL: Uses throttling to prevent WPF crashes during rapid operations
        /// </summary>
        private void ExecuteMoveToOtherPane(TabViewModel tab)
        {
            if (tab == null)
                return;
            
            // CRITICAL: Prevent overlapping move operations (causes WPF crashes)
            lock (_moveLock)
            {
                if (_isMoveInProgress)
                {
                    _logger.Debug($"[WorkspaceViewModel] Move already in progress, skipping for '{tab.Title}'");
                    return;
                }
                _isMoveInProgress = true;
            }
            
            try
            {
                // If only 1 pane, split first
                if (Panes.Count == 1)
                {
                    ExecuteSplitVertical();
                    _logger.Info("[WorkspaceViewModel] Auto-split workspace for 'Move to Other Pane'");
                }
                
                // Find source and target panes
                var sourcePaneVm = FindPaneContainingTab(tab);
                var targetPaneVm = Panes.FirstOrDefault(p => p != sourcePaneVm);
                
                if (sourcePaneVm == null || targetPaneVm == null)
                {
                    _logger.Warning("[WorkspaceViewModel] Could not find source or target pane");
                    return;
                }
                
                // Move tab to target pane (append to end)
                MoveTabBetweenPanes(tab, sourcePaneVm, targetPaneVm, targetPaneVm.Tabs.Count);
                
                _logger.Info($"[WorkspaceViewModel] Moved tab '{tab.Title}' to other pane");
                StatusMessage = $"Moved '{tab.Title}' to other pane";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to move tab '{tab?.Title}' to other pane");
                StatusMessage = "Error moving tab";
            }
        }
        
        /// <summary>
        /// Check if a tab can be moved to other pane (not during another move operation)
        /// </summary>
        private bool CanMoveToOtherPane(TabViewModel tab)
        {
            return tab != null && !_isMoveInProgress;
        }
        
        #endregion
        
        #region Workspace Persistence (Milestone 2A Completion)
        
        /// <summary>
        /// Save current workspace state (tabs and panes) to disk
        /// Called automatically on tab/pane changes and app exit
        /// Uses debouncing to prevent race conditions during rapid operations
        /// </summary>
        public async Task SaveStateAsync()
        {
            // Debouncing: If already saving, mark as pending and return
            if (_isSaving)
            {
                _savePending = true;
                return;
            }
            
            _isSaving = true;
            
            try
            {
                // Loop to handle pending saves that occurred during save
                do
                {
                    _savePending = false;
                    
                    // CRITICAL: Take snapshot atomically to prevent collection modification exceptions
                    WorkspaceState state;
                    lock (_stateSnapshotLock)
                    {
                        var panesSnapshot = Panes.ToList();
                        var activePaneSnapshot = ActivePane;
                        
                        state = new WorkspaceState
                        {
                            PaneCount = panesSnapshot.Count,
                            ActivePaneIndex = Math.Max(0, panesSnapshot.IndexOf(activePaneSnapshot))
                        };
                        
                        // Capture state of each pane (using snapshot)
                        foreach (var pane in panesSnapshot)
                        {
                            var tabsSnapshot = pane.Tabs.ToList(); // Snapshot tabs too
                            
                            var paneState = new PaneState
                            {
                                Tabs = tabsSnapshot.Select(t => new TabState
                                {
                                    TabId = t.TabId,
                                    FilePath = t.Note.FilePath,
                                    Title = t.Title
                                }).ToList(),
                                ActiveTabId = pane.SelectedTab?.TabId
                            };
                            
                            state.Panes.Add(paneState);
                        }
                    }
                    
                    // Perform I/O outside lock for better concurrency
                    await _workspacePersistence.SaveAsync(state);
                    
                    _logger.Debug($"[WorkspaceViewModel] Saved workspace state: {state.Panes.Sum(p => p.Tabs.Count)} total tabs");
                }
                while (_savePending); // If another save was requested during save, do it now
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[WorkspaceViewModel] Failed to save workspace state");
                // Don't throw - save failures shouldn't crash the app
            }
            finally
            {
                _isSaving = false;
            }
        }
        
        /// <summary>
        /// Restore workspace state (tabs and panes) from disk
        /// Called during app startup
        /// </summary>
        public async Task RestoreStateAsync()
        {
            try
            {
                var state = await _workspacePersistence.LoadAsync();
                if (state == null)
                {
                    _logger.Info("[WorkspaceViewModel] No saved state to restore");
                    return;
                }
                
                _logger.Info($"[WorkspaceViewModel] Restoring workspace: {state.PaneCount} pane(s), " +
                           $"{state.Panes.Sum(p => p.Tabs.Count)} total tabs");
                
                // Create second pane if needed
                if (state.PaneCount == 2 && Panes.Count == 1)
                {
                    ExecuteSplitVertical();
                    _logger.Debug("[WorkspaceViewModel] Created second pane for split view");
                }
                
                // Restore tabs to each pane
                for (int paneIndex = 0; paneIndex < state.Panes.Count && paneIndex < Panes.Count; paneIndex++)
                {
                    var paneState = state.Panes[paneIndex];
                    var targetPane = Panes[paneIndex];
                    
                    // Temporarily set as active pane for OpenNoteAsync
                    var previousActivePane = ActivePane;
                    ActivePane = targetPane;
                    
                    foreach (var tabState in paneState.Tabs)
                    {
                        try
                        {
                            // Check if file still exists
                            if (!System.IO.File.Exists(tabState.FilePath))
                            {
                                _logger.Warning($"[WorkspaceViewModel] Skipping missing file: {tabState.FilePath}");
                                continue;
                            }
                            
                            // Create minimal Note domain object for opening
                            // Note: OpenNoteAsync will load content from file, we just need basic metadata
                            var domainNote = Note.CreateForOpening(
                                tabState.Title,
                                tabState.FilePath
                            );
                            
                            // Open note (this will create tab in active pane)
                            await OpenNoteAsync(domainNote);
                            
                            _logger.Debug($"[WorkspaceViewModel] Restored tab: {tabState.Title}");
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, $"[WorkspaceViewModel] Failed to restore tab: {tabState.FilePath}");
                            // Continue with other tabs
                        }
                    }
                    
                    // Restore selected tab in this pane
                    if (!string.IsNullOrEmpty(paneState.ActiveTabId))
                    {
                        var tabToSelect = targetPane.Tabs.FirstOrDefault(t => t.TabId == paneState.ActiveTabId);
                        if (tabToSelect != null)
                        {
                            targetPane.SelectedTab = tabToSelect;
                        }
                    }
                    
                    // Restore previous active pane (will set correct one below)
                    ActivePane = previousActivePane;
                }
                
                // Restore active pane
                if (state.ActivePaneIndex >= 0 && state.ActivePaneIndex < Panes.Count)
                {
                    ActivePane = Panes[state.ActivePaneIndex];
                }
                
                _logger.Info($"[WorkspaceViewModel] Workspace restored: {Panes.Sum(p => p.Tabs.Count)} tabs opened");
                StatusMessage = $"Restored {Panes.Sum(p => p.Tabs.Count)} tabs";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[WorkspaceViewModel] Failed to restore workspace state");
                // Don't throw - restoration failures shouldn't prevent app startup
            }
        }
        
        #endregion
        
        #region Tear-Out Command Implementations (Phase 7)
        
        /// <summary>
        /// Detach single tab to new window
        /// </summary>
        private async Task ExecuteDetachTab(TabViewModel tab)
        {
            if (tab == null || _windowManager == null) return;
            
            try
            {
                _logger.Info($"[WorkspaceViewModel] Detaching tab: {tab.Title}");
                
                // Remove from current pane FIRST (without dispose)
                var sourcePane = FindPaneContainingTab(tab);
                sourcePane?.RemoveTabWithoutDispose(tab);
                
                // Create new detached window with this tab
                var initialTabs = new List<TabViewModel> { tab };
                var detachedWindow = await _windowManager.CreateDetachedWindowAsync(initialTabs);
                
                if (detachedWindow != null)
                {
                    
                    // Track memory usage
                    SimpleMemoryTracker.TrackDetachedWindowCreation(detachedWindow.WindowId);
                    
                    _logger.Info($"[WorkspaceViewModel] Tab detached successfully: {tab.Title} to window {detachedWindow.WindowId}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[WorkspaceViewModel] Failed to detach tab: {tab?.Title}");
            }
        }
        
        /// <summary>
        /// Detach all other tabs to new window (keep current tab in main window)
        /// </summary>
        private async Task ExecuteDetachOtherTabs(TabViewModel keepTab)
        {
            if (keepTab == null || _windowManager == null) return;
            
            try
            {
                var sourcePane = FindPaneContainingTab(keepTab);
                if (sourcePane == null) return;
                
                // Get all other tabs
                var otherTabs = sourcePane.Tabs.Where(t => t != keepTab).ToList();
                if (!otherTabs.Any()) return;
                
                _logger.Info($"[WorkspaceViewModel] Detaching {otherTabs.Count} other tabs, keeping: {keepTab.Title}");
                
                // Create new detached window with other tabs
                var detachedWindow = await _windowManager.CreateDetachedWindowAsync(otherTabs);
                
                if (detachedWindow != null)
                {
                    // Remove other tabs from source pane
                    foreach (var tab in otherTabs)
                    {
                        sourcePane.RemoveTab(tab);
                    }
                    
                    // Track memory usage
                    SimpleMemoryTracker.TrackDetachedWindowCreation(detachedWindow.WindowId);
                    
                    _logger.Info($"[WorkspaceViewModel] {otherTabs.Count} tabs detached to window {detachedWindow.WindowId}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[WorkspaceViewModel] Failed to detach other tabs");
            }
        }
        
        /// <summary>
        /// Redock single tab from detached window to main window
        /// </summary>
        private async Task ExecuteRedockTab(TabViewModel tab)
        {
            if (tab == null || _windowManager == null) return;
            
            try
            {
                var sourceWindow = _windowManager.FindWindowContainingTab(tab);
                if (sourceWindow == null) return;
                
                _logger.Info($"[WorkspaceViewModel] Redocking tab: {tab.Title} from window {sourceWindow.WindowId}");
                
                await _windowManager.RedockTabAsync(tab, sourceWindow);
                
                // Add to active pane in main window
                ActivePane.AddTab(tab, true);
                
                _logger.Info($"[WorkspaceViewModel] Tab redocked successfully: {tab.Title}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[WorkspaceViewModel] Failed to redock tab: {tab?.Title}");
            }
        }
        
        /// <summary>
        /// Redock all tabs from detached windows to main window
        /// </summary>
        private async Task ExecuteRedockAllTabs()
        {
            if (_windowManager == null) return;
            
            try
            {
                var detachedWindows = _windowManager.DetachedWindows.ToList();
                if (!detachedWindows.Any()) return;
                
                _logger.Info($"[WorkspaceViewModel] Redocking all tabs from {detachedWindows.Count} detached windows");
                
                foreach (var window in detachedWindows)
                {
                    await _windowManager.RedockAllTabsAsync(window);
                }
                
                _logger.Info("[WorkspaceViewModel] All tabs redocked successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[WorkspaceViewModel] Failed to redock all tabs");
            }
        }
        
        // CanExecute methods for tear-out commands
        private bool CanDetachTab(TabViewModel tab) => tab != null && _windowManager != null && !_windowManager.IsAtWindowLimit();
        private bool CanDetachOtherTabs(TabViewModel tab) => tab != null && _windowManager != null && ActivePane?.Tabs?.Count > 1 && !_windowManager.IsAtWindowLimit();
        private bool CanRedockTab(TabViewModel tab) => tab != null && _windowManager != null && _windowManager.FindWindowContainingTab(tab) != null;
        private bool CanRedockAllTabs() => _windowManager?.DetachedWindows?.Any() == true;
        
        
        #endregion
    }
}

