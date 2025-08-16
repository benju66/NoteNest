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
        private IWorkspaceService? _workspaceService;
        private readonly Dictionary<string, SplitPaneView> _paneViews = new();
        private SplitContainer? _splitContainer;
        
        public SplitWorkspace()
        {
            InitializeComponent();
            
            // Register keyboard shortcuts
            CommandBindings.Add(new CommandBinding(
                new RoutedCommand("SplitHorizontal", typeof(SplitWorkspace), 
                    new InputGestureCollection { new KeyGesture(Key.OemPipe, ModifierKeys.Control | ModifierKeys.Shift) }),
                ExecuteSplitHorizontal));
            
            CommandBindings.Add(new CommandBinding(
                new RoutedCommand("SplitVertical", typeof(SplitWorkspace),
                    new InputGestureCollection { new KeyGesture(Key.OemBackslash, ModifierKeys.Control) }),
                ExecuteSplitVertical));

            CommandBindings.Add(new CommandBinding(
                new RoutedCommand("FocusNextPane", typeof(SplitWorkspace),
                    new InputGestureCollection { new KeyGesture(Key.F6) }),
                ExecuteFocusNextPane));

            CommandBindings.Add(new CommandBinding(
                new RoutedCommand("MoveFocusLeft", typeof(SplitWorkspace),
                    new InputGestureCollection { new KeyGesture(Key.Left, ModifierKeys.Control | ModifierKeys.Alt) }),
                (s, e) => MoveFocus(FocusDirection.Left)));

            CommandBindings.Add(new CommandBinding(
                new RoutedCommand("MoveFocusRight", typeof(SplitWorkspace),
                    new InputGestureCollection { new KeyGesture(Key.Right, ModifierKeys.Control | ModifierKeys.Alt) }),
                (s, e) => MoveFocus(FocusDirection.Right)));
        }
        
        public void Initialize(IWorkspaceService workspaceService)
        {
            _workspaceService = workspaceService;
            
            // Subscribe to pane collection changes
            if (_workspaceService.Panes is INotifyCollectionChanged ncc)
            {
                ncc.CollectionChanged += OnPanesCollectionChanged;
            }
            
            // Initialize with existing panes
            RebuildLayout();
        }
        
        private void OnPanesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildLayout();
        }
        
        private void RebuildLayout()
        {
            if (_workspaceService == null) return;
            
            WorkspaceGrid.Children.Clear();
            _paneViews.Clear();
            
            var panes = _workspaceService.Panes.ToList();
            
            if (panes.Count == 0)
            {
                // No panes - shouldn't happen
                return;
            }
            else if (panes.Count == 1)
            {
                // Single pane - no split
                var paneView = new SplitPaneView();
                paneView.BindToPane(panes[0]);
                _paneViews[panes[0].Id] = paneView;
                WorkspaceGrid.Children.Add(paneView);
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


