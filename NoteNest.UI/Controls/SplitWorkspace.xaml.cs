using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Interfaces.Split;
using NoteNest.Core.Models;

namespace NoteNest.UI.Controls
{
    public partial class SplitWorkspace : UserControl
    {
        public static readonly RoutedCommand SplitHorizontalCommand = new();
        public static readonly RoutedCommand SplitVerticalCommand = new();
        public static readonly RoutedCommand FocusNextPaneCommand = new();
        public static readonly RoutedCommand MoveFocusLeftCommand = new();
        public static readonly RoutedCommand MoveFocusRightCommand = new();
        private IWorkspaceService? _workspaceService;
        private readonly Dictionary<string, SplitPaneView> _paneViews = new();
        private SplitContainer? _splitContainer;
        
        public SplitWorkspace()
        {
            InitializeComponent();
            
            // Command handlers
            CommandBindings.Add(new CommandBinding(SplitHorizontalCommand, ExecuteSplitHorizontal));
            CommandBindings.Add(new CommandBinding(SplitVerticalCommand, ExecuteSplitVertical));
            CommandBindings.Add(new CommandBinding(FocusNextPaneCommand, ExecuteFocusNextPane));
            CommandBindings.Add(new CommandBinding(MoveFocusLeftCommand, (s, e) => MoveFocus(FocusDirection.Left)));
            CommandBindings.Add(new CommandBinding(MoveFocusRightCommand, (s, e) => MoveFocus(FocusDirection.Right)));

            // Key bindings
            InputBindings.Add(new KeyBinding(SplitHorizontalCommand, new KeyGesture(Key.OemPipe, ModifierKeys.Control | ModifierKeys.Shift)));
            InputBindings.Add(new KeyBinding(SplitVerticalCommand, new KeyGesture(Key.OemBackslash, ModifierKeys.Control)));
            InputBindings.Add(new KeyBinding(FocusNextPaneCommand, new KeyGesture(Key.F6)));
            InputBindings.Add(new KeyBinding(MoveFocusLeftCommand, new KeyGesture(Key.Left, ModifierKeys.Control | ModifierKeys.Alt)));
            InputBindings.Add(new KeyBinding(MoveFocusRightCommand, new KeyGesture(Key.Right, ModifierKeys.Control | ModifierKeys.Alt)));
        }
        
        public void Initialize(IWorkspaceService workspaceService)
        {
            _workspaceService = workspaceService;
            
            // Log initialization
            System.Diagnostics.Debug.WriteLine($"SplitWorkspace Initialize: Found {_workspaceService.Panes.Count} panes");

            // Ensure at least one pane exists
            if (_workspaceService.Panes.Count == 0)
            {
                var initialPane = new SplitPane();
                _workspaceService.Panes.Add(initialPane);
                _workspaceService.ActivePane = initialPane;
                System.Diagnostics.Debug.WriteLine("Created initial pane");
            }

            // Subscribe to pane collection changes
            if (_workspaceService.Panes is INotifyCollectionChanged ncc)
            {
                ncc.CollectionChanged += OnPanesCollectionChanged;
            }
            
            // Initialize with existing panes
            RebuildLayout();

            // Log the active pane status
            if (_workspaceService.ActivePane != null)
            {
                System.Diagnostics.Debug.WriteLine($"Active pane: {_workspaceService.ActivePane.Id}, Tabs: {_workspaceService.ActivePane.Tabs.Count}");
            }
        }
        
        private void OnPanesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildLayout();
        }
        
        private void RebuildLayout()
        {
            if (_workspaceService == null) 
            {
                System.Diagnostics.Debug.WriteLine("RebuildLayout: WorkspaceService is null");
                return;
            }
            
            WorkspaceGrid.Children.Clear();
            _paneViews.Clear();
            
            var panes = _workspaceService.Panes.ToList();
            System.Diagnostics.Debug.WriteLine($"RebuildLayout: Rebuilding with {panes.Count} panes");
            
            if (panes.Count == 0)
            {
                // No panes - shouldn't happen
                System.Diagnostics.Debug.WriteLine("RebuildLayout: No panes found!");
                return;
            }
            else if (panes.Count == 1)
            {
                // Single pane - no split
                var paneView = new SplitPaneView();
                paneView.BindToPane(panes[0]);
                _paneViews[panes[0].Id] = paneView;
                WorkspaceGrid.Children.Add(paneView);
                System.Diagnostics.Debug.WriteLine($"RebuildLayout: Single pane with {panes[0].Tabs.Count} tabs");
            }
            else if (panes.Count == 2)
            {
                // Two panes - single split
                _splitContainer = new SplitContainer { Orientation = SplitOrientation.Vertical };
                
                var paneView1 = new SplitPaneView();
                paneView1.BindToPane(panes[0]);
                _paneViews[panes[0].Id] = paneView1;
                
                var paneView2 = new SplitPaneView();
                paneView2.BindToPane(panes[1]);
                _paneViews[panes[1].Id] = paneView2;
                
                _splitContainer.SetFirstPane(paneView1);
                _splitContainer.SetSecondPane(paneView2);
                
                WorkspaceGrid.Children.Add(_splitContainer);
                System.Diagnostics.Debug.WriteLine($"RebuildLayout: Two panes - Pane1: {panes[0].Tabs.Count} tabs, Pane2: {panes[1].Tabs.Count} tabs");
            }
            // For MVP, limit to 2 panes. More complex layouts in Phase 2
        }
        
        public void SetActivePane(SplitPane pane)
        {
            _workspaceService?.SetActivePane(pane);
            UpdatePaneVisuals();
        }
        
        private void UpdatePaneVisuals()
        {
            foreach (var kvp in _paneViews)
            {
                var pane = _workspaceService?.Panes.FirstOrDefault(p => p.Id == kvp.Key);
                if (pane != null)
                {
                    kvp.Value.IsActive = pane.IsActive;
                }
            }
        }
        
        private async void ExecuteSplitHorizontal(object sender, ExecutedRoutedEventArgs e)
        {
            if (_workspaceService?.ActivePane != null && _workspaceService.Panes.Count < 2)
            {
                await _workspaceService.SplitPaneAsync(_workspaceService.ActivePane, SplitOrientation.Horizontal);
            }
        }
        
        private async void ExecuteSplitVertical(object sender, ExecutedRoutedEventArgs e)
        {
            if (_workspaceService?.ActivePane != null && _workspaceService.Panes.Count < 2)
            {
                await _workspaceService.SplitPaneAsync(_workspaceService.ActivePane, SplitOrientation.Vertical);
            }
        }

        private void ExecuteFocusNextPane(object sender, ExecutedRoutedEventArgs e)
        {
            if (_workspaceService == null) return;
            
            var panes = _workspaceService.Panes.ToList();
            if (panes.Count <= 1) return;
            
            var currentIndex = panes.IndexOf(_workspaceService.ActivePane);
            var nextIndex = (currentIndex + 1) % panes.Count;
            
            _workspaceService.ActivePane = panes[nextIndex];
            UpdatePaneVisuals();
            
            // Set keyboard focus
            if (_paneViews.TryGetValue(panes[nextIndex].Id, out var paneView))
            {
                paneView.Focus();
            }
        }
        
        private void MoveFocus(FocusDirection direction)
        {
            // For MVP, simple left/right between two panes
            if (_workspaceService?.Panes.Count == 2)
            {
                var panes = _workspaceService.Panes.ToList();
                var targetPane = _workspaceService.ActivePane == panes[0] ? panes[1] : panes[0];
                
                _workspaceService.ActivePane = targetPane;
                UpdatePaneVisuals();
                
                if (_paneViews.TryGetValue(targetPane.Id, out var paneView))
                {
                    paneView.Focus();
                }
            }
        }
        
        private enum FocusDirection
        {
            Left, Right, Up, Down
        }
    }
}


