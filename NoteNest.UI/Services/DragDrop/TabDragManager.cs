using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NoteNest.Core.Interfaces.Services;
using NoteNest.UI.Config;

namespace NoteNest.UI.Services.DragDrop
{
    public sealed class TabDragManager : IDisposable
    {
        private WeakReference<ITabItem>? _draggedTab;
        private Window? _sourceWindow;
        private FrameworkElement? _sourceElement;
        private Popup? _ghostPopup;
        private Point _dragOffset;
        private bool _isManualDragging;

        private Popup GhostPopup => _ghostPopup ??= new Popup
        {
            AllowsTransparency = true,
            IsHitTestVisible = false,
            Placement = PlacementMode.Absolute,
            PopupAnimation = PopupAnimation.None
        };

        public bool BeginManualDrag(ITabItem tab, FrameworkElement source, Window window, Point startScreenPoint)
        {
            if (_draggedTab?.TryGetTarget(out _) == true)
                return false;

            _draggedTab = new WeakReference<ITabItem>(tab);
            _sourceWindow = window;
            _sourceElement = source;
            _dragOffset = new Point(0, 0);
            _isManualDragging = true;

            CreateGhost(tab, startScreenPoint);

            Mouse.Capture(_sourceElement, CaptureMode.SubTree);
            Mouse.OverrideCursor = Cursors.Hand;

            if (_sourceElement != null)
                _sourceElement.Opacity = 0.5;

            return true;
        }

        private void CreateGhost(ITabItem tab, Point startScreenPoint)
        {
            byte alpha = (byte)(Math.Round(Math.Max(0.0, Math.Min(1.0, DragConfig.Instance.GhostOpacity)) * 255));
            var ghost = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(alpha, 240, 240, 240)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(alpha, 128, 128, 128)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock
                {
                    Text = tab.Title,
                    Foreground = Brushes.Black,
                    FontSize = 12
                }
            };

            GhostPopup.Child = ghost;
            GhostPopup.IsOpen = true;
            GhostPopup.HorizontalOffset = startScreenPoint.X;
            GhostPopup.VerticalOffset = startScreenPoint.Y;
        }

        public void UpdateManualDrag(Point screenPoint)
        {
            if (!_isManualDragging)
                return;

            GhostPopup.HorizontalOffset = screenPoint.X - _dragOffset.X;
            GhostPopup.VerticalOffset = screenPoint.Y - _dragOffset.Y;
        }

        public bool IsOutsideDetachThreshold(Point screenPoint, double threshold = 50)
        {
            if (_sourceWindow == null) return false;
            var rect = new Rect(_sourceWindow.Left, _sourceWindow.Top, _sourceWindow.ActualWidth, _sourceWindow.ActualHeight);
            if (rect.Contains(screenPoint)) return false;
            var distance = CalculateDistanceFromRect(screenPoint, rect);
            var thresh = DragConfig.Instance.DetachThresholdPixels;
            return distance > thresh;
        }

        public void EndManualDrag(bool completed)
        {
            _isManualDragging = false;
            if (_ghostPopup != null)
                _ghostPopup.IsOpen = false;
            Mouse.Capture(null);
            Mouse.OverrideCursor = null;
            if (_sourceElement != null)
                _sourceElement.Opacity = 1.0;

            if (!completed)
            {
                // nothing special to do for now
            }

            _draggedTab = null;
            _sourceWindow = null;
            _sourceElement = null;
        }

        public ITabItem? GetDraggedTab()
        {
            return _draggedTab?.TryGetTarget(out var tab) == true ? tab : null;
        }

        public Window? GetSourceWindow()
        {
            return _sourceWindow;
        }

        public void StartOleDrag()
        {
            if (!(_draggedTab?.TryGetTarget(out var tab) ?? false) || _sourceElement == null)
                return;
            if (_ghostPopup != null) _ghostPopup.IsOpen = false;
            var data = new DataObject("NoteNestTab", tab);
            data.SetData("SourceWindow", _sourceWindow);

            System.Windows.DragDrop.DoDragDrop(_sourceElement, data, DragDropEffects.Move);
            EndManualDrag(true);
        }

        private static double CalculateDistanceFromRect(Point p, Rect r)
        {
            double dx = Math.Max(r.Left - p.X, Math.Max(0, p.X - r.Right));
            double dy = Math.Max(r.Top - p.Y, Math.Max(0, p.Y - r.Bottom));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public void Dispose()
        {
            if (_ghostPopup != null)
            {
                _ghostPopup.IsOpen = false;
                _ghostPopup = null;
            }
        }
    }
}


