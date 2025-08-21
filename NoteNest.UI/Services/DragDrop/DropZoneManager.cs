using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace NoteNest.UI.Services.DragDrop
{
    public class DropZoneManager
    {
        private readonly Dictionary<string, WeakReference<FrameworkElement>> _dropZones = new();
        private InsertionAdorner? _adorner;
        private AdornerLayer? _adornerLayer;
        private Panel? _currentPanel;
        private int _currentIndex = -1;

        public void RegisterDropZone(string id, FrameworkElement element)
        {
            _dropZones[id] = new WeakReference<FrameworkElement>(element);
        }

        public void ShowInsertionLine(Panel headerPanel, int index)
        {
            if (_currentPanel != headerPanel || _adorner == null)
            {
                HideInsertionLine();
                _adornerLayer = AdornerLayer.GetAdornerLayer(headerPanel);
                if (_adornerLayer == null) return;
                _adorner = new InsertionAdorner(headerPanel);
                _adornerLayer.Add(_adorner);
                _currentPanel = headerPanel;
                _currentIndex = -1;
            }
            if (_currentIndex == index) return;
            _currentIndex = index;

            double offset = 0;
            for (int i = 0; i < Math.Min(index, headerPanel.Children.Count); i++)
            {
                if (headerPanel.Children[i] is FrameworkElement child)
                    offset += child.ActualWidth;
            }
            _adorner.UpdatePosition(offset, true);
        }

        public void HideInsertionLine()
        {
            if (_adorner != null && _adornerLayer != null)
            {
                _adornerLayer.Remove(_adorner);
                _adorner = null;
                _currentPanel = null;
                _currentIndex = -1;
            }
        }

        public DropTarget? GetDropTarget(Point screenPoint)
        {
            foreach (var kvp in _dropZones)
            {
                if (!kvp.Value.TryGetTarget(out var element) || !element.IsVisible)
                    continue;
                var local = element.PointFromScreen(screenPoint);
                if (element.InputHitTest(local) != null)
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
            return null;
        }

        private static int CalculateIndex(FrameworkElement container, Point localPoint)
        {
            if (container is Panel panel)
            {
                double acc = 0;
                for (int i = 0; i < panel.Children.Count; i++)
                {
                    if (panel.Children[i] is FrameworkElement child)
                    {
                        acc += child.ActualWidth;
                        if (localPoint.X < acc)
                            return i;
                    }
                }
                return panel.Children.Count;
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


