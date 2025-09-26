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
using NoteNest.UI.Utils;
using System.Runtime.CompilerServices;
using System.Windows.Automation.Peers;
using System.Windows.Automation;
using NoteNest.UI.Config;
using NoteNest.Core.Diagnostics;

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
        // Ensure our custom template from Themes/Generic.xaml is used
        static DraggableTabControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(DraggableTabControl),
                new FrameworkPropertyMetadata(typeof(DraggableTabControl)));
        }
        private Point _dragStartPoint;
        private DragState _dragState = DragState.Idle;
        private readonly TabDragManager _dragManager;
        private readonly DropZoneManager _dropZoneManager;
        private static WeakReference<SplitPaneView> _lastHighlightedPane;
        private long _lastMouseUpdate;
        private const long MOUSE_UPDATE_TICKS = 166667; // ~16.67ms => ~60 FPS
        private long MouseUpdateTicks => DragConfig.Instance.MouseUpdateIntervalTicks;
        private Point _cachedScreenPoint;
        private bool _screenPointValid;
        private readonly System.Collections.Generic.Dictionary<FrameworkElement, Point> _localPointCache = new();
        private readonly ConditionalWeakTable<FrameworkElement, SplitPaneView> _ancestorCache = new();
        private readonly ConditionalWeakTable<FrameworkElement, TimestampHolder> _cacheTimestamp = new();
        private static readonly TimeSpan CACHE_LIFETIME = TimeSpan.FromMinutes(5);
        private System.Windows.Threading.DispatcherTimer? _cacheCleanupTimer;
        private string _lastPerformedAction = string.Empty;

        private sealed class TimestampHolder
        {
            public DateTime Timestamp { get; }
            public TimestampHolder(DateTime ts) { Timestamp = ts; }
        }

        public DraggableTabControl()
        {
            _dragManager = (System.Windows.Application.Current as App)?.ServiceProvider?.GetService(typeof(TabDragManager)) as TabDragManager ?? new TabDragManager();
            _dropZoneManager = (System.Windows.Application.Current as App)?.ServiceProvider?.GetService(typeof(DropZoneManager)) as DropZoneManager ?? new DropZoneManager();
            AllowDrop = true;
            Focusable = true;
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
            Loaded += (s, e) =>
            {
                _dropZoneManager.RegisterDropZone($"TabControl_{GetHashCode()}", this);

                // Periodically clean the visual tree cache
                _cacheCleanupTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMinutes(2)
                };
                _cacheCleanupTimer.Tick += (sender, args) => InvalidateVisualTreeCache();
                _cacheCleanupTimer.Start();

                // Accessibility tooltips/help text
                SetupAccessibilityTooltips();
            };

            // Clean caches on unload
            Unloaded += (s, e) =>
            {
                try
                {
                    if (_cacheCleanupTimer != null)
                    {
                        _cacheCleanupTimer.Stop();
                        _cacheCleanupTimer = null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error cleaning cache on unload: {ex.Message}");
                }
            };
        }

#if DEBUG
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            var scrollViewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
            var leftButton = GetTemplateChild("PART_LeftButton") as Button;
            var rightButton = GetTemplateChild("PART_RightButton") as Button;
            var dropdownButton = GetTemplateChild("PART_DropdownButton") as Button;

            if (scrollViewer == null || leftButton == null || rightButton == null || dropdownButton == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"WARNING: DraggableTabControl template is missing parts. " +
                    $"ScrollViewer={scrollViewer != null}, LeftButton={leftButton != null}, " +
                    $"RightButton={rightButton != null}, DropdownButton={dropdownButton != null}. " +
                    $"Tab overflow feature will not work. Check Themes/Generic.xaml");
            }
        }
#endif

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
            if (now - _lastMouseUpdate < MouseUpdateTicks)
                return;
            _lastMouseUpdate = now;
            InvalidateCoordinateCache();
            if (e.LeftButton == MouseButtonState.Pressed && _dragState == DragState.Idle)
            {
                var current = GetScreenPoint(e);
                var diff = _dragStartPoint - current;
                var threshold = DragConfig.Instance.DragThresholdPixels;
                if (Math.Abs(diff.X) > threshold || Math.Abs(diff.Y) > threshold)
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

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            if (_dragState == DragState.Dragging)
            {
                if (e.Key == Key.Escape)
                {
                    _dragState = DragState.Cancelling;
                    EndDragOperation(false);
                    e.Handled = true;
                    return;
                }
            }

            if (_dragState == DragState.Idle && SelectedItem is ITabItem tabItem)
            {
                bool handled = false;
                var mods = Keyboard.Modifiers;

                if (e.Key == Key.F6 && mods == ModifierKeys.Control)
                {
                    handled = MoveTabToNextPane(tabItem);
                }
                else if (e.Key == Key.F6 && mods == (ModifierKeys.Control | ModifierKeys.Shift))
                {
                    handled = MoveTabToPreviousPane(tabItem);
                }
                else if (e.Key == Key.Left && mods == (ModifierKeys.Control | ModifierKeys.Shift))
                {
                    handled = MoveTabWithinPane(tabItem, -1);
                }
                else if (e.Key == Key.Right && mods == (ModifierKeys.Control | ModifierKeys.Shift))
                {
                    handled = MoveTabWithinPane(tabItem, 1);
                }
                else if (e.Key == Key.D && mods == ModifierKeys.Control)
                {
                    handled = DetachTabToNewWindow(tabItem);
                }

                if (handled)
                {
                    e.Handled = true;
                    AnnounceTabMovement(tabItem, GetLastPerformedAction());
                }
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
            #if DEBUG
            EnhancedMemoryTracker.TrackServiceOperation("DraggableTabControl", "StartManualDrag", () =>
            {
            #endif
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
            #if DEBUG
            });
            #endif
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
                            var scrollViewer = GetTabScrollViewer();
                            var idx = TabIndexCalculator.CalculateInsertionIndex(headerPanel, local, scrollViewer);
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
            if (_dragState == DragState.Dragging)
            {
                if (e.Key == Key.Escape)
                {
                    _dragState = DragState.Cancelling;
                    EndDragOperation(false);
                    e.Handled = true;
                    return;
                }

                // Allow detaching while dragging with Ctrl+D
                if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    var draggedTab = _dragManager.GetDraggedTab();
                    if (draggedTab != null)
                    {
                        // Use last known screen point; fall back to current mouse
                        Point screenPoint;
                        try
                        {
                            if (_screenPointValid)
                            {
                                screenPoint = _cachedScreenPoint;
                            }
                            else
                            {
                                var pos = Mouse.GetPosition(this);
                                screenPoint = PointToScreen(pos);
                            }
                        }
                        catch { screenPoint = new Point(SystemParameters.WorkArea.Width / 2, SystemParameters.WorkArea.Height / 2); }

                        try { DetachToNewWindow(draggedTab, screenPoint); } catch { }
                        EndDragOperation(true);
                        e.Handled = true;
                        return;
                    }
                }
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
                    var scrollViewer = GetTabScrollViewer();
                    var index = TabIndexCalculator.CalculateInsertionIndex(headerPanel, e.GetPosition(headerPanel), scrollViewer);
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
                        var scrollViewer = GetTabScrollViewer();
                        var idx = TabIndexCalculator.CalculateInsertionIndex(headerPanel, e.GetPosition(headerPanel), scrollViewer);
                        var list = ItemsSource as System.Collections.IList;
                        if (list != null && list.Contains(tab))
                        {
                            ReorderWithinPane(tab, idx);
                        }
                        else
                        {
                            // Cross-window/tabcontrol drop: move via service at computed index
                            var services = (System.Windows.Application.Current as App)?.ServiceProvider;
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

        // Fix 2: Expose ScrollViewer from template for scroll-aware calculations
        public ScrollViewer GetTabScrollViewer()
        {
            return Template?.FindName("PART_ScrollViewer", this) as ScrollViewer;
        }

        private TabItem FindTabItemFromPoint(Point point)
        {
            var element = InputHitTest(point) as DependencyObject;
            while (element != null && element is not TabItem)
            {
                if (element is not Visual && element is not System.Windows.Media.Media3D.Visual3D)
                {
                    return null;
                }
                element = VisualTreeHelper.GetParent(element);
            }
            return element as TabItem;
        }

        private void MoveToOtherPane(ITabItem tab, DraggableTabControl targetControl, Point screenPoint)
        {
            try
            {
                var services = (System.Windows.Application.Current as App)?.ServiceProvider;
                var workspace = services?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
                var targetPane = GetPaneFromControl(targetControl);
                if (workspace != null && targetPane != null)
                {
                    var headerPanel = targetControl.GetHeaderPanel();
                    var local = headerPanel.PointFromScreen(screenPoint);
                    var scrollViewer = targetControl.GetTabScrollViewer();
                    var idx = TabIndexCalculator.CalculateInsertionIndex(headerPanel, local, scrollViewer);
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
            // Only Visual/Visual3D can be traversed via VisualTreeHelper
            if (parent is not Visual && parent is not System.Windows.Media.Media3D.Visual3D)
            {
                return null;
            }
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
                if (current is not Visual && current is not System.Windows.Media.Media3D.Visual3D)
                    return null;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
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
                var services = (System.Windows.Application.Current as App)?.ServiceProvider;
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

        private SplitPaneView? FindCachedSplitPaneAncestor(FrameworkElement element)
        {
            if (element == null) return null;

            if (_ancestorCache.TryGetValue(element, out var cachedResult))
            {
                if (_cacheTimestamp.TryGetValue(element, out var tsHolder))
                {
                    if (DateTime.UtcNow - tsHolder.Timestamp < CACHE_LIFETIME)
                    {
                        if (cachedResult != null && IsElementInVisualTree(cachedResult))
                        {
                            return cachedResult;
                        }
                    }
                }

                // Cache is stale, remove it
                _ancestorCache.Remove(element);
                _cacheTimestamp.Remove(element);
            }

            var result = ComputeSplitPaneAncestor(element);
            if (result != null)
            {
                _ancestorCache.Add(element, result);
                _cacheTimestamp.Add(element, new TimestampHolder(DateTime.UtcNow));
            }

            return result;
        }

        private static SplitPaneView? ComputeSplitPaneAncestor(FrameworkElement element)
        {
            try
            {
                var current = element as DependencyObject;
                while (current != null)
                {
                    if (current is SplitPaneView splitPane)
                        return splitPane;
                    current = VisualTreeHelper.GetParent(current);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error finding SplitPaneView ancestor: {ex.Message}");
            }
            return null;
        }

        private static bool IsElementInVisualTree(FrameworkElement element)
        {
            try
            {
                return PresentationSource.FromVisual(element) != null;
            }
            catch
            {
                return false;
            }
        }

        private void InvalidateVisualTreeCache()
        {
            // Best-effort: no-op for now; ConditionalWeakTable entries will auto-expire with GC
        }

        private bool MoveTabToNextPane(ITabItem tab)
        {
            try
            {
                var workspace = GetWorkspaceService();
                var currentPane = GetPaneFromControl(this);
                if (workspace == null || currentPane == null) return false;

                var panesList = string.IsNullOrEmpty(currentPane.OwnerKey)
                    ? workspace.Panes.ToList()
                    : workspace.DetachedPanes.Where(p => p.OwnerKey == currentPane.OwnerKey).ToList();

                if (panesList.Count > 1)
                {
                    var currentIndex = panesList.IndexOf(currentPane);
                    if (currentIndex >= 0)
                    {
                        var nextIndex = (currentIndex + 1) % panesList.Count;
                        var targetPane = panesList[nextIndex];
                        _ = workspace.MoveTabToPaneAsync(tab, targetPane, 0);
                        _lastPerformedAction = $"Moved to pane {nextIndex + 1}";
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error moving tab to next pane: {ex.Message}");
            }
            return false;
        }

        private bool MoveTabToPreviousPane(ITabItem tab)
        {
            try
            {
                var workspace = GetWorkspaceService();
                var currentPane = GetPaneFromControl(this);
                if (workspace == null || currentPane == null) return false;

                var panesList = string.IsNullOrEmpty(currentPane.OwnerKey)
                    ? workspace.Panes.ToList()
                    : workspace.DetachedPanes.Where(p => p.OwnerKey == currentPane.OwnerKey).ToList();

                if (panesList.Count > 1)
                {
                    var currentIndex = panesList.IndexOf(currentPane);
                    if (currentIndex >= 0)
                    {
                        var prevIndex = currentIndex == 0 ? panesList.Count - 1 : currentIndex - 1;
                        var targetPane = panesList[prevIndex];
                        _ = workspace.MoveTabToPaneAsync(tab, targetPane, 0);
                        _lastPerformedAction = $"Moved to pane {prevIndex + 1}";
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error moving tab to previous pane: {ex.Message}");
            }
            return false;
        }

        private bool MoveTabWithinPane(ITabItem tab, int direction)
        {
            try
            {
                var workspace = GetWorkspaceService();
                var currentPane = GetPaneFromControl(this);
                if (workspace == null || currentPane == null) return false;

                var currentIndex = currentPane.Tabs.IndexOf(tab);
                if (currentIndex >= 0)
                {
                    var newIndex = currentIndex + direction;
                    if (newIndex >= 0 && newIndex < currentPane.Tabs.Count)
                    {
                        _ = workspace.MoveTabToPaneAsync(tab, currentPane, newIndex);
                        _lastPerformedAction = direction > 0 ? "Moved right" : "Moved left";
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error moving tab within pane: {ex.Message}");
            }
            return false;
        }

        private bool DetachTabToNewWindow(ITabItem tab)
        {
            try
            {
                var currentPosition = PointToScreen(new Point(0, 0));
                var offsetPosition = new Point(currentPosition.X + 50, currentPosition.Y + 50);
                DetachToNewWindow(tab, offsetPosition);
                _lastPerformedAction = "Detached to new window";
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error detaching tab: {ex.Message}");
                return false;
            }
        }

        private IWorkspaceService? GetWorkspaceService()
        {
            try
            {
                var app = System.Windows.Application.Current as App;
                return app?.ServiceProvider?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
            }
            catch
            {
                return null;
            }
        }

        private string GetLastPerformedAction()
        {
            return _lastPerformedAction;
        }

        private void AnnounceTabMovement(ITabItem tab, string action)
        {
            try
            {
                var announcement = $"Tab '{tab.Title}' {action}";

                if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                {
                    var peer = UIElementAutomationPeer.FromElement(this);
                    if (peer != null)
                    {
                        peer.RaiseNotificationEvent(
                            AutomationNotificationKind.ActionCompleted,
                            AutomationNotificationProcessing.MostRecent,
                            announcement,
                            "TabMovement");
                    }
                }

                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        if (Items.Contains(tab))
                        {
                            SelectedItem = tab;
                            Focus();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error setting focus after move: {ex.Message}");
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);

                System.Diagnostics.Debug.WriteLine($"Accessibility: {announcement}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error announcing tab movement: {ex.Message}");
            }
        }

        private void SetupAccessibilityTooltips()
        {
            try
            {
                var tooltipText = "Keyboard shortcuts:\n" +
                                 "Ctrl+F6: Move to next pane\n" +
                                 "Ctrl+Shift+F6: Move to previous pane\n" +
                                 "Ctrl+Shift+\u2190/\u2192: Move within pane\n" +
                                 "Ctrl+D: Detach to new window\n" +
                                 "Esc: Cancel drag operation";

                // ToolTip disabled - keyboard shortcuts still work, just no visual tooltip
                // ToolTip = new ToolTip
                // {
                //     Content = tooltipText,
                //     Placement = PlacementMode.Bottom
                // };

                AutomationProperties.SetHelpText(this, tooltipText);
                AutomationProperties.SetName(this, "Tab Control with Keyboard Navigation");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting up accessibility tooltips: {ex.Message}");
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
                            var scrollViewer = targetControl.GetTabScrollViewer();
                            var idx = TabIndexCalculator.CalculateInsertionIndex(headerPanel, local, scrollViewer);
                            _dropZoneManager.ShowInsertionLine(headerPanel, idx);
                        }

                        // Pane highlight on destination (cached ancestor)
                        var spv = FindCachedSplitPaneAncestor(targetControl);
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
                            var scrollViewer = GetTabScrollViewer();
                            var idx = TabIndexCalculator.CalculateInsertionIndex(headerPanel, local, scrollViewer);
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
    }

    internal static class PointExtensions
    {
        public static Point ToScreenPoint(this Point point, Visual relativeTo)
        {
            return relativeTo.PointToScreen(point);
        }
    }
}


