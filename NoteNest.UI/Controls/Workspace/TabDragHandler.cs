using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using NoteNest.UI.ViewModels.Workspace;

namespace NoteNest.UI.Controls.Workspace
{
    /// <summary>
    /// Handles all drag & drop logic for tab reordering
    /// Part of Milestone 2B: Drag & Drop - Phase 1 (same pane reordering)
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
                UpdateDragVisuals(e.GetPosition(_tabControl));
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
            
            // Get adorner layer
            _adornerLayer = AdornerLayer.GetAdornerLayer(_tabControl);
            if (_adornerLayer == null)
            {
                System.Diagnostics.Debug.WriteLine("[TabDragHandler] ERROR: No adorner layer found!");
                CancelDrag();
                return;
            }
            
            // Create drag adorner (ghost image)
            _dragAdorner = new TabDragAdorner(_tabControl, _draggedTabItem);
            _adornerLayer.Add(_dragAdorner);
            
            // Create insertion indicator
            _insertionAdorner = new InsertionIndicatorAdorner(_tabControl);
            _adornerLayer.Add(_insertionAdorner);
            
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
            
            // Complete the drop
            CompleteDrag(e.GetPosition(_tabControl));
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
        
        private void UpdateDragVisuals(Point position)
        {
            if (!_isDragging)
                return;
            
            // Update ghost image position
            _dragAdorner?.UpdatePosition(position);
            
            // Calculate insertion index
            int insertionIndex = TabHitTestHelper.CalculateInsertionIndex(_tabControl, position, _draggedTab);
            
            // Update insertion indicator
            var insertionBounds = TabHitTestHelper.GetInsertionPointBounds(_tabControl, insertionIndex);
            _insertionAdorner?.UpdateInsertionPoint(insertionBounds);
            
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Drag position: {position.X:F0}, Insertion index: {insertionIndex}");
        }
        
        private void CompleteDrag(Point dropPosition)
        {
            if (!_isDragging)
                return;
            
            System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Completing drag at position: {dropPosition}");
            
            try
            {
                // Calculate where to insert
                int insertionIndex = TabHitTestHelper.CalculateInsertionIndex(_tabControl, dropPosition, _draggedTab);
                int currentIndex = (_paneView.DataContext as PaneViewModel)?.Tabs.IndexOf(_draggedTab) ?? -1;
                
                System.Diagnostics.Debug.WriteLine($"[TabDragHandler] Current index: {currentIndex}, Target index: {insertionIndex}");
                
                // Only move if position changed
                if (currentIndex != -1 && currentIndex != insertionIndex && insertionIndex >= 0)
                {
                    var paneVm = _paneView.DataContext as PaneViewModel;
                    if (paneVm != null)
                    {
                        // Remove from current position
                        paneVm.Tabs.RemoveAt(currentIndex);
                        
                        // Adjust insertion index if needed
                        if (insertionIndex > currentIndex)
                            insertionIndex--;
                        
                        // Insert at new position
                        paneVm.Tabs.Insert(Math.Min(insertionIndex, paneVm.Tabs.Count), _draggedTab);
                        
                        // Keep it selected
                        paneVm.SelectedTab = _draggedTab;
                        
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

