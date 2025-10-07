using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;
using NoteNest.UI.ViewModels.Common;
using NoteNest.UI.ViewModels.Workspace;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Commands;

namespace NoteNest.UI.ViewModels.Windows
{
    /// <summary>
    /// ViewModel for detached windows in tear-out functionality
    /// Manages a single pane with multiple tabs
    /// Coordinates with WindowManager for cross-window operations
    /// </summary>
    public class DetachedWindowViewModel : ViewModelBase
    {
        private readonly IAppLogger _logger;
        private string _windowId;
        private string _windowTitle;
        private PaneViewModel _paneViewModel;
        private TabViewModel _selectedTab;
        private WindowBounds _bounds;
        private bool _isMaximized;
        
        // Events for coordination with WindowManager
        public event EventHandler<DetachedWindowViewModel> WindowClosed;
        public event EventHandler<(DetachedWindowViewModel Window, WindowBounds Bounds, bool IsMaximized)> BoundsChanged;
        public event EventHandler<TabViewModel> TabCloseRequested;
        public event EventHandler<List<TabViewModel>> RedockRequested;
        
        public DetachedWindowViewModel(IAppLogger logger = null)
        {
            _logger = logger;
            _windowId = Guid.NewGuid().ToString();
            _windowTitle = "NoteNest - Detached";
            _bounds = new WindowBounds 
            { 
                Left = 200, 
                Top = 200, 
                Width = 900, 
                Height = 700 
            };
            
            // Create pane for this window
            _paneViewModel = new PaneViewModel(_windowId);
            _paneViewModel.PropertyChanged += OnPanePropertyChanged;
            
            // Initialize commands
            InitializeCommands();
            
            _logger?.Debug($"[DetachedWindowViewModel] Created: {_windowId}");
        }
        
        #region Properties
        
        public string WindowId 
        { 
            get => _windowId; 
            private set => SetProperty(ref _windowId, value); 
        }
        
        public string WindowTitle 
        { 
            get => _windowTitle; 
            set => SetProperty(ref _windowTitle, value); 
        }
        
        public PaneViewModel PaneViewModel 
        { 
            get => _paneViewModel; 
            private set => SetProperty(ref _paneViewModel, value); 
        }
        
        public TabViewModel SelectedTab
        {
            get => _selectedTab;
            set => SetProperty(ref _selectedTab, value);
        }
        
        public WindowBounds Bounds
        {
            get => _bounds;
            set => SetProperty(ref _bounds, value);
        }
        
        public bool IsMaximized
        {
            get => _isMaximized;
            set => SetProperty(ref _isMaximized, value);
        }
        
        public int TabCount => PaneViewModel?.Tabs?.Count ?? 0;
        public bool HasTabs => TabCount > 0;
        public bool HasUnsavedTabs => PaneViewModel?.Tabs?.Any(t => t.IsDirty) ?? false;
        
        #endregion
        
        #region Commands
        
        public ICommand SaveTabCommand { get; private set; }
        public ICommand SaveAllTabsCommand { get; private set; }
        public ICommand CloseTabCommand { get; private set; }
        public ICommand CloseWindowCommand { get; private set; }
        public ICommand RedockAllTabsCommand { get; private set; }
        public ICommand NextTabCommand { get; private set; }
        public ICommand PreviousTabCommand { get; private set; }
        
        private void InitializeCommands()
        {
            SaveTabCommand = new RelayCommand<TabViewModel>(
                async tab => await SaveTabAsync(tab),
                tab => tab?.IsDirty == true);
                
            SaveAllTabsCommand = new RelayCommand(
                async () => await SaveAllTabsAsync(),
                () => HasUnsavedTabs);
                
            CloseTabCommand = new RelayCommand<TabViewModel>(
                async tab => await CloseTabAsync(tab),
                tab => tab != null);
                
            CloseWindowCommand = new RelayCommand(
                async () => await CloseWindowAsync(),
                () => true);
                
            RedockAllTabsCommand = new RelayCommand(
                async () => await RedockAllTabsAsync(),
                () => HasTabs);
                
            NextTabCommand = new RelayCommand(
                () => SelectNextTab(),
                () => TabCount > 1);
                
            PreviousTabCommand = new RelayCommand(
                () => SelectPreviousTab(),
                () => TabCount > 1);
        }
        
        #endregion
        
        #region Tab Management
        
        /// <summary>
        /// Add tab to this detached window
        /// </summary>
        public void AddTab(TabViewModel tab, bool select = true)
        {
            if (tab == null) return;
            
            PaneViewModel.AddTab(tab, select);
            
            if (select)
            {
                SelectedTab = tab;
            }
            
            UpdateWindowTitle();
            RefreshCommandStates();
            
            _logger?.Debug($"[DetachedWindow] Tab added: {tab.Title} to window {WindowId}");
        }
        
        /// <summary>
        /// Remove tab from this detached window
        /// </summary>
        public void RemoveTab(TabViewModel tab)
        {
            if (tab == null) return;
            
            PaneViewModel.RemoveTab(tab);
            
            if (SelectedTab == tab)
            {
                SelectedTab = PaneViewModel.SelectedTab;
            }
            
            UpdateWindowTitle();
            RefreshCommandStates();
            
            _logger?.Debug($"[DetachedWindow] Tab removed: {tab.Title} from window {WindowId}");
        }
        
        /// <summary>
        /// Get all tabs in this window
        /// </summary>
        public List<TabViewModel> GetAllTabs()
        {
            return PaneViewModel.Tabs?.ToList() ?? new List<TabViewModel>();
        }
        
        private void SelectNextTab()
        {
            var tabs = PaneViewModel.Tabs;
            if (tabs?.Count <= 1) return;
            
            var currentIndex = tabs.IndexOf(SelectedTab);
            var nextIndex = (currentIndex + 1) % tabs.Count;
            PaneViewModel.SelectedTab = tabs[nextIndex];
        }
        
        private void SelectPreviousTab()
        {
            var tabs = PaneViewModel.Tabs;
            if (tabs?.Count <= 1) return;
            
            var currentIndex = tabs.IndexOf(SelectedTab);
            var previousIndex = (currentIndex - 1 + tabs.Count) % tabs.Count;
            PaneViewModel.SelectedTab = tabs[previousIndex];
        }
        
        private void UpdateWindowTitle()
        {
            var tabCount = TabCount;
            if (tabCount == 0)
            {
                WindowTitle = "NoteNest - Detached";
            }
            else if (tabCount == 1)
            {
                var tab = PaneViewModel.Tabs?.FirstOrDefault();
                WindowTitle = $"NoteNest - {tab?.Title ?? "Untitled"}";
            }
            else
            {
                WindowTitle = $"NoteNest - {tabCount} tabs";
            }
        }
        
        #endregion
        
        #region Command Implementations
        
        private async Task SaveTabAsync(TabViewModel tab)
        {
            if (tab?.IsDirty != true) return;
            
            try
            {
                // Tab save logic will be handled by WorkspaceViewModel/SaveManager
                // Note: IsDirty is managed internally by TabViewModel, no need to set here
                RefreshCommandStates();
                
                _logger?.Info($"[DetachedWindow] Tab saved: {tab.Title}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"[DetachedWindow] Failed to save tab: {tab?.Title}");
            }
        }
        
        private async Task SaveAllTabsAsync()
        {
            var dirtyTabs = PaneViewModel.Tabs?.Where(t => t.IsDirty).ToList();
            if (dirtyTabs?.Any() != true) return;
            
            try
            {
                foreach (var tab in dirtyTabs)
                {
                    await SaveTabAsync(tab);
                }
                
                _logger?.Info($"[DetachedWindow] All tabs saved: {dirtyTabs.Count} tabs");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[DetachedWindow] Failed to save all tabs");
            }
        }
        
        private async Task CloseTabAsync(TabViewModel tab)
        {
            if (tab == null) return;
            
            try
            {
                // Check if tab has unsaved changes
                if (tab.IsDirty)
                {
                    // TODO: Show save dialog (integration in Phase 7)
                    // For now, just save automatically
                    await SaveTabAsync(tab);
                }
                
                // Fire event for WindowManager to handle
                TabCloseRequested?.Invoke(this, tab);
                
                _logger?.Info($"[DetachedWindow] Tab close requested: {tab.Title}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"[DetachedWindow] Failed to close tab: {tab?.Title}");
            }
        }
        
        private async Task CloseWindowAsync()
        {
            try
            {
                // Save any unsaved tabs
                if (HasUnsavedTabs)
                {
                    await SaveAllTabsAsync();
                }
                
                // Fire event for WindowManager to handle window closure
                WindowClosed?.Invoke(this, this);
                
                _logger?.Info($"[DetachedWindow] Window close requested: {WindowId}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"[DetachedWindow] Failed to close window: {WindowId}");
            }
        }
        
        private async Task RedockAllTabsAsync()
        {
            try
            {
                var allTabs = GetAllTabs();
                if (!allTabs.Any()) return;
                
                // Fire event for WindowManager to handle redocking
                RedockRequested?.Invoke(this, allTabs);
                
                _logger?.Info($"[DetachedWindow] Redock requested: {allTabs.Count} tabs from {WindowId}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"[DetachedWindow] Failed to redock tabs from: {WindowId}");
            }
        }
        
        #endregion
        
        #region Window Event Handlers
        
        /// <summary>
        /// Called when window state changes (minimize/maximize/restore)
        /// </summary>
        public void OnWindowStateChanged(System.Windows.WindowState state)
        {
            IsMaximized = state == System.Windows.WindowState.Maximized;
            
            // Fire bounds changed event for persistence
            BoundsChanged?.Invoke(this, (this, Bounds, IsMaximized));
        }
        
        /// <summary>
        /// Called when window bounds change (move/resize)
        /// </summary>
        public void OnWindowBoundsChanged(double left, double top, double width, double height)
        {
            Bounds = new WindowBounds
            {
                Left = left,
                Top = top,
                Width = width,
                Height = height
            };
            
            // Fire bounds changed event for persistence
            BoundsChanged?.Invoke(this, (this, Bounds, IsMaximized));
        }
        
        /// <summary>
        /// Called when window is closing - validation check
        /// </summary>
        public bool CanCloseWindow()
        {
            // TODO: Show unsaved changes dialog if needed (Phase 7 integration)
            return true; // For now, always allow close
        }
        
        /// <summary>
        /// Called when window is closed - cleanup
        /// </summary>
        public void OnWindowClosed()
        {
            // Cleanup event subscriptions
            if (_paneViewModel != null)
            {
                _paneViewModel.PropertyChanged -= OnPanePropertyChanged;
            }
        }
        
        #endregion
        
        #region Event Handlers
        
        private void OnPanePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PaneViewModel.SelectedTab))
            {
                SelectedTab = PaneViewModel.SelectedTab;
            }
            
            RefreshCommandStates();
            UpdateWindowTitle();
        }
        
        private void RefreshCommandStates()
        {
            // Notify all commands to re-evaluate their CanExecute state
            OnPropertyChanged(nameof(HasTabs));
            OnPropertyChanged(nameof(HasUnsavedTabs));
            OnPropertyChanged(nameof(TabCount));
        }
        
        #endregion
        
        #region State Conversion
        
        /// <summary>
        /// Convert to DetachedWindowState for persistence
        /// </summary>
        public DetachedWindowState ToState()
        {
            return new DetachedWindowState
            {
                WindowId = WindowId,
                Title = WindowTitle,
                Bounds = new WindowBounds
                {
                    Left = Bounds.Left,
                    Top = Bounds.Top,
                    Width = Bounds.Width,
                    Height = Bounds.Height
                },
                Tabs = PaneViewModel.Tabs?.Select(tab => new TabState
                {
                    TabId = tab.TabId,
                    FilePath = tab.Note?.FilePath ?? "",
                    Title = tab.Title
                }).ToList() ?? new List<TabState>(),
                ActiveTabId = SelectedTab?.TabId,
                IsMaximized = IsMaximized,
                MonitorIndex = -1 // TODO: Detect monitor in Phase 6
            };
        }
        
        /// <summary>
        /// Restore from DetachedWindowState
        /// </summary>
        public static DetachedWindowViewModel FromState(DetachedWindowState state, IAppLogger logger = null)
        {
            var viewModel = new DetachedWindowViewModel(logger)
            {
                WindowId = state.WindowId,
                WindowTitle = state.Title,
                Bounds = state.Bounds,
                IsMaximized = state.IsMaximized
            };
            
            // Tabs will be restored in Phase 7 integration
            
            return viewModel;
        }
        
        #endregion
    }
}
