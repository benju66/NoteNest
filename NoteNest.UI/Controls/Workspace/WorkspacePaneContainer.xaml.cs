using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NoteNest.UI.ViewModels.Workspace;

namespace NoteNest.UI.Controls.Workspace
{
    /// <summary>
    /// Container that dynamically shows 1 or 2 panes with GridSplitter
    /// Part of Milestone 2A: Split View
    /// Uses proven pattern from SplitWorkspace.xaml.cs
    /// </summary>
    public partial class WorkspacePaneContainer : UserControl
    {
        private GridSplitter _splitter;
        
        public WorkspacePaneContainer()
        {
            InitializeComponent();
            
            DataContextChanged += OnDataContextChanged;
            
            System.Diagnostics.Debug.WriteLine("[WorkspacePaneContainer] Initialized");
        }
        
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from old ViewModel
            if (e.OldValue is WorkspaceViewModel oldWorkspace)
            {
                oldWorkspace.Panes.CollectionChanged -= OnPanesCollectionChanged;
            }
            
            // Subscribe to new ViewModel
            if (e.NewValue is WorkspaceViewModel newWorkspace)
            {
                newWorkspace.Panes.CollectionChanged += OnPanesCollectionChanged;
                RebuildLayout(newWorkspace);
            }
        }
        
        private void OnPanesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (DataContext is WorkspaceViewModel workspace)
            {
                RebuildLayout(workspace);
            }
        }
        
        private void RebuildLayout(WorkspaceViewModel workspace)
        {
            if (workspace == null)
            {
                System.Diagnostics.Debug.WriteLine("[WorkspacePaneContainer] RebuildLayout: workspace is null");
                return;
            }
            
            ContainerGrid.Children.Clear();
            ContainerGrid.RowDefinitions.Clear();
            ContainerGrid.ColumnDefinitions.Clear();
            
            var panes = workspace.Panes.ToList();
            System.Diagnostics.Debug.WriteLine($"[WorkspacePaneContainer] RebuildLayout: {panes.Count} pane(s)");
            
            if (panes.Count == 0)
            {
                // No panes - show placeholder
                var placeholder = new TextBlock
                {
                    Text = "No workspace panes available",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic
                };
                ContainerGrid.Children.Add(placeholder);
                return;
            }
            else if (panes.Count == 1)
            {
                // Single pane - no split
                var paneView = new PaneView { DataContext = panes[0] };
                ContainerGrid.Children.Add(paneView);
                System.Diagnostics.Debug.WriteLine($"[WorkspacePaneContainer] Single pane layout");
            }
            else if (panes.Count >= 2)
            {
                // Two panes side-by-side with GridSplitter (PROVEN PATTERN)
                // Column 0: First pane (50%)
                // Column 1: GridSplitter (4px)
                // Column 2: Second pane (50%)
                
                ContainerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                ContainerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) }); // Splitter
                ContainerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                
                // Create first pane
                var paneView1 = new PaneView { DataContext = panes[0] };
                Grid.SetColumn(paneView1, 0);
                ContainerGrid.Children.Add(paneView1);
                
                // Create GridSplitter (PROVEN CONFIGURATION)
                _splitter = new GridSplitter
                {
                    Width = 4,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = (Brush)TryFindResource("SystemControlBackgroundBaseMediumBrush") ?? Brushes.LightGray,
                    ShowsPreview = false
                };
                Grid.SetColumn(_splitter, 1);
                ContainerGrid.Children.Add(_splitter);
                
                // Create second pane
                var paneView2 = new PaneView { DataContext = panes[1] };
                Grid.SetColumn(paneView2, 2);
                ContainerGrid.Children.Add(paneView2);
                
                System.Diagnostics.Debug.WriteLine($"[WorkspacePaneContainer] Split layout: 2 panes");
            }
        }
        
        private object TryFindResource(string resourceKey)
        {
            try
            {
                return System.Windows.Application.Current?.TryFindResource(resourceKey);
            }
            catch
            {
                return null;
            }
        }
    }
}

