using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NoteNest.Core.Models;
using NoteNest.Core.Interfaces.Services;
using NoteNest.UI.Controls;

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
            // Unbind from previous pane if any
            if (Pane != null && Pane.Tabs is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= OnTabsCollectionChanged;
            }
            
            Pane = pane;
            PaneTabControl.ItemsSource = pane.Tabs;
            
            // Subscribe to collection changes
            if (pane.Tabs is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += OnTabsCollectionChanged;
            }
            
            // Set initial selection
            if (pane.SelectedTab != null)
            {
                PaneTabControl.SelectedItem = pane.SelectedTab;
            }
            else if (pane.Tabs.Count > 0)
            {
                PaneTabControl.SelectedItem = pane.Tabs[0];
                pane.SelectedTab = pane.Tabs[0];
            }
            
            IsActive = pane.IsActive;
            UpdateEmptyState();
        }

        private void OnTabsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateEmptyState();
            
            // Auto-select first tab if none selected
            if (Pane != null && Pane.SelectedTab == null && Pane.Tabs.Count > 0)
            {
                Pane.SelectedTab = Pane.Tabs[0];
                PaneTabControl.SelectedItem = Pane.Tabs[0];
            }
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
                : new SolidColorBrush(Colors.LightGray);
        }
        
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Pane != null && PaneTabControl.SelectedItem is ITabItem tab)
            {
                Pane.SelectedTab = tab;
                
                // Load content into the editor if it's a new selection
                if (e.AddedItems.Count > 0)
                {
                    LoadTabContent(tab);
                }
            }
        }
        
        private void TabControl_GotFocus(object sender, RoutedEventArgs e)
        {
            // Notify workspace that this pane is now active
            if (Pane != null)
            {
                var mainWindow = Application.Current.MainWindow;
                var panel = FindNoteNestPanel(mainWindow);
                if (panel != null)
                {
                    var splitWorkspace = panel.FindName("SplitWorkspace") as SplitWorkspace;
                    splitWorkspace?.SetActivePane(Pane);
                }
            }
        }

        private NoteNestPanel? FindNoteNestPanel(DependencyObject? obj)
        {
            if (obj == null) return null;
            if (obj is NoteNestPanel panel) return panel;
            
            int childCount = VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                var result = FindNoteNestPanel(child);
                if (result != null) return result;
            }
            return null;
        }
        
        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ITabItem tab)
            {
                Pane?.Tabs.Remove(tab);
                
                // If this was the last tab, close the pane
                if (Pane?.Tabs.Count == 0)
                {
                    var workspaceService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
                    if (workspaceService != null && workspaceService.Panes.Count > 1)
                    {
                        _ = workspaceService.ClosePaneAsync(Pane);
                    }
                }
            }
        }
        
        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Mark tab as dirty
            if (PaneTabControl.SelectedItem is ITabItem tab)
            {
                tab.IsDirty = true;
                if (sender is SmartTextEditor ste)
                {
                    tab.Content = ste.Text;
                }
            }
        }

        private void Editor_Loaded(object sender, RoutedEventArgs e)
        {
            // Load content when editor is loaded
            if (sender is SmartTextEditor ste && PaneTabControl.SelectedItem is ITabItem tab)
            {
                ste.Text = tab.Content ?? string.Empty;
            }
        }

        private void LoadTabContent(ITabItem tab)
        {
            var container = PaneTabControl.ItemContainerGenerator.ContainerFromItem(tab) as TabItem;
            if (container == null) return;
            var contentPresenter = FindVisualChild<ContentPresenter>(container);
            if (contentPresenter == null) return;
            var ste = FindVisualChild<SmartTextEditor>(contentPresenter);
            if (ste == null) return;
            ste.Text = tab.Content ?? string.Empty;
        }

        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;
                    
                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void UpdateEmptyState()
        {
            if (Pane != null && Pane.Tabs.Count == 0)
            {
                EmptyMessage.Visibility = Visibility.Visible;
                PaneTabControl.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyMessage.Visibility = Visibility.Collapsed;
                PaneTabControl.Visibility = Visibility.Visible;
            }
        }

        // Per-tab toolbar handlers
        private void Toolbar_BulletList_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            ste?.InsertBulletList();
        }
        private void Toolbar_NumberedList_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            ste?.InsertNumberedList();
        }
        private void Toolbar_TaskList_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            ste?.InsertTaskList();
        }
        private void Toolbar_Indent_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            ste?.IndentSelection();
        }
        private void Toolbar_Outdent_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            ste?.OutdentSelection();
        }
        private void Toolbar_Bold_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            if (ste == null || string.IsNullOrEmpty(ste.SelectedText)) return;
            var start = ste.SelectionStart;
            ste.SelectedText = $"**{ste.SelectedText}**";
            ste.CaretIndex = start + ste.SelectedText.Length;
        }
        private void Toolbar_Italic_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            if (ste == null || string.IsNullOrEmpty(ste.SelectedText)) return;
            var start = ste.SelectionStart;
            ste.SelectedText = $"*{ste.SelectedText}*";
            ste.CaretIndex = start + ste.SelectedText.Length;
        }
        private void Toolbar_Underline_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            if (ste == null || string.IsNullOrEmpty(ste.SelectedText)) return;
            var start = ste.SelectionStart;
            ste.SelectedText = $"__{ste.SelectedText}__";
            ste.CaretIndex = start + ste.SelectedText.Length;
        }
    }
}


