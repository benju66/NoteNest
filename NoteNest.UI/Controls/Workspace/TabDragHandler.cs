using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using NoteNest.UI.ViewModels.Workspace;

namespace NoteNest.UI.Controls.Workspace
{
    /// <summary>
    /// Handles all drag & drop logic for tab reordering (same pane and cross-pane)
    /// Part of Milestone 2B: Drag & Drop - Phases 1 & 2
    /// </summary>
    public class TabDragHandler : IDisposable
    {
        private readonly TabControl _tabControl;
        private readonly PaneView _paneView;
        private Point _dragStartPosition;
        private bool _isDragging;
        private TabViewModel _draggedTab;
        private TabItem _draggedTabItem;
        private TabDragAdorner _dragAdorner;
        private InsertionIndicatorAdorner _insertionAdorner;
        private AdornerLayer _adornerLayer;
        
        // Phase 2: Cross-pane drag tracking
        private TabControl _currentTargetTabControl;
        private PaneView _currentTargetPaneView;
        
        public TabDragHandler(TabControl tabControl, PaneView paneView)
        {
            _tabControl = tabControl ?? throw new ArgumentNullException(nameof(tabControl));
            _paneView = paneView ?? throw new ArgumentNullException(nameof(paneView));
            
            // Wire up drag events
            _tabControl.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            _tabControl.PreviewMouseMove += OnPreviewMouseMove;
            _tabControl.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            _tabControl.PreviewKeyDown += OnPreviewKeyDown;
            _tabControl.DragOver += OnDragOver;
            _tabControl.Drop += OnDrop;
            _tabControl.AllowDrop = true;
            
            System.Diagnostics.Debug.WriteLine("[TabDragHandler] Initialized for PaneView");
        }
        
        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Capture drag start position
            _dragStartPosition = e.GetPosition(null);
            
            // Find if we clicked on a TabItem (not the close button)
            var clickedElement = e.OriginalSource as DependencyObject;
            
            // Don't start drag if clicking on close button
            if (clickedElement is FrameworkElement fe && fe.Name == "TabCloseButton")
                return;
            
            var tabItem = TabHitTestHelper.FindAncestor<TabItem>(clickedElement);
            if (tabItem != null && tabItem.DataContext is TabViewModel)
            {
                _draggedTabItem = tabItem;
                _draggedTab = tabItem.DataContext as TabViewModel;
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Potential drag on tab: {_draggedTab?.Title}");
            }
        }
        
        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedTab == null)
                return;
            
            if (_isDragging)
            {
                // Update adorner positions during drag
                // Use screen coordinates for cross-pane detection
                var screenPosition = _tabControl.PointToScreen(e.GetPosition(_tabControl));
                UpdateDragVisuals(screenPosition);
                return;
            }
            
            // Check if we've moved far enough to start drag
            var currentPosition = e.GetPosition(null);
            var diff = _dragStartPosition - currentPosition;
            
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                StartDrag();
            }
        }
        
        private void StartDrag()
        {
            if (_isDragging || _draggedTab == null || _draggedTabItem == null)
                return;
            
            _isDragging = true;
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Starting drag: {_draggedTab.Title}");
            
            // CRITICAL FIX: Get adorner layer from WINDOW, not TabControl
            // This allows adorners to draw over BOTH panes during cross-pane drag
            var mainWindow = Window.GetWindow(_paneView);
            if (mainWindow == null)
            {
                System.Diagnostics.Debug.WriteLine("[TabDragHandler] ERROR: No window found!");
                CancelDrag();
                return;
            }
            
            _adornerLayer = AdornerLayer.GetAdornerLayer(mainWindow.Content as UIElement);
            if (_adornerLayer == null)
            {
                System.Diagnostics.Debug.WriteLine("[TabDragHandler] ERROR: No adorner layer found!");
                CancelDrag();
                return;
            }
            
            // Create drag adorner (ghost image) - using window-level layer
            _dragAdorner = new TabDragAdorner(mainWindow.Content as UIElement, _draggedTabItem);
            _adornerLayer.Add(_dragAdorner);
            
            // Create insertion indicator - using window-level layer
            _insertionAdorner = new InsertionIndicatorAdorner(mainWindow.Content as UIElement);
            _adornerLayer.Add(_insertionAdorner);
            
            System.Diagnostics.Debug.WriteLine("[TabDragHandler] Adorners created on window-level layer for cross-pane support");
            
            // Dim the original tab
            _draggedTabItem.Opacity = 0.5;
            
            // Change cursor
            Mouse.OverrideCursor = Cursors.Hand;
            
            // Capture mouse to receive events even outside the control
            _tabControl.CaptureMouse();
        }
        
        private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                // Drag didn't start, clear tracked tab
                _draggedTab = null;
                _draggedTabItem = null;
                return;
            }
            
            // Complete the drop using screen coordinates
            var screenPosition = _tabControl.PointToScreen(e.GetPosition(_tabControl));
            CompleteDrag(screenPosition);
        }
        
        private void OnDragOver(object sender, DragEventArgs e)
        {
            // Placeholder for future cross-pane drag support
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
        
        private void OnDrop(object sender, DragEventArgs e)
        {
            // Placeholder for future cross-pane drag support
            e.Handled = true;
        }
        
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // ESC cancels drag
            if (e.Key == Key.Escape && _isDragging)
            {
                CancelDrag();
                e.Handled = true;
            }
        }
        
        private void UpdateDragVisuals(Point screenPosition)
        {
            if (!_isDragging)
                return;
            
            // Get window for coordinate conversions
            var mainWindow = Window.GetWindow(_paneView);
            if (mainWindow == null)
                return;
            
            // Convert screen position to window coordinates for adorner updates
            var windowPosition = mainWindow.PointFromScreen(screenPosition);
            
            // Update ghost image position (window coordinates)
            _dragAdorner?.UpdatePosition(windowPosition);
            
            // Phase 2: Detect which pane we're over using screen coordinates
            var targetPaneView = FindPaneViewAtPoint(screenPosition);
            var targetTabControl = targetPaneView?.TabControlElement;
            
            // Update tracked target (for cross-pane drops)
            _currentTargetTabControl = targetTabControl ?? _tabControl;
            _currentTargetPaneView = targetPaneView ?? _paneView;
            
            // Convert screen position to target TabControl's coordinates
            var targetTabControlPosition = _currentTargetTabControl.PointFromScreen(screenPosition);
            
            // Calculate insertion index in the target TabControl
            int insertionIndex = TabHitTestHelper.CalculateInsertionIndex(_currentTargetTabControl, targetTabControlPosition, _draggedTab);
            
            // Get insertion bounds in target TabControl coordinates
            var insertionBounds = TabHitTestHelper.GetInsertionPointBounds(_currentTargetTabControl, insertionIndex);
            
            // Convert insertion bounds to window coordinates for adorner
            var insertionBoundsInWindow = new Rect(
                _currentTargetTabControl.TranslatePoint(insertionBounds.TopLeft, mainWindow),
                _currentTargetTabControl.TranslatePoint(insertionBounds.BottomRight, mainWindow)
            );
            
            // Update insertion indicator with window coordinates
            _insertionAdorner?.UpdateInsertionPoint(insertionBoundsInWindow);
            
            var isCrossPaneDrag = _currentTargetPaneView != _paneView;
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Drag - Screen: ({screenPosition.X:F0}, {screenPosition.Y:F0}), Window: ({windowPosition.X:F0}, {windowPosition.Y:F0}), Insertion index: {insertionIndex}, Cross-pane: {isCrossPaneDrag}");
        }
        
        /// <summary>
        /// Find which PaneView the mouse is currently over (Phase 2: Cross-pane support)
        /// </summary>
        private PaneView FindPaneViewAtPoint(Point screenPosition)
        {
            try
            {
                // Get the workspace container
                var workspace = FindWorkspaceViewModel();
                if (workspace == null || workspace.Panes.Count <= 1)
                    return _paneView; // Only one pane, stay in current
                
                // Try to find PaneView under cursor using hit-testing
                var mainWindow = Window.GetWindow(_paneView);
                if (mainWindow == null)
                    return _paneView;
                
                // Convert to window coordinates
                var windowPoint = mainWindow.PointFromScreen(screenPosition);
                
                // Hit-test to find element under cursor
                var hitElement = mainWindow.InputHitTest(windowPoint) as DependencyObject;
                
                // Walk up visual tree to find PaneView
                return TabHitTestHelper.FindAncestor<PaneView>(hitElement) ?? _paneView;
            }
            catch
            {
                return _paneView; // Fallback to current pane on error
            }
        }
        
        private WorkspaceViewModel FindWorkspaceViewModel()
        {
            // Walk up the visual tree to find WorkspaceViewModel
            var current = _paneView as FrameworkElement;
            while (current != null)
            {
                if (current.DataContext is WorkspaceViewModel workspace)
                    return workspace;
                
                current = System.Windows.Media.VisualTreeHelper.GetParent(current) as FrameworkElement;
            }
            
            // Fallback: Check Window.DataContext
            var window = Window.GetWindow(_paneView);
            if (window?.DataContext is NoteNest.UI.ViewModels.Shell.MainShellViewModel shell)
            {
                return shell.Workspace;
            }
            
            return null;
        }
        
        private void CompleteDrag(Point screenDropPosition)
        {
            if (!_isDragging)
                return;
            
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Completing drag at screen position: {screenDropPosition}");
            
            try
            {
                var sourcePaneVm = _paneView.DataContext as PaneViewModel;
                var targetPaneVm = _currentTargetPaneView?.DataContext as PaneViewModel;
                
                if (sourcePaneVm == null || targetPaneVm == null || _draggedTab == null)
                {
                    System.Diagnostics.Debug.WriteLine("[TabDragHandler] Missing ViewModels, aborting drop");
                    return;
                }
                
                // Convert screen position to target TabControl's coordinates
                var targetTabControlPosition = _currentTargetTabControl.PointFromScreen(screenDropPosition);
                
                // Calculate insertion index in target pane
                int insertionIndex = TabHitTestHelper.CalculateInsertionIndex(_currentTargetTabControl, targetTabControlPosition, _draggedTab);
                
                // Phase 2: Cross-pane drag
                if (sourcePaneVm != targetPaneVm)
                {
                    System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Cross-pane drag detected - moving tab '{_draggedTab.Title}' to other pane at index {insertionIndex}");
                    
                    var workspace = FindWorkspaceViewModel();
                    if (workspace != null)
                    {
                        workspace.MoveTabBetweenPanes(_draggedTab, sourcePaneVm, targetPaneVm, insertionIndex);
                        System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Cross-pane move completed");
                    }
                }
                // Phase 1: Same-pane reorder
                else
                {
                    int currentIndex = sourcePaneVm.Tabs.IndexOf(_draggedTab);
                    
                    System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Same-pane reorder: Current index: {currentIndex}, Target index: {insertionIndex}");
                    
                    // Only move if position changed
                    if (currentIndex != -1 && currentIndex != insertionIndex && insertionIndex >= 0)
                    {
                        // Remove from current position
                        sourcePaneVm.Tabs.RemoveAt(currentIndex);
                        
                        // Adjust insertion index if needed
                        if (insertionIndex > currentIndex)
                            insertionIndex--;
                        
                        // Insert at new position
                        sourcePaneVm.Tabs.Insert(Math.Min(insertionIndex, sourcePaneVm.Tabs.Count), _draggedTab);
                        
                        // Keep it selected
                        sourcePaneVm.SelectedTab = _draggedTab;
                        
                        System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Tab moved from {currentIndex} to {insertionIndex}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] ERROR during drop: {ex.Message}");
            }
            finally
            {
                CleanupDrag();
            }
        }
        
        private void CancelDrag()
        {
            System.Diagnostics.Debug.WriteLine("[TabDragHandler] Drag cancelled");
            CleanupDrag();
        }
        
        private void CleanupDrag()
        {
            // Remove adorners
            if (_adornerLayer != null)
            {
                if (_dragAdorner != null)
                {
                    _adornerLayer.Remove(_dragAdorner);
                    _dragAdorner = null;
                }
                
                if (_insertionAdorner != null)
                {
                    _adornerLayer.Remove(_insertionAdorner);
                    _insertionAdorner = null;
                }
            }
            
            // Restore original tab opacity
            if (_draggedTabItem != null)
            {
                _draggedTabItem.Opacity = 1.0;
                _draggedTabItem = null;
            }
            
            // Restore cursor
            Mouse.OverrideCursor = null;
            
            // Release mouse capture
            if (_tabControl.IsMouseCaptured)
                _tabControl.ReleaseMouseCapture();
            
            // Clear state
            _isDragging = false;
            _draggedTab = null;
            
            System.Diagnostics.Debug.WriteLine("[TabDragHandler] Drag cleanup complete");
        }
        
        public void Dispose()
        {
            // Unhook events
            _tabControl.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            _tabControl.PreviewMouseMove -= OnPreviewMouseMove;
            _tabControl.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
            _tabControl.PreviewKeyDown -= OnPreviewKeyDown;
            _tabControl.DragOver -= OnDragOver;
            _tabControl.Drop -= OnDrop;
            
            CleanupDrag();
            
            System.Diagnostics.Debug.WriteLine("[TabDragHandler] Disposed");
        }
    }
}

