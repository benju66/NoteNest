using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NoteNest.Core.Interfaces.Services;
using NoteNest.UI.Services.DragDrop;
using NoteNest.Core.Models;
using NoteNest.UI.Windows;

namespace NoteNest.UI.Controls
{
    public enum DragState
    {
        Idle,
        Starting,
        Dragging,
        Completing,
        Cancelling
    }
    public class DraggableTabControl : TabControl
    {
        private Point _dragStartPoint;
        private DragState _dragState = DragState.Idle;
        private readonly TabDragManager _dragManager;
        private readonly DropZoneManager _dropZoneManager;
        private static WeakReference<SplitPaneView> _lastHighlightedPane;
        private long _lastMouseUpdate;
        private const long MOUSE_UPDATE_TICKS = 166667; // ~16.67ms => ~60 FPS
        private Point _cachedScreenPoint;
        private bool _screenPointValid;
        private readonly System.Collections.Generic.Dictionary<FrameworkElement, Point> _localPointCache = new();

        public DraggableTabControl()
        {
            _dragManager = (Application.Current as App)?.ServiceProvider?.GetService(typeof(TabDragManager)) as TabDragManager ?? new TabDragManager();
            _dropZoneManager = (Application.Current as App)?.ServiceProvider?.GetService(typeof(DropZoneManager)) as DropZoneManager ?? new DropZoneManager();
            AllowDrop = true;
            Loaded += (s, e) => _dropZoneManager.RegisterDropZone($"TabControl_{GetHashCode()}", this);
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            var tabItem = FindTabItemFromPoint(e.GetPosition(this));
            if (tabItem != null)
            {
                _dragStartPoint = PointToScreen(e.GetPosition(this));
            }
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);

            // Throttle high-frequency mouse move handling to ~60 FPS for smoothness and lower CPU usage
            var now = DateTime.UtcNow.Ticks;
            if (now - _lastMouseUpdate < MOUSE_UPDATE_TICKS)
                return;
            _lastMouseUpdate = now;
            InvalidateCoordinateCache();
            if (e.LeftButton == MouseButtonState.Pressed && _dragState == DragState.Idle)
            {
                var current = GetScreenPoint(e);
                var diff = _dragStartPoint - current;
                if (Math.Abs(diff.X) > 5 || Math.Abs(diff.Y) > 5)
                {
                    var tabItem = FindTabItemFromPoint(e.GetPosition(this));
                    if (tabItem?.DataContext is ITabItem tab)
                    {
                        StartManualDrag(tab);
                    }
                }
            }
            else if (_dragState == DragState.Dragging)
            {
                var screenPoint = GetScreenPoint(e);
                _dragManager.UpdateManualDrag(screenPoint);
                UpdateDropTarget(screenPoint);

                // Stay in manual-drag mode across windows; rely on screenPoint targeting
            }
        }

        private Point GetScreenPoint(MouseEventArgs e)
        {
            if (!_screenPointValid)
            {
                _cachedScreenPoint = PointToScreen(e.GetPosition(this));
                _screenPointValid = true;
            }
            return _cachedScreenPoint;
        }

        private Point GetLocalPoint(FrameworkElement element, Point screenPoint)
        {
            if (!_localPointCache.TryGetValue(element, out var localPoint))
            {
                localPoint = element.PointFromScreen(screenPoint);
                _localPointCache[element] = localPoint;
            }
            return localPoint;
        }

        private void InvalidateCoordinateCache()
        {
            _screenPointValid = false;
            _localPointCache.Clear();
        }

        private void UpdateDropTarget(Point screenPoint)
        {
            try
            {
                var target = _dropZoneManager.GetDropTarget(screenPoint);
                if (target != null)
                {
                    if (target.Element is DraggableTabControl targetControl)
                    {
                        var headerPanel = targetControl.GetHeaderPanel();
                        if (headerPanel != null)
                        {
                            var local = GetLocalPoint(headerPanel, screenPoint);
                            var idx = CalculateIndexFromPoint(local, headerPanel);
                            _dropZoneManager.ShowInsertionLine(headerPanel, idx);
                        }

                        // Pane highlight on destination
                        var spv = FindAncestor<SplitPaneView>(targetControl);
                        if (spv != null)
                        {
                            // Clear previous highlight
                            if (_lastHighlightedPane != null && _lastHighlightedPane.TryGetTarget(out var prev) && prev != spv)
                            {
                                prev.SetDropHighlight(false);
                            }
                            spv.SetDropHighlight(true);
                            _lastHighlightedPane = new WeakReference<SplitPaneView>(spv);
                        }
                    }
                    else if (target.Element == this)
                    {
                        var headerPanel = GetHeaderPanel();
                        if (headerPanel != null)
                        {
                            var local = GetLocalPoint(headerPanel, screenPoint);
                            var idx = CalculateIndexFromPoint(local, headerPanel);
                            _dropZoneManager.ShowInsertionLine(headerPanel, idx);
                        }
                    }
                }
                else
                {
                    _dropZoneManager.HideInsertionLine();
                    // Clear highlight if any
                    if (_lastHighlightedPane != null && _lastHighlightedPane.TryGetTarget(out var prev))
                    {
                        prev.SetDropHighlight(false);
                        _lastHighlightedPane = null;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Drop target update failed: {ex.Message}");
                try { _dropZoneManager.HideInsertionLine(); } catch { }
                try
                {
                    if (_lastHighlightedPane?.TryGetTarget(out var prev) == true)
                    {
                        prev.SetDropHighlight(false);
                        _lastHighlightedPane = null;
                    }
                }
                catch { }
            }
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);
            if (_dragState == DragState.Dragging)
            {
                CompleteDrop(GetScreenPoint(e));
            }
        }

        private void StartManualDrag(ITabItem tab)
        {
            if (_dragState != DragState.Idle)
            {
                System.Diagnostics.Debug.WriteLine($"Attempted to start drag while in state: {_dragState}");
                return;
            }

            _dragState = DragState.Starting;

            try
            {
                var window = Window.GetWindow(this);
                if (_dragManager.BeginManualDrag(tab, this, window, _dragStartPoint))
                {
                    _dragState = DragState.Dragging;
                    PreviewKeyDown += OnDragKeyDown;
                }
                else
                {
                    _dragState = DragState.Idle;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start drag: {ex.Message}");
                _dragState = DragState.Idle;
            }
        }

        private void CompleteDrop(Point screenPoint)
        {
            if (_dragState != DragState.Dragging)
            {
                System.Diagnostics.Debug.WriteLine($"Attempted to complete drop while in state: {_dragState}");
                return;
            }

            _dragState = DragState.Completing;

            try
            {
                var draggedTab = _dragManager.GetDraggedTab();
                if (draggedTab != null)
                {
                    var target = _dropZoneManager.GetDropTarget(screenPoint);
                    if (target != null && target.Element == this)
                    {
                        var headerPanel = GetHeaderPanel();
                        if (headerPanel != null)
                        {
                            var local = GetLocalPoint(headerPanel, screenPoint);
                            var idx = CalculateIndexFromPoint(local, headerPanel);
                            ReorderWithinPane(draggedTab, idx);
                        }
                    }
                    else if (target?.Element is DraggableTabControl other)
                    {
                        MoveToOtherPane(draggedTab, other, screenPoint);
                    }
                    else
                    {
                        // If released outside any drop zone and outside window threshold, detach into new window
                        if (_dragManager.IsOutsideDetachThreshold(screenPoint))
                        {
                            DetachToNewWindow(draggedTab, screenPoint);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Drop operation failed: {ex.Message}");
            }
            finally
            {
                EndDragOperation(true);
            }
        }

        private void OnDragKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _dragState == DragState.Dragging)
            {
                _dragState = DragState.Cancelling;
                EndDragOperation(false);
                e.Handled = true;
            }
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            if (e.Data.GetDataPresent("NoteNestTab"))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            if (e.Data.GetDataPresent("NoteNestTab"))
            {
                var headerPanel = GetHeaderPanel();
                if (headerPanel != null)
                {
                    var index = CalculateIndexFromPoint(e.GetPosition(headerPanel), headerPanel);
                    _dropZoneManager.ShowInsertionLine(headerPanel, index);
                }
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        protected override void OnDragLeave(DragEventArgs e)
        {
            base.OnDragLeave(e);
            _dropZoneManager.HideInsertionLine();
        }

        protected override void OnDrop(DragEventArgs e)
        {
            base.OnDrop(e);
            if (e.Data.GetDataPresent("NoteNestTab"))
            {
                if (e.Data.GetData("NoteNestTab") is ITabItem tab)
                {
                    var headerPanel = GetHeaderPanel();
                    if (headerPanel != null)
                    {
                        var idx = CalculateIndexFromPoint(e.GetPosition(headerPanel), headerPanel);
                        var list = ItemsSource as System.Collections.IList;
                        if (list != null && list.Contains(tab))
                        {
                            ReorderWithinPane(tab, idx);
                        }
                        else
                        {
                            // Cross-window/tabcontrol drop: move via service at computed index
                            var services = (Application.Current as App)?.ServiceProvider;
                            var workspace = services?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
                            var targetPane = GetPaneFromControl(this);
                            if (workspace != null && targetPane != null)
                            {
                                _ = workspace.MoveTabToPaneAsync(tab, targetPane, idx);
                            }
                        }
                    }
                }
                _dropZoneManager.HideInsertionLine();
                e.Handled = true;
            }
        }

        private void ReorderWithinPane(ITabItem tab, int index)
        {
            if (ItemsSource is System.Collections.IList list)
            {
                var currentIndex = list.IndexOf(tab);
                if (currentIndex < 0) return;
                // Adjust target index when moving to the right, because removing shifts indices left
                var targetIndex = index;
                if (targetIndex > currentIndex) targetIndex--;
                if (targetIndex == currentIndex) return;

                list.Remove(tab);
                if (targetIndex >= 0 && targetIndex <= list.Count)
                    list.Insert(targetIndex, tab);
                else
                    list.Add(tab);
                SelectedItem = tab;
            }
        }

        private Panel GetHeaderPanel()
        {
            var tp = FindVisualChild<TabPanel>(this);
            if (tp != null) return tp;
            var sp = FindVisualChild<StackPanel>(this);
            return sp;
        }

        private TabItem FindTabItemFromPoint(Point point)
        {
            var element = InputHitTest(point) as DependencyObject;
            while (element != null && element is not TabItem)
            {
                element = VisualTreeHelper.GetParent(element);
            }
            return element as TabItem;
        }

        private void MoveToOtherPane(ITabItem tab, DraggableTabControl targetControl, Point screenPoint)
        {
            try
            {
                var services = (Application.Current as App)?.ServiceProvider;
                var workspace = services?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
                var targetPane = GetPaneFromControl(targetControl);
                if (workspace != null && targetPane != null)
                {
                    var headerPanel = targetControl.GetHeaderPanel();
                    var local = headerPanel.PointFromScreen(screenPoint);
                    var idx = CalculateIndexFromPoint(local, headerPanel);
                    _ = workspace.MoveTabToPaneAsync(tab, targetPane, idx);
                }
            }
            catch { }
        }

        private static SplitPane GetPaneFromControl(DraggableTabControl control)
        {
            var spv = FindAncestor<SplitPaneView>(control);
            return spv?.Pane;
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private static T FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            var current = start;
            while (current != null)
            {
                if (current is T t) return t;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static int CalculateIndexFromPoint(Point local, Panel panel)
        {
            double acc = 0;
            for (int i = 0; i < panel.Children.Count; i++)
            {
                if (panel.Children[i] is FrameworkElement child)
                {
                    acc += child.ActualWidth;
                    if (local.X < acc)
                        return i;
                }
            }
            return panel.Children.Count;
        }

        private void DetachToNewWindow(ITabItem tab, Point screenPoint)
        {
            try
            {
                // Remove from current pane list
                if (ItemsSource is System.Collections.IList list && list.Contains(tab))
                {
                    list.Remove(tab);
                }
                var services = (Application.Current as App)?.ServiceProvider;
                var window = new DetachedTabWindow(tab, screenPoint, services);
                window.Show();
            }
            catch { }
        }

        private void EndDragOperation(bool completed)
        {
            try
            {
                _dragManager.EndManualDrag(completed);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error ending drag manager: {ex.Message}");
            }

            try
            {
                _dropZoneManager.HideInsertionLine();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error hiding insertion line: {ex.Message}");
            }

            if (_lastHighlightedPane?.TryGetTarget(out var prev) == true)
            {
                try
                {
                    prev.SetDropHighlight(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error clearing highlight: {ex.Message}");
                }
                _lastHighlightedPane = null;
            }

            PreviewKeyDown -= OnDragKeyDown;
            _dragState = DragState.Idle;
        }
    }

    internal static class PointExtensions
    {
        public static Point ToScreenPoint(this Point point, Visual relativeTo)
        {
            return relativeTo.PointToScreen(point);
        }
    }
}


