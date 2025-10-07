using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.UI.ViewModels.Workspace;
using NoteNest.UI.ViewModels.Windows;
using NoteNest.UI.Windows;
using NoteNest.UI.Services;

namespace NoteNest.UI.Controls.Workspace
{
    /// <summary>
    /// Handles all drag & drop logic for tab reordering (same pane, cross-pane, and cross-window)
    /// Enhanced with tear-out functionality - Phase 3: Cross-window drag operations
    /// </summary>
    public class TabDragHandler : IDisposable
    {
        private readonly TabControl _tabControl;
        private readonly PaneView _paneView;
        private readonly IWindowManager _windowManager; // Injected for cross-window operations
        
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
        
        // Phase 3: Cross-window drag tracking
        private DetachedWindow _currentTargetWindow;
        private DetachedWindowViewModel _currentTargetWindowViewModel;
        private Window _sourceWindow; // Track which window the drag started from
        private bool _isOutsideMainWindow;
        private bool _hasExceededDetachThreshold;
        
        // Tear-out thresholds
        private const double DETACH_THRESHOLD_DISTANCE = 75; // pixels outside window to trigger detach
        private const double GHOST_PREVIEW_THRESHOLD = 50; // pixels to start showing ghost preview
        
        public TabDragHandler(TabControl tabControl, PaneView paneView, IWindowManager windowManager = null)
        {
            _tabControl = tabControl ?? throw new ArgumentNullException(nameof(tabControl));
            _paneView = paneView ?? throw new ArgumentNullException(nameof(paneView));
            _windowManager = windowManager; // Can be null for detached windows (they don't create new windows)
            
            // Wire up drag events
            _tabControl.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            _tabControl.PreviewMouseMove += OnPreviewMouseMove;
            _tabControl.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            _tabControl.PreviewKeyDown += OnPreviewKeyDown;
            _tabControl.DragOver += OnDragOver;
            _tabControl.Drop += OnDrop;
            _tabControl.AllowDrop = true;
            
            System.Diagnostics.Debug.WriteLine("[TabDragHandler] Initialized for PaneView");
            
            // Also log to file for debugging
            try 
            {
                var app = System.Windows.Application.Current as NoteNest.UI.App;
                var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                logger?.Debug("[TabDragHandler] Initialized for PaneView with drag & drop support");
            }
            catch { /* Ignore logging errors */ }
        }
        
        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[TabDragHandler] Mouse down event received");
            
            // Capture drag start position
            _dragStartPosition = e.GetPosition(null);
            
            // Find if we clicked on a TabItem (not the close button)
            var clickedElement = e.OriginalSource as DependencyObject;
            
            // Don't start drag if clicking on close button
            if (clickedElement is FrameworkElement fe && fe.Name == "TabCloseButton")
                return;
            
            var tabItem = TabHitTestHelper.FindAncestor<TabItem>(clickedElement);
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Hit test result: TabItem={tabItem != null}, ClickedElement={clickedElement?.GetType().Name}");
            
            if (tabItem != null && tabItem.DataContext is TabViewModel)
            {
                _draggedTabItem = tabItem;
                _draggedTab = tabItem.DataContext as TabViewModel;
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] ✅ Tab captured for potential drag: {_draggedTab?.Title}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[TabDragHandler] ❌ No valid tab captured");
                _draggedTab = null;
                _draggedTabItem = null;
            }
        }
        
        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                System.Diagnostics.Debug.WriteLine("[TabDragHandler] Mouse move: Left button not pressed");
                return;
            }
            
            if (_draggedTab == null)
            {
                System.Diagnostics.Debug.WriteLine("[TabDragHandler] Mouse move: No dragged tab captured");
                return;
            }
            
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
            var distance = Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y);
            
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Mouse move: Distance={distance:F1}, Threshold={SystemParameters.MinimumHorizontalDragDistance}");
            
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Drag threshold exceeded, starting drag for: {_draggedTab?.Title}");
                StartDrag();
            }
        }
        
        private void StartDrag()
        {
            if (_isDragging || _draggedTab == null || _draggedTabItem == null)
                return;
            
            _isDragging = true;
            
            // Track the source window (could be main or detached)
            _sourceWindow = Window.GetWindow(_paneView);
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Starting drag: {_draggedTab.Title} from window: {_sourceWindow?.GetType().Name}");
            
            // Log to file for debugging
            try 
            {
                var app = System.Windows.Application.Current as NoteNest.UI.App;
                var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                logger?.Debug($"[TabDragHandler] DRAG STARTED: {_draggedTab.Title}");
            }
            catch { /* Ignore logging errors */ }
            
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
            System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Mouse captured by TabControl in window: {Window.GetWindow(_tabControl)?.GetType().Name}");
        }
        
        private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Mouse up event: IsDragging={_isDragging}, HasTab={_draggedTab != null}");
            
            // Log to file for debugging
            try 
            {
                var app = System.Windows.Application.Current as NoteNest.UI.App;
                var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                logger?.Debug($"[TabDragHandler] MOUSE UP: IsDragging={_isDragging}, Tab={_draggedTab?.Title}");
            }
            catch { /* Ignore logging errors */ }
            
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
            
            // Phase 3: Enhanced cross-window detection
            UpdateCrossWindowDetection(screenPosition, mainWindow);
            
            // Determine target for drop operation
            DragTarget dragTarget = DetermineDragTarget(screenPosition);
            
            // Update visual feedback based on target
            UpdateVisualFeedback(dragTarget, screenPosition, mainWindow);
            
            // Debug logging
            var isCrossPaneDrag = _currentTargetPaneView != _paneView;
            var isCrossWindowDrag = _currentTargetWindow != null;
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Drag - Screen: ({screenPosition.X:F0}, {screenPosition.Y:F0}), " +
                $"Cross-pane: {isCrossPaneDrag}, Cross-window: {isCrossWindowDrag}, Outside: {_isOutsideMainWindow}, " +
                $"Threshold: {_hasExceededDetachThreshold}");
        }
        
        /// <summary>
        /// Phase 3: Detect cross-window conditions and thresholds
        /// </summary>
        private void UpdateCrossWindowDetection(Point screenPosition, Window mainWindow)
        {
            // Get the actual main window (not the current window)
            var actualMainWindow = System.Windows.Application.Current.MainWindow;
            var mainWindowBounds = new Rect(actualMainWindow.Left, actualMainWindow.Top, actualMainWindow.ActualWidth, actualMainWindow.ActualHeight);
            _isOutsideMainWindow = !mainWindowBounds.Contains(screenPosition);
            
            // Check if we're dragging from a detached window
            bool isDraggingFromDetached = _sourceWindow is NoteNest.UI.Windows.DetachedWindow;
            
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Main window bounds: {mainWindowBounds}, Position: {screenPosition}, Outside: {_isOutsideMainWindow}, From detached: {isDraggingFromDetached}");
            
            if (_isOutsideMainWindow)
            {
                // Calculate distance from main window edge
                var distanceFromWindow = CalculateDistanceFromWindow(screenPosition, mainWindowBounds);
                _hasExceededDetachThreshold = distanceFromWindow > DETACH_THRESHOLD_DISTANCE;
                
                // Phase 3: Find detached window under cursor (if WindowManager available)
                if (_windowManager != null)
                {
                    _currentTargetWindow = FindDetachedWindowAtPoint(screenPosition);
                    _currentTargetWindowViewModel = _currentTargetWindow?.DataContext as DetachedWindowViewModel;
                }
            }
            else
            {
                // Inside main window - clear any detached window targets
                _currentTargetWindow = null;
                _currentTargetWindowViewModel = null;
                
                if (isDraggingFromDetached)
                {
                    // When dragging from detached to main window, we're dropping into main
                    _hasExceededDetachThreshold = false;
                    System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Dragging from detached window to main window");
                }
                else
                {
                    // Normal drag within main window
                    _hasExceededDetachThreshold = false;
                }
            }
        }
        
        /// <summary>
        /// Calculate minimum distance from point to window bounds
        /// </summary>
        private double CalculateDistanceFromWindow(Point screenPosition, Rect windowBounds)
        {
            double dx = Math.Max(0, Math.Max(windowBounds.Left - screenPosition.X, screenPosition.X - windowBounds.Right));
            double dy = Math.Max(0, Math.Max(windowBounds.Top - screenPosition.Y, screenPosition.Y - windowBounds.Bottom));
            return Math.Sqrt(dx * dx + dy * dy);
        }
        
        /// <summary>
        /// Find detached window containing a specific pane
        /// </summary>
        private DetachedWindowViewModel FindDetachedWindowContainingPane(PaneViewModel pane)
        {
            if (_windowManager?.DetachedWindows == null || pane == null) return null;
            
            return _windowManager.DetachedWindows.FirstOrDefault(w => 
                w.PaneViewModel == pane);
        }
        
        /// <summary>
        /// Phase 3: Find detached window under cursor
        /// </summary>
        private DetachedWindow FindDetachedWindowAtPoint(Point screenPosition)
        {
            if (_windowManager?.DetachedWindows == null) return null;
            
            // Check each detached window to see if cursor is over it
            foreach (var windowViewModel in _windowManager.DetachedWindows)
            {
                // Find the actual WPF window (this will need WindowManager integration)
                var wpfWindows = System.Windows.Application.Current.Windows.OfType<DetachedWindow>();
                var detachedWindow = wpfWindows.FirstOrDefault(w => 
                    (w.DataContext as DetachedWindowViewModel)?.WindowId == windowViewModel.WindowId);
                
                if (detachedWindow != null)
                {
                    var windowBounds = new Rect(detachedWindow.Left, detachedWindow.Top, 
                                              detachedWindow.ActualWidth, detachedWindow.ActualHeight);
                    
                    if (windowBounds.Contains(screenPosition))
                    {
                        return detachedWindow;
                    }
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Determine the current drag target (main window pane, detached window, or new window)
        /// </summary>
        private DragTarget DetermineDragTarget(Point screenPosition)
        {
            bool isDraggingFromDetached = _sourceWindow is NoteNest.UI.Windows.DetachedWindow;
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] DetermineDragTarget: Outside={_isOutsideMainWindow}, Threshold={_hasExceededDetachThreshold}, FromDetached={isDraggingFromDetached}");
            System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] DetermineDragTarget - SourceWindow: {_sourceWindow?.GetType().Name}, CurrentPaneView: {_paneView.GetHashCode()}");
            
            // Priority 1: Existing detached window (if cursor is over one)
            if (_currentTargetWindow != null && _currentTargetWindowViewModel != null)
            {
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Dropping into existing detached window");
                return new DragTarget
                {
                    Type = DragTargetType.DetachedWindow,
                    DetachedWindow = _currentTargetWindowViewModel,
                    PaneView = _currentTargetWindow.DetachedPaneView // Assuming this exists in XAML
                };
            }
            
            // Priority 2: Main window pane (inside main window)
            if (!_isOutsideMainWindow)
            {
            var targetPaneView = FindPaneViewAtPoint(screenPosition);
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Main window pane target: {targetPaneView?.GetHashCode() ?? 0} vs source: {_paneView.GetHashCode()}");
                System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Target PaneView found: {targetPaneView?.GetHashCode() ?? -1}, Are they same: {targetPaneView == _paneView}");
                
                // Ensure we have a valid target pane view that's NOT the source
                if (targetPaneView != null && targetPaneView != _paneView)
                {
                    System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Returning MainWindowPane target with PaneView: {targetPaneView.GetHashCode()}");
                    return new DragTarget
                    {
                        Type = DragTargetType.MainWindowPane,
                        PaneView = targetPaneView
                    };
                }
                else if (isDraggingFromDetached)
                {
                    // Dragging from detached to main window but couldn't find specific pane
                    // Use the first available pane
                    var workspace = FindWorkspaceViewModel();
                    if (workspace?.Panes.Count > 0)
                    {
                        // Get the first pane view from the main window
                        // Since we can't directly access the view from the view model,
                        // we'll return the first pane we can find in the main window
                        var mainWindow = System.Windows.Application.Current.MainWindow;
                        var firstPaneView = FindFirstPaneViewInWindow(mainWindow);
                        if (firstPaneView != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Using first pane for detached->main drop");
                            return new DragTarget
                            {
                                Type = DragTargetType.MainWindowPane,
                                PaneView = firstPaneView
                            };
                        }
                    }
                }
            }
            
            // Priority 3: New detached window (if outside main window and threshold exceeded)
            // BUT NOT if we're dragging from a detached window to the main window area
            if (_hasExceededDetachThreshold && _windowManager != null && !isDraggingFromDetached)
            {
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Creating new detached window - threshold exceeded");
                return new DragTarget
                {
                    Type = DragTargetType.NewDetachedWindow
                };
            }
            
            // Fallback: Current pane
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Fallback to current pane");
            return new DragTarget
            {
                Type = DragTargetType.MainWindowPane,
                PaneView = _paneView
            };
        }
        
        /// <summary>
        /// Update visual feedback based on drag target
        /// </summary>
        private void UpdateVisualFeedback(DragTarget target, Point screenPosition, Window mainWindow)
        {
            switch (target.Type)
            {
                case DragTargetType.MainWindowPane:
                    UpdateMainWindowPaneFeedback(target.PaneView, screenPosition, mainWindow);
                    break;
                    
                case DragTargetType.DetachedWindow:
                    UpdateDetachedWindowFeedback(target.DetachedWindow, target.PaneView, screenPosition);
                    break;
                    
                case DragTargetType.NewDetachedWindow:
                    UpdateNewWindowFeedback(screenPosition);
                    break;
            }
        }
        
        /// <summary>
        /// Update feedback for main window pane target
        /// </summary>
        private void UpdateMainWindowPaneFeedback(PaneView targetPaneView, Point screenPosition, Window mainWindow)
        {
            var targetTabControl = targetPaneView?.TabControlElement;
            if (targetTabControl == null) return;
            
            // Update tracked target
            _currentTargetTabControl = targetTabControl;
            _currentTargetPaneView = targetPaneView;
            
            // Convert screen position to target TabControl's coordinates
            var targetTabControlPosition = targetTabControl.PointFromScreen(screenPosition);
            
            // Calculate insertion index
            int insertionIndex = TabHitTestHelper.CalculateInsertionIndex(targetTabControl, targetTabControlPosition, _draggedTab);
            
            // Get insertion bounds
            var insertionBounds = TabHitTestHelper.GetInsertionPointBounds(targetTabControl, insertionIndex);
            
            // Convert to window coordinates for adorner
            var insertionBoundsInWindow = new Rect(
                targetTabControl.TranslatePoint(insertionBounds.TopLeft, mainWindow),
                targetTabControl.TranslatePoint(insertionBounds.BottomRight, mainWindow)
            );
            
            // Update insertion indicator
            _insertionAdorner?.UpdateInsertionPoint(insertionBoundsInWindow);
        }
        
        /// <summary>
        /// Update feedback for detached window target
        /// </summary>
        private void UpdateDetachedWindowFeedback(DetachedWindowViewModel targetWindow, PaneView targetPaneView, Point screenPosition)
        {
            // TODO: Phase 3.2 - Implement cross-window insertion indicator
            // For now, just hide the insertion indicator when over detached window
            _insertionAdorner?.Hide();
            
            // Change cursor to indicate valid drop target
            Mouse.OverrideCursor = Cursors.Hand;
        }
        
        /// <summary>
        /// Update feedback for new detached window target
        /// </summary>
        private void UpdateNewWindowFeedback(Point screenPosition)
        {
            // Hide insertion indicator
            _insertionAdorner?.Hide();
            
            // Show ghost window preview or change cursor
            Mouse.OverrideCursor = Cursors.SizeAll; // Indicates "tear-out"
            
            // TODO: Phase 3.3 - Show ghost window preview
        }
        
        /// <summary>
        /// Find which PaneView the mouse is currently over (Phase 2: Cross-pane support)
        /// </summary>
        private PaneView FindPaneViewAtPoint(Point screenPosition)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] FindPaneViewAtPoint called from {_sourceWindow?.GetType().Name}, SourcePaneView: {_paneView.GetHashCode()}");
                
                // Get the workspace container
                var workspace = FindWorkspaceViewModel();
                if (workspace == null || workspace.Panes.Count <= 1)
                {
                    System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Returning source PaneView (only one pane): {_paneView.GetHashCode()}");
                    return _paneView; // Only one pane, stay in current
                }
                
                // Get the main window (not the current pane's window which might be detached)
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] No main window found, returning source PaneView: {_paneView.GetHashCode()}");
                    return _paneView;
                }
                
                // Convert to window coordinates
                var windowPoint = mainWindow.PointFromScreen(screenPosition);
                
                // Hit-test to find element under cursor
                var hitElement = mainWindow.InputHitTest(windowPoint) as DependencyObject;
                System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Hit test at window point ({windowPoint.X:F0}, {windowPoint.Y:F0}) returned: {hitElement?.GetType().Name ?? "null"}");
                System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Mouse captured by: {Mouse.Captured?.GetType().Name ?? "nothing"}");
                
                // Walk up visual tree to find PaneView
                var foundPaneView = TabHitTestHelper.FindAncestor<PaneView>(hitElement);
                
                // If we found a PaneView in the main window, use it
                if (foundPaneView != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Found PaneView in main window: {foundPaneView.GetHashCode()}");
                    System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Returning found PaneView: {foundPaneView.GetHashCode()} (different from source: {foundPaneView.GetHashCode() != _paneView.GetHashCode()})");
                    return foundPaneView;
                }
                
                // If dragging from detached to main window and no specific pane found,
                // return the first pane in the main window
                if (_sourceWindow is NoteNest.UI.Windows.DetachedWindow && workspace.Panes.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Using first pane in main window for detached->main drag");
                    System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Attempting to find first pane in main window...");
                    
                    // Try multiple approaches to find a main window pane
                    var firstPaneView = FindFirstPaneViewInWindow(mainWindow);
                    if (firstPaneView == null)
                    {
                        // Try a more thorough search
                        firstPaneView = FindPaneViewInVisualTree(mainWindow);
                    }
                    
                    if (firstPaneView != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Found first pane: {firstPaneView.GetHashCode()} (different from source: {firstPaneView.GetHashCode() != _paneView.GetHashCode()})");
                        return firstPaneView;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] ERROR: Could not find ANY PaneView in main window!");
                        // CRITICAL: Do NOT return the source pane from detached window
                        // This would cause the drag to be treated as same-pane reorder
                        return null; // Returning null will cancel the drop, which is better than wrong behavior
                    }
                }
                
                // Only return source pane if NOT dragging from detached window
                if (_sourceWindow is NoteNest.UI.Windows.DetachedWindow)
                {
                    System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] ERROR: About to return source pane from detached window - returning null instead");
                    return null; // Prevent same-pane reorder bug
                }
                
                System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Final fallback - returning source PaneView: {_paneView.GetHashCode()}");
                return _paneView; // Fallback to current pane ONLY for main window drags
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Error in FindPaneViewAtPoint: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Exception occurred, returning source PaneView: {_paneView.GetHashCode()}");
                return _paneView; // Fallback to current pane on error
            }
        }
        
        /// <summary>
        /// Find the first PaneView in a window using visual tree traversal
        /// </summary>
        private PaneView FindFirstPaneViewInWindow(Window window)
        {
            if (window == null) return null;
            
            return FindVisualChild<PaneView>(window);
        }
        
        /// <summary>
        /// More thorough search for PaneView in visual tree
        /// </summary>
        private PaneView FindPaneViewInVisualTree(DependencyObject parent)
        {
            if (parent == null) return null;
            
            // Use a queue for breadth-first search
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(parent);
            
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                
                // Check if this is a PaneView
                if (current is PaneView paneView && paneView != _paneView)
                {
                    System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Found PaneView via BFS: {paneView.GetHashCode()}");
                    return paneView;
                }
                
                // Add all children to queue
                int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < childCount; i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(current, i);
                    if (child != null)
                    {
                        queue.Enqueue(child);
                    }
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] BFS found no PaneView in visual tree");
            return null;
        }
        
        /// <summary>
        /// Helper to find a visual child of a specific type
        /// </summary>
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                
                if (child is T typedChild)
                    return typedChild;
                
                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            
            return null;
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
            
            // If dragging from detached window, try to get workspace from main window
            if (_sourceWindow is NoteNest.UI.Windows.DetachedWindow)
            {
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow?.DataContext is NoteNest.UI.ViewModels.Shell.MainShellViewModel mainShell)
                {
                    return mainShell.Workspace;
                }
            }
            
            return null;
        }
        
        private void CompleteDrag(Point screenDropPosition)
        {
            if (!_isDragging)
                return;
            
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Completing drag at screen position: {screenDropPosition}");
            
            // Log to file for debugging
            try 
            {
                var app = System.Windows.Application.Current as NoteNest.UI.App;
                var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                logger?.Debug($"[TabDragHandler] DRAG COMPLETION STARTED: {_draggedTab?.Title} at {screenDropPosition}");
            }
            catch { /* Ignore logging errors */ }
            
            try
            {
                // Phase 3: Enhanced drop handling with cross-window support
                var dragTarget = DetermineDragTarget(screenDropPosition);
                
                // Execute drag completion immediately (we're already on UI thread)
                HandleDragCompletionSync(dragTarget, screenDropPosition);
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
        
        /// <summary>
        /// Phase 3: Handle drag completion based on target type (Synchronous version)
        /// </summary>
        private void HandleDragCompletionSync(DragTarget target, Point screenDropPosition)
        {
            if (_draggedTab == null) return;
            
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] HandleDragCompletion: Target type = {target.Type}");
            
            // Log to file for debugging
            try 
            {
                var app = System.Windows.Application.Current as NoteNest.UI.App;
                var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                logger?.Debug($"[TabDragHandler] HANDLING DRAG COMPLETION: Target = {target.Type}");
            }
            catch { /* Ignore logging errors */ }
            
            switch (target.Type)
            {
                case DragTargetType.MainWindowPane:
                    HandleMainWindowPaneDropSync(target, screenDropPosition);
                    break;
                    
                case DragTargetType.DetachedWindow:
                    HandleDetachedWindowDropSync(target, screenDropPosition);
                    break;
                    
                case DragTargetType.NewDetachedWindow:
                    HandleNewDetachedWindowDropSync(screenDropPosition);
                    break;
            }
        }
        
        /// <summary>
        /// Phase 3: Handle drag completion based on target type (Async version - kept for advanced operations)
        /// </summary>
        private async Task HandleDragCompletion(DragTarget target, Point screenDropPosition)
        {
            if (_draggedTab == null) return;
            
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] HandleDragCompletion: Target type = {target.Type}");
            
            switch (target.Type)
            {
                case DragTargetType.MainWindowPane:
                    await HandleMainWindowPaneDrop(target, screenDropPosition);
                    break;
                    
                case DragTargetType.DetachedWindow:
                    await HandleDetachedWindowDrop(target, screenDropPosition);
                    break;
                    
                case DragTargetType.NewDetachedWindow:
                    await HandleNewDetachedWindowDrop(screenDropPosition);
                    break;
            }
        }
        
        /// <summary>
        /// Handle drop in main window pane (Synchronous version for basic reordering)
        /// </summary>
        private void HandleMainWindowPaneDropSync(DragTarget target, Point screenDropPosition)
        {
            var sourcePaneVm = _paneView.DataContext as PaneViewModel;
            var targetPaneVm = target.PaneView?.DataContext as PaneViewModel;
            
            System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] HandleMainWindowPaneDropSync - Source PaneView: {_paneView.GetHashCode()}, Target PaneView: {target.PaneView?.GetHashCode() ?? -1}");
            System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Source PaneViewModel: {sourcePaneVm?.GetHashCode() ?? -1}, Target PaneViewModel: {targetPaneVm?.GetHashCode() ?? -1}");
            System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Are ViewModels equal: {sourcePaneVm == targetPaneVm}");
            
            if (sourcePaneVm == null || targetPaneVm == null)
            {
                System.Diagnostics.Debug.WriteLine("[TabDragHandler] Missing ViewModels for main window drop");
                    return;
                }
            
            // Log to file for debugging
            try 
            {
                var app = System.Windows.Application.Current as NoteNest.UI.App;
                var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                logger?.Debug($"[TabDragHandler] MAIN WINDOW DROP: Source={sourcePaneVm.Id}, Target={targetPaneVm.Id}");
            }
            catch { /* Ignore logging errors */ }
            
            // Calculate insertion index
            var targetTabControl = target.PaneView.TabControlElement;
            var targetTabControlPosition = targetTabControl.PointFromScreen(screenDropPosition);
            int insertionIndex = TabHitTestHelper.CalculateInsertionIndex(targetTabControl, targetTabControlPosition, _draggedTab);
            
            // Cross-pane drag
            if (sourcePaneVm != targetPaneVm)
            {
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Cross-pane drag: '{_draggedTab.Title}' to pane at index {insertionIndex}");
                
                // Check if we're dragging from a detached window by checking _sourceWindow
                if (_sourceWindow is NoteNest.UI.Windows.DetachedWindow detachedSourceWindow)
                {
                    // Dragging from detached window to main window
                    System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Dragging from detached window to main window");
                    
                    // Get the DetachedWindowViewModel from the window's DataContext
                    var detachedWindowVm = detachedSourceWindow.DataContext as DetachedWindowViewModel;
                    if (detachedWindowVm != null)
                    {
                        // Remove from source using the DetachedWindowViewModel
                        detachedWindowVm.RemoveTab(_draggedTab);
                        
                        // Add to target pane in main window
                        if (insertionIndex >= 0 && insertionIndex < targetPaneVm.Tabs.Count)
                        {
                            targetPaneVm.Tabs.Insert(insertionIndex, _draggedTab);
                        }
                        else
                        {
                            targetPaneVm.AddTab(_draggedTab, true);
                        }
                        
                        // If source was a detached window that's now empty, close it
                        if (!detachedWindowVm.HasTabs)
                        {
                            try 
                            {
                                var app = System.Windows.Application.Current as NoteNest.UI.App;
                                var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                                logger?.Info($"[TabDragHandler] Closing empty detached window after drag: {detachedWindowVm.WindowId}");
                            }
                            catch { /* Ignore logging errors */ }
                            _windowManager?.CloseDetachedWindowAsync(detachedWindowVm).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[TabDragHandler] ERROR: Could not get DetachedWindowViewModel from DetachedWindow");
                    }
                }
                else
                {
                    // Normal cross-pane drag within main window
                    var workspace = FindWorkspaceViewModel();
                    workspace?.MoveTabBetweenPanes(_draggedTab, sourcePaneVm, targetPaneVm, insertionIndex);
                }
            }
            // Same-pane reorder
            else
            {
                int currentIndex = sourcePaneVm.Tabs.IndexOf(_draggedTab);
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Same-pane reorder check: current={currentIndex}, insertion={insertionIndex}, TabCount={sourcePaneVm.Tabs.Count}");
                
                // Log to file for debugging
                try 
                {
                    var app = System.Windows.Application.Current as NoteNest.UI.App;
                    var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                    logger?.Debug($"[TabDragHandler] SAME-PANE REORDER CHECK: current={currentIndex}, insertion={insertionIndex}");
                }
                catch { /* Ignore logging errors */ }
                
                if (currentIndex != -1 && currentIndex != insertionIndex && insertionIndex >= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[TabDragHandler] ✅ Executing same-pane reorder: '{_draggedTab.Title}' from {currentIndex} to {insertionIndex}");
                    
                    // Log to file for debugging
                    try 
                    {
                        var app = System.Windows.Application.Current as NoteNest.UI.App;
                        var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                        logger?.Debug($"[TabDragHandler] REORDER: '{_draggedTab.Title}' from {currentIndex} to {insertionIndex}");
                    }
                    catch { /* Ignore logging errors */ }
                    
                    sourcePaneVm.Tabs.RemoveAt(currentIndex);
                    
                    if (insertionIndex > currentIndex)
                        insertionIndex--;
                    
                    int finalIndex = Math.Min(insertionIndex, sourcePaneVm.Tabs.Count);
                    sourcePaneVm.Tabs.Insert(finalIndex, _draggedTab);
                    sourcePaneVm.SelectedTab = _draggedTab;
                    
                    System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Tab reordered from {currentIndex} to {finalIndex}");
                    
                    // Log success
                    try 
                    {
                        var app = System.Windows.Application.Current as NoteNest.UI.App;
                        var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                        logger?.Info($"[TabDragHandler] ✅ TAB REORDERED: '{_draggedTab.Title}' moved to position {finalIndex}");
                    }
                    catch { /* Ignore logging errors */ }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[TabDragHandler] ❌ Same-pane reorder skipped: conditions not met");
                    
                    // Log why reorder was skipped
                    try 
                    {
                        var app = System.Windows.Application.Current as NoteNest.UI.App;
                        var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                        logger?.Warning($"[TabDragHandler] REORDER SKIPPED: current={currentIndex}, insertion={insertionIndex}, conditions not met");
                    }
                    catch { /* Ignore logging errors */ }
                }
            }
        }
        
        /// <summary>
        /// Handle drop in detached window (Synchronous version)
        /// </summary>
        private void HandleDetachedWindowDropSync(DragTarget target, Point screenDropPosition)
        {
            if (target.DetachedWindow == null)
            {
                System.Diagnostics.Debug.WriteLine("[TabDragHandler] No target detached window for drop");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Dropping '{_draggedTab?.Title}' into detached window {target.DetachedWindow.WindowId}");
            
            // Log to file for debugging
            try 
            {
                var app = System.Windows.Application.Current as NoteNest.UI.App;
                var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                logger?.Debug($"[TabDragHandler] DETACHED WINDOW DROP: Tab='{_draggedTab?.Title}' to Window={target.DetachedWindow.WindowId}");
            }
            catch { /* Ignore logging errors */ }
            
            // Move tab from source pane to detached window
            var sourcePaneVm = _paneView.DataContext as PaneViewModel;
            if (sourcePaneVm != null && _draggedTab != null)
            {
                // Remove from source (without disposing the tab)
                sourcePaneVm.RemoveTabWithoutDispose(_draggedTab);
                
                // Add to detached window
                target.DetachedWindow.AddTab(_draggedTab, true);
                
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] ✅ Tab moved to detached window successfully");
                
                // Log success
                try 
                {
                    var app = System.Windows.Application.Current as NoteNest.UI.App;
                    var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                    logger?.Info($"[TabDragHandler] ✅ TAB MOVED TO DETACHED WINDOW: '{_draggedTab.Title}'");
                }
                catch { /* Ignore logging errors */ }
            }
        }
        
        /// <summary>
        /// Handle drop to create new detached window (Synchronous version)  
        /// </summary>
        private void HandleNewDetachedWindowDropSync(Point screenDropPosition)
        {
            // CRITICAL: Capture the tab reference NOW before it gets cleared by CleanupDrag!
            var tabToDetach = _draggedTab;
            var sourcePaneVm = _paneView.DataContext as PaneViewModel;
            
            if (tabToDetach == null || _windowManager == null)
            {
                System.Diagnostics.Debug.WriteLine("[TabDragHandler] No tab or WindowManager for detach");
                return;
            }
            
            // Execute on UI thread since WPF windows require STA thread
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                new Action(async () => 
                {
                    try
                    {
                        // Remove from source pane first (captured reference)
                        sourcePaneVm?.RemoveTabWithoutDispose(tabToDetach);
                        
                        // Create detached window with the captured tab
                        var initialTabs = new List<TabViewModel> { tabToDetach };
                        var newWindow = await _windowManager.CreateDetachedWindowAsync(initialTabs);
                        
                        if (newWindow != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] ✅ Tab '{tabToDetach.Title}' detached to window {newWindow.WindowId}");
                            
                            // Log success
                            try 
                            {
                                var app = System.Windows.Application.Current as NoteNest.UI.App;
                                var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                                logger?.Info($"[TabDragHandler] ✅ TAB DETACHED VIA DRAG: '{tabToDetach.Title}'");
                            }
                            catch { /* Ignore logging errors */ }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Error creating detached window: {ex.Message}");
                        
                        // Log error to file
                        try 
                        {
                            var app = System.Windows.Application.Current as NoteNest.UI.App;
                            var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                            logger?.Error(ex, "[TabDragHandler] Failed to create detached window via drag");
                        }
                        catch { /* Ignore logging errors */ }
                    }
                }));
        }
        
        /// <summary>
        /// Handle drop in main window pane (existing functionality)
        /// </summary>
        private Task HandleMainWindowPaneDrop(DragTarget target, Point screenDropPosition)
        {
            var sourcePaneVm = _paneView.DataContext as PaneViewModel;
            var targetPaneVm = target.PaneView?.DataContext as PaneViewModel;
            
            if (sourcePaneVm == null || targetPaneVm == null)
            {
                System.Diagnostics.Debug.WriteLine("[TabDragHandler] Missing ViewModels for main window drop");
                return Task.CompletedTask;
            }
            
            // Calculate insertion index
            var targetTabControl = target.PaneView.TabControlElement;
            var targetTabControlPosition = targetTabControl.PointFromScreen(screenDropPosition);
            int insertionIndex = TabHitTestHelper.CalculateInsertionIndex(targetTabControl, targetTabControlPosition, _draggedTab);
            
            // Cross-pane drag
            if (sourcePaneVm != targetPaneVm)
            {
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Cross-pane drag: '{_draggedTab.Title}' to pane at index {insertionIndex}");
                
                // Check if source pane is in a detached window before moving
                var sourceWindow = FindDetachedWindowContainingPane(sourcePaneVm);
                
                var workspace = FindWorkspaceViewModel();
                workspace?.MoveTabBetweenPanes(_draggedTab, sourcePaneVm, targetPaneVm, insertionIndex);
                
                // If source was a detached window that's now empty, close it
                if (sourceWindow != null && !sourceWindow.HasTabs)
                {
                    try 
                    {
                        var app = System.Windows.Application.Current as NoteNest.UI.App;
                        var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                        logger?.Info($"[TabDragHandler] Closing empty detached window after drag: {sourceWindow.WindowId}");
                    }
                    catch { /* Ignore logging errors */ }
                    _windowManager?.CloseDetachedWindowAsync(sourceWindow).ConfigureAwait(false);
                }
            }
            // Same-pane reorder
            else
            {
                int currentIndex = sourcePaneVm.Tabs.IndexOf(_draggedTab);
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Same-pane reorder check: current={currentIndex}, insertion={insertionIndex}, TabCount={sourcePaneVm.Tabs.Count}");
                
                if (currentIndex != -1 && currentIndex != insertionIndex && insertionIndex >= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[TabDragHandler] ✅ Executing same-pane reorder: '{_draggedTab.Title}' from {currentIndex} to {insertionIndex}");
                    
                    // Log to file for debugging
                    try 
                    {
                        var app = System.Windows.Application.Current as NoteNest.UI.App;
                        var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                        logger?.Debug($"[TabDragHandler] REORDER: '{_draggedTab.Title}' from {currentIndex} to {insertionIndex}");
                    }
                    catch { /* Ignore logging errors */ }
                    
                        sourcePaneVm.Tabs.RemoveAt(currentIndex);
                        
                        if (insertionIndex > currentIndex)
                            insertionIndex--;
                        
                    int finalIndex = Math.Min(insertionIndex, sourcePaneVm.Tabs.Count);
                    sourcePaneVm.Tabs.Insert(finalIndex, _draggedTab);
                        sourcePaneVm.SelectedTab = _draggedTab;
                        
                    System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Tab reordered from {currentIndex} to {finalIndex}");
                    
                    // Log success
                    try 
                    {
                        var app = System.Windows.Application.Current as NoteNest.UI.App;
                        var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                        logger?.Info($"[TabDragHandler] ✅ TAB REORDERED: '{_draggedTab.Title}' moved to position {finalIndex}");
                    }
                    catch { /* Ignore logging errors */ }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[TabDragHandler] ❌ Same-pane reorder skipped: conditions not met");
                    
                    // Log why reorder was skipped
                    try 
                    {
                        var app = System.Windows.Application.Current as NoteNest.UI.App;
                        var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                        logger?.Warning($"[TabDragHandler] REORDER SKIPPED: current={currentIndex}, insertion={insertionIndex}, conditions not met");
                    }
                    catch { /* Ignore logging errors */ }
                }
            }
            
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Phase 3: Handle drop in existing detached window
        /// </summary>
        private async Task HandleDetachedWindowDrop(DragTarget target, Point screenDropPosition)
        {
            if (_windowManager == null || target.DetachedWindow == null)
            {
                System.Diagnostics.Debug.WriteLine("[TabDragHandler] No WindowManager or target window for cross-window drop");
                return;
            }
            
            try
            {
                // Calculate insertion index in target window
                int insertionIndex = 0; // TODO: Calculate proper insertion index for detached window
                
                // Move tab to detached window via WindowManager
                await _windowManager.MoveTabToWindowAsync(_draggedTab, target.DetachedWindow, insertionIndex);
                
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Tab '{_draggedTab.Title}' moved to detached window {target.DetachedWindow.WindowId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Failed to move tab to detached window: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Phase 3: Handle tear-out to new detached window
        /// </summary>
        private async Task HandleNewDetachedWindowDrop(Point screenDropPosition)
        {
            if (_windowManager == null)
            {
                System.Diagnostics.Debug.WriteLine("[TabDragHandler] No WindowManager for new detached window creation");
                return;
            }
            
            try
            {
                // CRITICAL FIX: Remove from source FIRST to avoid Tabs.Contains() check!
                var sourcePaneVm = _paneView.DataContext as PaneViewModel;
                
                // Debug: Log state before removal
                try 
                {
                    var app = System.Windows.Application.Current as NoteNest.UI.App;
                    var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                    logger?.Debug($"[TabDragHandler] BEFORE RemoveTab: Tab='{_draggedTab?.Title}', SourcePane has {sourcePaneVm?.Tabs?.Count ?? -1} tabs");
                }
                catch { /* Ignore logging errors */ }
                
                // Remove from source pane without disposing (tab is being moved, not closed)
                sourcePaneVm?.RemoveTabWithoutDispose(_draggedTab);
                
                try 
                {
                    var app = System.Windows.Application.Current as NoteNest.UI.App;
                    var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                    logger?.Debug($"[TabDragHandler] AFTER RemoveTab: SourcePane now has {sourcePaneVm?.Tabs?.Count ?? -1} tabs");
                }
                catch { /* Ignore logging errors */ }
                
                // NOW create detached window with the tab (after it's been removed from source)
                var initialTabs = new List<TabViewModel> { _draggedTab };
                
                try 
                {
                    var app = System.Windows.Application.Current as NoteNest.UI.App;
                    var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                    logger?.Debug($"[TabDragHandler] BEFORE CreateDetachedWindowAsync: Tab='{_draggedTab?.Title}' (removed from source)");
                }
                catch { /* Ignore logging errors */ }
                
                var newWindow = await _windowManager.CreateDetachedWindowAsync(initialTabs);
                
                if (newWindow != null)
                {
                    try 
                    {
                        var app = System.Windows.Application.Current as NoteNest.UI.App;
                        var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                        logger?.Debug($"[TabDragHandler] SUCCESS: Tab '{_draggedTab?.Title}' moved to detached window {newWindow.WindowId}");
                    }
                    catch { /* Ignore logging errors */ }
                    
                    System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Tab '{_draggedTab.Title}' torn out to new window {newWindow.WindowId}");
                    
                    // Log success
                    try 
                    {
                        var app = System.Windows.Application.Current as NoteNest.UI.App;
                        var logger = app?.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();
                        logger?.Info($"[TabDragHandler] ✅ TAB TORN OUT: '{_draggedTab.Title}' moved to new detached window");
                    }
                    catch { /* Ignore logging errors */ }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Failed to create detached window: {ex.Message}");
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
            _draggedTabItem = null;
            _dragStartPosition = default;
            _currentTargetTabControl = null;
            _currentTargetPaneView = null;
            _currentTargetWindow = null;
            _currentTargetWindowViewModel = null;
            _sourceWindow = null;
            _isOutsideMainWindow = false;
            _hasExceededDetachThreshold = false;
            
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
    
    /// <summary>
    /// Types of drag targets for cross-window operations
    /// </summary>
    public enum DragTargetType
    {
        MainWindowPane,      // Drop in main window pane
        DetachedWindow,      // Drop in existing detached window
        NewDetachedWindow    // Create new detached window
    }
    
    /// <summary>
    /// Represents a drag target for cross-window operations
    /// </summary>
    public class DragTarget
    {
        public DragTargetType Type { get; set; }
        public PaneView PaneView { get; set; }
        public DetachedWindowViewModel DetachedWindow { get; set; }
        public int InsertionIndex { get; set; } = -1;
    }
}

