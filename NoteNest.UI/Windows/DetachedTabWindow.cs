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
        private SplitPaneView _paneView;
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
            _pane = new SplitPane();
            _paneView = new SplitPaneView();
            _paneView.BindToPane(_pane);
            Content = _paneView;
            AllowDrop = true;
            // Register this pane so DnD services can discover it and it participates in service moves
            _workspaceService?.RegisterPane(_pane);
            // Auto-close this window when the last tab is removed via pane operations
            if (_pane?.Tabs != null)
            {
                _pane.Tabs.CollectionChanged -= Tabs_CollectionChanged;
                _pane.Tabs.CollectionChanged += Tabs_CollectionChanged;
            }
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
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            // Optional: prompt for unsaved changes if any
        }

        protected override void OnClosed(EventArgs e)
        {
            // Reset dialog owner back to main window
            if (_dialogService != null)
            {
                _dialogService.OwnerWindow = Application.Current?.MainWindow;
            }
            _workspaceService?.UnregisterPane(_pane);
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
    }
}


