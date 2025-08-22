using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using NoteNest.UI.Utils;

namespace NoteNest.UI.Services.DragDrop
{
    public class DropZoneManager
    {
        private readonly Dictionary<string, WeakReference<FrameworkElement>> _dropZones = new();
        private InsertionAdorner? _adorner;
        private AdornerLayer? _adornerLayer;
        private Panel? _currentPanel;
        private int _currentIndex = -1;
        private bool _pendingUpdate = false;
        private readonly object _updateLock = new object();

        public void RegisterDropZone(string id, FrameworkElement element)
        {
            _dropZones[id] = new WeakReference<FrameworkElement>(element);
        }

        public void ShowInsertionLine(Panel headerPanel, int index)
        {
            // Quick exit if no change needed
            if (_currentPanel == headerPanel && _currentIndex == index)
                return;

            // Prevent update flooding
            lock (_updateLock)
            {
                if (_pendingUpdate)
                    return;
                _pendingUpdate = true;
            }

            // Batch the update on the UI thread at render priority
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    UpdateInsertionLineImmediate(headerPanel, index);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error updating insertion line: {ex.Message}");
                }
                finally
                {
                    lock (_updateLock)
                    {
                        _pendingUpdate = false;
                    }
                }
            }, DispatcherPriority.Render);
        }

        private void UpdateInsertionLineImmediate(Panel headerPanel, int index)
        {
            // Remove old adorner if panel changed
            if (_currentPanel != headerPanel || _adorner == null)
            {
                HideInsertionLineImmediate();

                if (headerPanel != null)
                {
                    _adornerLayer = AdornerLayer.GetAdornerLayer(headerPanel);
                    if (_adornerLayer != null)
                    {
                        _adorner = new InsertionAdorner(headerPanel);
                        _adornerLayer.Add(_adorner);
                        _currentPanel = headerPanel;
                    }
                }
            }

            // Update position if we have a valid adorner
            if (_adorner != null && headerPanel != null)
            {
                _currentIndex = index;

                double offset = 0;
                int validIndex = Math.Max(0, Math.Min(index, headerPanel.Children.Count));

                for (int i = 0; i < validIndex; i++)
                {
                    if (headerPanel.Children[i] is FrameworkElement child && child.IsVisible)
                    {
                        offset += child.ActualWidth;
                    }
                }

                _adorner.UpdatePosition(offset, true);
            }
        }

        public void HideInsertionLine()
        {
            // Cancel any pending updates
            lock (_updateLock)
            {
                if (_pendingUpdate)
                {
                    _pendingUpdate = false;
                }
            }

            // Schedule immediate hide on UI thread
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                HideInsertionLineImmediate();
            }, DispatcherPriority.Send);
        }

        private void HideInsertionLineImmediate()
        {
            if (_adorner != null && _adornerLayer != null)
            {
                try
                {
                    _adornerLayer.Remove(_adorner);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error removing adorner: {ex.Message}");
                }

                _adorner = null;
                _adornerLayer = null;
            }

            _currentPanel = null;
            _currentIndex = -1;
        }

        public DropTarget? GetDropTarget(Point screenPoint)
        {
            foreach (var kvp in _dropZones.ToList())
            {
                if (!kvp.Value.TryGetTarget(out var element) || !element.IsVisible)
                    continue;

                try
                {
                    var local = element.PointFromScreen(screenPoint);

                    // Better bounds checking
                    var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
                    if (bounds.Contains(local))
                    {
                        return new DropTarget
                        {
                            ZoneId = kvp.Key,
                            Element = element,
                            LocalPosition = local,
                            TabIndex = CalculateIndex(element, local)
                        };
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in hit testing for {kvp.Key}: {ex.Message}");
                    continue;
                }
            }
            return null;
        }

        public void Dispose()
        {
            HideInsertionLineImmediate();
            _dropZones.Clear();
        }

        private static int CalculateIndex(FrameworkElement container, Point localPoint)
        {
            if (container is Panel panel)
            {
                return TabIndexCalculator.CalculateInsertionIndex(panel, localPoint);
            }
            return 0;
        }
    }

    public class DropTarget
    {
        public string ZoneId { get; set; }
        public FrameworkElement Element { get; set; }
        public Point LocalPosition { get; set; }
        public int TabIndex { get; set; }
    }
}


