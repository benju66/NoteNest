using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using NoteNest.UI.Utils;
using NoteNest.UI.Config;

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
        private DateTime _lastCleanup = DateTime.MinValue;
        private readonly object _cleanupLock = new object();
        private int _registrationCount = 0;

        public void RegisterDropZone(string id, FrameworkElement element)
        {
            if (string.IsNullOrEmpty(id) || element == null)
                return;

            _dropZones[id] = new WeakReference<FrameworkElement>(element);

            Interlocked.Increment(ref _registrationCount);

            var now = DateTime.UtcNow;
            var shouldCleanup = false;

            lock (_cleanupLock)
            {
                var interval = DragConfig.Instance.CacheCleanupInterval;
                if (now - _lastCleanup > interval || _registrationCount > 50)
                {
                    shouldCleanup = true;
                    _lastCleanup = now;
                    _registrationCount = 0;
                }
            }

            if (shouldCleanup)
            {
                Task.Run(CleanupStaleReferences);
            }
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
            var zonesToCheck = _dropZones.ToList();
            var deadZones = new List<string>();

            foreach (var kvp in zonesToCheck)
            {
                if (!kvp.Value.TryGetTarget(out var element) || element == null || !element.IsVisible)
                {
                    deadZones.Add(kvp.Key);
                    continue;
                }

                try
                {
                    var local = element.PointFromScreen(screenPoint);
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
                    deadZones.Add(kvp.Key);
                }
            }

            if (deadZones.Count > 0)
            {
                foreach (var deadKey in deadZones)
                {
                    _dropZones.Remove(deadKey);
                }

                if (deadZones.Count > 5)
                {
                    System.Diagnostics.Debug.WriteLine($"Removed {deadZones.Count} dead zones during hit testing");
                }
            }

            return null;
        }

        private void CleanupStaleReferences()
        {
            try
            {
                var staleKeys = new List<string>();
                var validCount = 0;

                foreach (var kvp in _dropZones.ToList())
                {
                    if (!kvp.Value.TryGetTarget(out var element))
                    {
                        staleKeys.Add(kvp.Key);
                    }
                    else if (element != null)
                    {
                        bool isValid = false;
                        try
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                isValid = PresentationSource.FromVisual(element) != null && element.IsLoaded;
                            });
                        }
                        catch { isValid = false; }

                        if (isValid) validCount++; else staleKeys.Add(kvp.Key);
                    }
                }

                if (staleKeys.Count > 0)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var key in staleKeys)
                        {
                            _dropZones.Remove(key);
                        }
                    });
                }

                System.Diagnostics.Debug.WriteLine($"DropZone cleanup: Removed {staleKeys.Count} stale references, {validCount} valid zones remaining");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during drop zone cleanup: {ex.Message}");
            }
        }

        public void ForceCleanup()
        {
            lock (_cleanupLock)
            {
                _lastCleanup = DateTime.MinValue;
            }
            CleanupStaleReferences();
        }

        public (int total, int valid, int stale) GetDropZoneStatistics()
        {
            try
            {
                int total = _dropZones.Count;
                int valid = 0;
                int stale = 0;

                foreach (var kvp in _dropZones.ToList())
                {
                    if (kvp.Value.TryGetTarget(out var element) && element != null)
                    {
                        try
                        {
                            bool isValid = false;
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                isValid = PresentationSource.FromVisual(element) != null;
                            });
                            if (isValid) valid++; else stale++;
                        }
                        catch { stale++; }
                    }
                    else
                    {
                        stale++;
                    }
                }

                return (total, valid, stale);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting drop zone statistics: {ex.Message}");
                return (_dropZones.Count, 0, 0);
            }
        }

        public void Dispose()
        {
            try
            {
                HideInsertionLineImmediate();
                lock (_cleanupLock)
                {
                    _dropZones.Clear();
                    _lastCleanup = DateTime.MinValue;
                    _registrationCount = 0;
                }
                System.Diagnostics.Debug.WriteLine("DropZoneManager disposed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing DropZoneManager: {ex.Message}");
            }
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


