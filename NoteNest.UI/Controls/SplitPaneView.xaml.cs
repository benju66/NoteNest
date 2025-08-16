using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;

namespace NoteNest.UI.Controls
{
    public partial class SplitPaneView : UserControl
    {
        public SplitPane? Pane { get; private set; }
        
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(nameof(IsActive), typeof(bool),
                typeof(SplitPaneView), new PropertyMetadata(false, OnIsActiveChanged));
        
        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }
        
        public SplitPaneView()
        {
            InitializeComponent();
        }
        
        public void BindToPane(SplitPane pane)
        {
            Pane = pane;
            PaneTabControl.ItemsSource = pane.Tabs;
            IsActive = pane.IsActive;
        }
        
        private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SplitPaneView view)
            {
                view.UpdateVisualState();
            }
        }
        
        private void UpdateVisualState()
        {
            PaneBorder.BorderBrush = IsActive 
                ? new SolidColorBrush(Colors.DodgerBlue)
                : new SolidColorBrush(Colors.Transparent);
        }
        
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Pane != null && PaneTabControl.SelectedItem is ITabItem tab)
            {
                Pane.SelectedTab = tab;
            }
        }
        
        private void TabControl_GotFocus(object sender, RoutedEventArgs e)
        {
            // Notify workspace that this pane is now active
            if (Pane != null)
            {
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    // Best-effort lookup for a workspace service instance via DataContext
                    if (mainWindow.DataContext is IWorkspaceService workspace)
                    {
                        workspace.SetActivePane(Pane);
                    }
                }
            }
        }
        
        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ITabItem tab)
            {
                Pane?.Tabs.Remove(tab);
            }
        }
        
        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Mark tab as dirty
            if (PaneTabControl.SelectedItem is ITabItem tab)
            {
                tab.IsDirty = true;
            }
        }
    }
}


