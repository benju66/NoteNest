using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Diagnostics;
using NoteNest.UI.ViewModels.Windows;
using NoteNest.UI.ViewModels.Workspace;
using NoteNest.UI.Windows;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Central coordinator for multi-window operations in tear-out functionality
    /// Manages detached windows, coordinates cross-window drag operations,
    /// handles window lifecycle, and maintains window state persistence
    /// </summary>
    public interface IWindowManager
    {
        /// <summary>
        /// All currently active detached windows
        /// </summary>
        IReadOnlyList<DetachedWindowViewModel> DetachedWindows { get; }
        
        /// <summary>
        /// Events for UI coordination
        /// </summary>
        event EventHandler<DetachedWindowViewModel> WindowCreated;
        event EventHandler<DetachedWindowViewModel> WindowClosed;
        event EventHandler<(TabViewModel Tab, DetachedWindowViewModel SourceWindow, DetachedWindowViewModel TargetWindow)> TabMoved;
        
        /// <summary>
        /// Create new detached window with initial tabs
        /// </summary>
        Task<DetachedWindowViewModel> CreateDetachedWindowAsync(List<TabViewModel> initialTabs, Point? preferredPosition = null);
        
        /// <summary>
        /// Close detached window and handle tab redocking
        /// </summary>
        Task CloseDetachedWindowAsync(DetachedWindowViewModel window, bool redockTabs = false);
        
        /// <summary>
        /// Move tab between windows (or from main window to detached)
        /// </summary>
        Task MoveTabToWindowAsync(TabViewModel tab, DetachedWindowViewModel targetWindow, int insertIndex = -1);
        
        /// <summary>
        /// Move tab from detached window back to main window
        /// </summary>
        Task RedockTabAsync(TabViewModel tab, DetachedWindowViewModel sourceWindow);
        
        /// <summary>
        /// Redock all tabs from a detached window back to main window
        /// </summary>
        Task RedockAllTabsAsync(DetachedWindowViewModel window);
        
        /// <summary>
        /// Find which detached window contains a specific tab
        /// </summary>
        DetachedWindowViewModel FindWindowContainingTab(TabViewModel tab);
        
        /// <summary>
        /// Find detached window by ID
        /// </summary>
        DetachedWindowViewModel FindWindowById(string windowId);
        
        /// <summary>
        /// Check if we're at the window limit (soft limit for performance)
        /// </summary>
        bool IsAtWindowLimit();
        
        /// <summary>
        /// Get current state for persistence
        /// </summary>
        List<DetachedWindowState> GetWindowStates();
        
        /// <summary>
        /// Restore windows from saved state
        /// </summary>
        Task RestoreWindowsAsync(List<DetachedWindowState> states);
    }
    
    public class WindowManager : IWindowManager, IDisposable
    {
        private readonly IAppLogger _logger;
        private readonly List<DetachedWindowViewModel> _detachedWindows = new();
        private readonly List<DetachedWindow> _detachedWpfWindows = new(); // Track WPF windows too
        private readonly object _windowsLock = new object();
        private ViewModels.Workspace.WorkspaceViewModel _mainWorkspace;
        
        // Soft limit for detached windows (performance consideration)
        private const int MAX_DETACHED_WINDOWS = 5;
        
        // Events
        public event EventHandler<DetachedWindowViewModel> WindowCreated;
        public event EventHandler<DetachedWindowViewModel> WindowClosed;
        public event EventHandler<(TabViewModel Tab, DetachedWindowViewModel SourceWindow, DetachedWindowViewModel TargetWindow)> TabMoved;
        
        public IReadOnlyList<DetachedWindowViewModel> DetachedWindows 
        { 
            get 
            { 
                lock (_windowsLock) 
                { 
                    return _detachedWindows.ToList(); 
                } 
            } 
        }
        
        public WindowManager(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.Debug("[WindowManager] Initialized");
        }

        /// <summary>
        /// Set the main workspace for redocking operations
        /// </summary>
        public void SetMainWorkspace(ViewModels.Workspace.WorkspaceViewModel workspace)
        {
            _mainWorkspace = workspace;
            _logger.Debug("[WindowManager] Main workspace reference set");
        }
        
        #region Window Creation & Destruction
        
        public async Task<DetachedWindowViewModel> CreateDetachedWindowAsync(List<TabViewModel> initialTabs, Point? preferredPosition = null)
        {
            _logger.Debug($"[WindowManager] CreateDetachedWindowAsync called with {initialTabs?.Count ?? 0} tabs, preferredPosition: {preferredPosition}");
            
            if (IsAtWindowLimit())
            {
                _logger.Warning($"[WindowManager] At window limit ({MAX_DETACHED_WINDOWS}), cannot create new detached window");
                return null;
            }
            
            try
            {
                // Create new detached window ViewModel
                var detachedWindowViewModel = new DetachedWindowViewModel(_logger);
                
                // Add initial tabs
                _logger.Debug($"[WindowManager] Adding {initialTabs?.Count ?? 0} initial tabs to detached window");
                foreach (var tab in initialTabs ?? new List<TabViewModel>())
                {
                    _logger.Debug($"[WindowManager] Adding tab '{tab?.Title}' to detached window {detachedWindowViewModel.WindowId}");
                    detachedWindowViewModel.AddTab(tab);
                    _logger.Debug($"[WindowManager] Tab added. Detached window now has {detachedWindowViewModel.TabCount} tabs");
                }
                
                // Update window title based on tabs
                UpdateWindowTitle(detachedWindowViewModel);
                
                // Subscribe to window events
                SubscribeToWindowEvents(detachedWindowViewModel);
                
                // Create actual WPF window
                var detachedWpfWindow = new DetachedWindow(_logger);
                detachedWpfWindow.SetViewModel(detachedWindowViewModel);
                
                
                // Set owner to main window for proper task bar behavior
                if (System.Windows.Application.Current?.MainWindow != null)
                {
                    detachedWpfWindow.Owner = System.Windows.Application.Current.MainWindow;
                }
                
                // Apply smart positioning if preferred position is provided
                if (preferredPosition.HasValue)
                {
                    ApplySmartPositioning(detachedWpfWindow, preferredPosition.Value);
                }
                
                // Add to collections
                lock (_windowsLock)
                {
                    _detachedWindows.Add(detachedWindowViewModel);
                    _detachedWpfWindows.Add(detachedWpfWindow);
                }
                
                // Show window
                detachedWpfWindow.Show();
                
                // Track memory usage
                SimpleMemoryTracker.TrackDetachedWindowCreation(detachedWindowViewModel.WindowId);
                
                // Fire event
                WindowCreated?.Invoke(this, detachedWindowViewModel);
                
                _logger.Info($"[WindowManager] Detached window created and shown: {detachedWindowViewModel.WindowId} with {initialTabs?.Count ?? 0} tabs");
                
                return detachedWindowViewModel;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[WindowManager] Failed to create detached window");
                return null;
            }
        }
        
        public async Task CloseDetachedWindowAsync(DetachedWindowViewModel windowViewModel, bool redockTabs = false)
        {
            if (windowViewModel == null) return;
            
            try
            {
                // Handle tabs
                if (redockTabs)
                {
                    await RedockAllTabsAsync(windowViewModel);
                }
                else
                {
                    // TODO: Save any unsaved tabs (Phase 7 integration)
                    var unsavedTabs = windowViewModel.GetAllTabs().Where(t => t.IsDirty).ToList();
                    if (unsavedTabs.Any())
                    {
                        _logger.Info($"[WindowManager] Closing window with {unsavedTabs.Count} unsaved tabs: {windowViewModel.WindowId}");
                        // Auto-save for now (Phase 7 will add user confirmation)
                    }
                }
                
                // Find and close WPF window
                DetachedWindow wpfWindow = null;
                lock (_windowsLock)
                {
                    wpfWindow = _detachedWpfWindows.FirstOrDefault(w => 
                        (w.DataContext as DetachedWindowViewModel)?.WindowId == windowViewModel.WindowId);
                    
                    if (wpfWindow != null)
                    {
                        _detachedWpfWindows.Remove(wpfWindow);
                    }
                    
                    _detachedWindows.Remove(windowViewModel);
                }
                
                // Close WPF window
                wpfWindow?.Close();
                
                // Unsubscribe from events
                UnsubscribeFromWindowEvents(windowViewModel);
                
                // Track memory usage
                SimpleMemoryTracker.TrackDetachedWindowDisposal(windowViewModel.WindowId);
                
                // Fire event
                WindowClosed?.Invoke(this, windowViewModel);
                
                // Cleanup ViewModel
                windowViewModel.OnWindowClosed();
                
                _logger.Info($"[WindowManager] Detached window closed: {windowViewModel.WindowId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[WindowManager] Failed to close detached window: {windowViewModel?.WindowId}");
            }
        }
        
        #endregion
        
        #region Tab Movement
        
        public async Task MoveTabToWindowAsync(TabViewModel tab, DetachedWindowViewModel targetWindow, int insertIndex = -1)
        {
            if (tab == null || targetWindow == null) return;
            
            try
            {
                // Find source window (could be main window or another detached window)
                var sourceWindow = FindWindowContainingTab(tab);
                
                // Remove from source
                sourceWindow?.RemoveTab(tab);
                
                // Add to target
                if (insertIndex >= 0 && insertIndex < targetWindow.PaneViewModel.Tabs.Count)
                {
                    targetWindow.PaneViewModel.Tabs.Insert(insertIndex, tab);
                }
                else
                {
                    targetWindow.AddTab(tab);
                }
                
                // Update window titles
                UpdateWindowTitle(targetWindow);
                if (sourceWindow != null)
                {
                    UpdateWindowTitle(sourceWindow);
                }
                
                // Fire event
                TabMoved?.Invoke(this, (tab, sourceWindow, targetWindow));
                
                _logger.Debug($"[WindowManager] Tab moved: {tab.Title} to window {targetWindow.WindowId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[WindowManager] Failed to move tab: {tab?.Title}");
            }
        }
        
        public async Task RedockTabAsync(TabViewModel tab, DetachedWindowViewModel sourceWindow)
        {
            if (tab == null || sourceWindow == null) return;
            
            try
            {
                // Remove from detached window
                sourceWindow.RemoveTab(tab);
                
                // Add to main window's active pane
                if (_mainWorkspace != null)
                {
                    _mainWorkspace.ActivePane?.AddTab(tab, true);
                    _logger.Info($"[WindowManager] Tab redocked to main window: {tab.Title} from {sourceWindow.WindowId}");
                }
                else
                {
                    _logger.Warning($"[WindowManager] Cannot redock tab - no main workspace reference: {tab.Title}");
                }
                
                // Update source window title
                UpdateWindowTitle(sourceWindow);
                
                // If window is now empty, close it
                if (!sourceWindow.HasTabs)
                {
                    await CloseDetachedWindowAsync(sourceWindow);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[WindowManager] Failed to redock tab: {tab?.Title}");
            }
        }
        
        public async Task RedockAllTabsAsync(DetachedWindowViewModel window)
        {
            if (window == null) return;
            
            try
            {
                var allTabs = window.GetAllTabs().ToList();
                
                foreach (var tab in allTabs)
                {
                    await RedockTabAsync(tab, window);
                }
                
                _logger.Info($"[WindowManager] All tabs redocked: {allTabs.Count} tabs from {window.WindowId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[WindowManager] Failed to redock all tabs from: {window?.WindowId}");
            }
        }
        
        #endregion
        
        #region Queries
        
        public DetachedWindowViewModel FindWindowContainingTab(TabViewModel tab)
        {
            if (tab == null) return null;
            
            lock (_windowsLock)
            {
                return _detachedWindows.FirstOrDefault(w => 
                    w.PaneViewModel.Tabs?.Contains(tab) == true);
            }
        }
        
        public DetachedWindowViewModel FindWindowById(string windowId)
        {
            if (string.IsNullOrEmpty(windowId)) return null;
            
            lock (_windowsLock)
            {
                return _detachedWindows.FirstOrDefault(w => w.WindowId == windowId);
            }
        }
        
        public bool IsAtWindowLimit()
        {
            lock (_windowsLock)
            {
                return _detachedWindows.Count >= MAX_DETACHED_WINDOWS;
            }
        }
        
        #endregion
        
        #region Persistence
        
        public List<DetachedWindowState> GetWindowStates()
        {
            lock (_windowsLock)
            {
                return _detachedWindows.Select(w => w.ToState()).ToList();
            }
        }
        
        public async Task RestoreWindowsAsync(List<DetachedWindowState> states)
        {
            if (states?.Any() != true) return;
            
            try
            {
                foreach (var state in states)
                {
                    // Create window ViewModel from state
                    var viewModel = DetachedWindowViewModel.FromState(state, _logger);
                    
                    // Subscribe to events
                    SubscribeToWindowEvents(viewModel);
                    
                    // Create actual WPF window
                    var detachedWpfWindow = new DetachedWindow(_logger);
                    detachedWpfWindow.SetViewModel(viewModel);
                    
                    // Restore window bounds
                    detachedWpfWindow.RestoreBounds(state.Bounds.Left, state.Bounds.Top, 
                                                   state.Bounds.Width, state.Bounds.Height, 
                                                   state.IsMaximized);
                    
                    // Set owner
                    if (System.Windows.Application.Current?.MainWindow != null)
                    {
                        detachedWpfWindow.Owner = System.Windows.Application.Current.MainWindow;
                    }
                    
                    // Add to collections
                    lock (_windowsLock)
                    {
                        _detachedWindows.Add(viewModel);
                        _detachedWpfWindows.Add(detachedWpfWindow);
                    }
                    
                    // Show window
                    detachedWpfWindow.Show();
                    
                    // Track memory
                    SimpleMemoryTracker.TrackDetachedWindowCreation(viewModel.WindowId);
                    
                    // Fire event
                    WindowCreated?.Invoke(this, viewModel);
                    
                    _logger.Debug($"[WindowManager] Restored detached window: {viewModel.WindowId}");
                }
                
                _logger.Info($"[WindowManager] Restored {states.Count} detached windows");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[WindowManager] Failed to restore detached windows");
            }
        }
        
        #endregion
        
        #region Event Management
        
        private void SubscribeToWindowEvents(DetachedWindowViewModel window)
        {
            window.WindowClosed += OnWindowClosed;
            window.TabCloseRequested += OnTabCloseRequested;
            window.RedockRequested += OnRedockRequested;
            window.BoundsChanged += OnWindowBoundsChanged;
        }
        
        private void UnsubscribeFromWindowEvents(DetachedWindowViewModel window)
        {
            window.WindowClosed -= OnWindowClosed;
            window.TabCloseRequested -= OnTabCloseRequested;
            window.RedockRequested -= OnRedockRequested;
            window.BoundsChanged -= OnWindowBoundsChanged;
        }
        
        private async void OnWindowClosed(object sender, DetachedWindowViewModel window)
        {
            // Redock tabs back to main window when closing detached window (industry standard behavior)
            await CloseDetachedWindowAsync(window, redockTabs: true);
        }
        
        private async void OnTabCloseRequested(object sender, TabViewModel tab)
        {
            var window = sender as DetachedWindowViewModel;
            if (window == null) return;
            
            window.RemoveTab(tab);
            UpdateWindowTitle(window);
            
            // If window is now empty, close it
            if (!window.HasTabs)
            {
                await CloseDetachedWindowAsync(window);
            }
        }
        
        private async void OnRedockRequested(object sender, List<TabViewModel> tabs)
        {
            var window = sender as DetachedWindowViewModel;
            if (window == null) return;
            
            foreach (var tab in tabs)
            {
                await RedockTabAsync(tab, window);
            }
        }
        
        private void OnWindowBoundsChanged(object sender, (DetachedWindowViewModel Window, WindowBounds Bounds, bool IsMaximized) args)
        {
            // Update bounds for persistence (handled automatically by ViewModel)
            _logger.Debug($"[WindowManager] Window bounds changed: {args.Window.WindowId}");
        }
        
        #endregion
        
        #region Helper Methods
        
        private void UpdateWindowTitle(DetachedWindowViewModel window)
        {
            if (window == null) return;
            
            var tabCount = window.TabCount;
            if (tabCount == 0)
            {
                window.WindowTitle = "NoteNest - Detached";
            }
            else if (tabCount == 1)
            {
                var tab = window.PaneViewModel.Tabs?.FirstOrDefault();
                window.WindowTitle = $"NoteNest - {tab?.Title ?? "Untitled"}";
            }
            else
            {
                window.WindowTitle = $"NoteNest - {tabCount} tabs";
            }
        }
        
        #endregion
        
        #region Smart Positioning
        
        /// <summary>
        /// Apply smart positioning to a detached window based on preferred drop location
        /// </summary>
        private void ApplySmartPositioning(DetachedWindow window, Point screenDropPosition)
        {
            try
            {
                _logger?.Debug($"[WindowManager] Applying smart positioning at screen position: {screenDropPosition}");
                
                // Default window size for new detached windows
                const double defaultWidth = 800;
                const double defaultHeight = 600;
                
                // Calculate position relative to drop point
                // Position window so that it appears near the drop location but doesn't obscure it
                double windowLeft = screenDropPosition.X - 100; // Offset to left of cursor
                double windowTop = screenDropPosition.Y - 50;   // Offset above cursor
                
                // Get screen bounds to ensure window stays on screen
                var workingArea = SystemParameters.WorkArea;
                var screenBounds = new Rect(
                    SystemParameters.VirtualScreenLeft,
                    SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight);
                
                // Adjust position to keep window fully visible
                // Ensure right edge doesn't go off screen
                if (windowLeft + defaultWidth > screenBounds.Right)
                {
                    windowLeft = screenBounds.Right - defaultWidth - 20; // 20px margin
                }
                
                // Ensure left edge doesn't go off screen
                if (windowLeft < screenBounds.Left)
                {
                    windowLeft = screenBounds.Left + 20; // 20px margin
                }
                
                // Ensure bottom edge doesn't go off screen
                if (windowTop + defaultHeight > screenBounds.Bottom)
                {
                    windowTop = screenBounds.Bottom - defaultHeight - 20; // 20px margin
                }
                
                // Ensure top edge doesn't go off screen
                if (windowTop < screenBounds.Top)
                {
                    windowTop = screenBounds.Top + 20; // 20px margin
                }
                
                // Apply positioning using RestoreBounds method for consistency
                window.RestoreBounds(windowLeft, windowTop, defaultWidth, defaultHeight, false);
                
                _logger?.Debug($"[WindowManager] Smart positioning applied: ({windowLeft}, {windowTop}) {defaultWidth}x{defaultHeight}");
            }
            catch (Exception ex)
            {
                _logger?.Warning($"[WindowManager] Failed to apply smart positioning, using default: {ex.Message}");
                
                // Fallback to default positioning (slightly offset from main window)
                var mainWindow = System.Windows.Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    window.RestoreBounds(
                        mainWindow.Left + 50, 
                        mainWindow.Top + 50, 
                        800, 
                        600, 
                        false);
                }
            }
        }
        
        #endregion
        
        #region IDisposable
        
        private bool _disposed = false;
        
        public void Dispose()
        {
            if (_disposed) return;
            
            try
            {
                // Close all detached windows
                var windowsCopy = DetachedWindows.ToList();
                foreach (var window in windowsCopy)
                {
                    CloseDetachedWindowAsync(window, false).Wait(1000); // Quick cleanup
                }
                
                lock (_windowsLock)
                {
                    _detachedWindows.Clear();
                    _detachedWpfWindows.Clear();
                }
                
                _logger?.Debug("[WindowManager] Disposed");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[WindowManager] Error during disposal");
            }
            finally
            {
                _disposed = true;
            }
        }
        
        #endregion
    }
}
