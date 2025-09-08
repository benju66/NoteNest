using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services;
using NoteNest.UI.Services;
using NoteNest.UI.Controls;
using NoteNest.Core.Models;

namespace NoteNest.UI.Windows
{
    public class DetachedTabWindow : Window
    {
        private readonly IWorkspaceService _workspaceService;
        private readonly IWorkspaceStateService _stateService;
        private readonly IDialogService _dialogService;
        private SplitPane _pane;
        private SplitWorkspace _workspaceView;
        private IServiceProvider _services;
        private bool _closingRequested;

        public int TabCount => _pane?.Tabs?.Count ?? 0;

        public DetachedTabWindow(ITabItem initialTab, Point screenPosition, IServiceProvider services)
        {
            _services = services;
            _workspaceService = services.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
            _stateService = services.GetService(typeof(IWorkspaceStateService)) as IWorkspaceStateService;
            _dialogService = services.GetService(typeof(IDialogService)) as IDialogService;

            if (_dialogService != null)
                _dialogService.OwnerWindow = this;

            Title = $"{initialTab.Title} - NoteNest";
            Width = 900;
            Height = 650;
            Left = screenPosition.X - Width / 2;
            Top = screenPosition.Y - 50;

            InitializeContent();
            AddTab(initialTab);
        }

        private void InitializeContent()
        {
            // Add ModernWpf theme resources to the window
            this.Resources.MergedDictionaries.Add(new ModernWpf.ThemeResources());
            this.Resources.MergedDictionaries.Add(new ModernWpf.Controls.XamlControlsResources());
            
            // Apply current theme based on main application theme
            try 
            {
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    var currentTheme = ModernWpf.ThemeManager.GetRequestedTheme(mainWindow);
                    ModernWpf.ThemeManager.SetRequestedTheme(this, currentTheme);
                }
                else
                {
                    // Fallback to default theme
                    ModernWpf.ThemeManager.SetRequestedTheme(this, ModernWpf.ElementTheme.Default);
                }
            }
            catch { }
            
            _pane = new SplitPane { OwnerKey = $"detached:{GetHashCode()}" };
            _workspaceView = new SplitWorkspace();
            // Initialize with existing workspace service; filter panes by OwnerKey within the view
            _workspaceView.OwnerKeyFilter = $"detached:{GetHashCode()}";
            _workspaceView.Initialize(_workspaceService);
            Content = _workspaceView;
            AllowDrop = true;
            // Register this pane so DnD services can discover it and it participates in service moves
            _workspaceService?.RegisterPane(_pane);
            // Auto-close this window when the last tab is removed via pane operations
            if (_pane?.Tabs != null)
            {
                _pane.Tabs.CollectionChanged -= Tabs_CollectionChanged;
                _pane.Tabs.CollectionChanged += Tabs_CollectionChanged;
            }
            // Do not add detached pane to main panes; it lives only in DetachedPanes
        }

        public void AddTab(ITabItem tab)
        {
            if (!_pane.Tabs.Contains(tab))
            {
                _pane.Tabs.Add(tab);
            }
            _pane.SelectedTab = tab;
            if (tab.Note != null && _stateService != null)
            {
                _stateService.AssociateNoteWithWindow(tab.Note.Id, $"detached:{GetHashCode()}", true);
            }
            try { Title = $"{tab.Title} - NoteNest"; } catch { }
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            
            // Save all dirty tabs before closing
            if (_pane?.Tabs != null && _pane.Tabs.Any())
            {
                try
                {
                    // Get state service to save
                    var stateService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IWorkspaceStateService)) as NoteNest.Core.Services.IWorkspaceStateService;
                    if (stateService != null)
                    {
                        // First, force flush all editors in this window
                        if (Content is SplitPaneView paneView)
                        {
                            paneView.FlushAllEditors();
                        }
                        
                        // Then save any dirty notes
                        var dirtyTabs = _pane.Tabs.Where(t => t.IsDirty).ToList();
                        if (dirtyTabs.Any())
                        {
                            e.Cancel = true; // Prevent close while saving
                            
                            try
                            {
                                foreach (var tab in dirtyTabs)
                                {
                                    stateService.UpdateNoteContent(tab.Note.Id, tab.Content ?? string.Empty);
                                }
                                await stateService.SaveAllDirtyNotesAsync();
                            }
                            finally
                            {
                                // Re-trigger close after save completes
                                e.Cancel = false;
                                Close();
                            }
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DetachedWindow] Error saving on close: {ex.Message}");
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Reset dialog owner back to main window
            if (_dialogService != null)
            {
                _dialogService.OwnerWindow = Application.Current?.MainWindow;
            }
            _workspaceService?.UnregisterPane(_pane);
            // no event unhook needed for SplitWorkspace
            if (_pane?.Tabs != null)
            {
                _pane.Tabs.CollectionChanged -= Tabs_CollectionChanged;
            }
            base.OnClosed(e);
        }

        public void RemoveTab(ITabItem tab)
        {
            if (_pane.Tabs.Contains(tab))
            {
                _pane.Tabs.Remove(tab);
                if (_pane.Tabs.Count == 0)
                {
                    _workspaceService?.UnregisterPane(_pane);
                    Close();
                }
            }
        }

        private void Tabs_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_closingRequested) return;
            if (_pane?.Tabs?.Count == 0)
            {
                _closingRequested = true;
                try
                {
                    _workspaceService?.UnregisterPane(_pane);
                    Close();
                }
                finally { _closingRequested = false; }
            }
        }

        private void PaneView_SelectedTabChanged(object? sender, ITabItem e)
        {
            try
            {
                // Update window title to reflect current selected tab
                if (e?.Title != null)
                {
                    Title = $"{e.Title} - NoteNest";
                }
            }
            catch { }
        }
    }
}


