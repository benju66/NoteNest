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

namespace NoteNest.UI.Windows
{
    public class DetachedTabWindow : Window
    {
        private readonly IWorkspaceService _workspaceService;
        private readonly IWorkspaceStateService _stateService;
        private readonly IDialogService _dialogService;
        private DraggableTabControl _tabControl;

        public int TabCount => _tabControl?.Items.Count ?? 0;

        public DetachedTabWindow(ITabItem initialTab, Point screenPosition, IServiceProvider services)
        {
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
            _tabControl = new DraggableTabControl();
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(_tabControl, 1);
            grid.Children.Add(_tabControl);
            Content = grid;
            AllowDrop = true;
        }

        public void AddTab(ITabItem tab)
        {
            var tabItem = new TabItem
            {
                Header = tab.Title,
                DataContext = tab
            };
            _tabControl.Items.Add(tabItem);
            _tabControl.SelectedItem = tabItem;

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
            base.OnClosed(e);
        }

        public void RemoveTab(ITabItem tab)
        {
            var tabItem = _tabControl.Items.Cast<TabItem>().FirstOrDefault(ti => ti.DataContext == tab);
            if (tabItem != null)
            {
                _tabControl.Items.Remove(tabItem);
                if (_tabControl.Items.Count == 0)
                {
                    Close();
                }
            }
        }
    }
}


