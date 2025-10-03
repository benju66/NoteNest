using System.Windows;
using System.Windows.Controls;
using NoteNest.UI.ViewModels.Workspace;

namespace NoteNest.UI.Controls.Workspace
{
    /// <summary>
    /// Individual pane view that displays tabs
    /// Part of Milestone 2A: Split View
    /// </summary>
    public partial class PaneView : UserControl
    {
        public PaneView()
        {
            InitializeComponent();
            System.Diagnostics.Debug.WriteLine("[PaneView] Initialized");
        }
        
        /// <summary>
        /// Activate pane when user clicks anywhere in it
        /// </summary>
        private void Border_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ActivateThisPane();
        }
        
        private void TabControl_GotFocus(object sender, RoutedEventArgs e)
        {
            ActivateThisPane();
        }
        
        private void ActivateThisPane()
        {
            if (DataContext is PaneViewModel paneVm)
            {
                var workspace = FindWorkspaceViewModel();
                if (workspace != null && workspace.ActivePane != paneVm)
                {
                    workspace.ActivePane = paneVm;
                    System.Diagnostics.Debug.WriteLine($"[PaneView] Pane activated: {paneVm.Id}");
                }
            }
        }
        
        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is TabViewModel tab)
            {
                var workspace = FindWorkspaceViewModel();
                if (workspace?.CloseTabCommand?.CanExecute(tab) == true)
                {
                    workspace.CloseTabCommand.Execute(tab);
                }
            }
        }
        
        private WorkspaceViewModel FindWorkspaceViewModel()
        {
            // Walk up the visual tree to find WorkspaceViewModel
            var current = this as FrameworkElement;
            while (current != null)
            {
                if (current.DataContext is WorkspaceViewModel workspace)
                    return workspace;
                
                current = System.Windows.Media.VisualTreeHelper.GetParent(current) as FrameworkElement;
            }
            
            // Fallback: Check Window.DataContext
            var window = Window.GetWindow(this);
            if (window?.DataContext is NoteNest.UI.ViewModels.Shell.MainShellViewModel shell)
            {
                return shell.Workspace;
            }
            
            return null;
        }
    }
}

