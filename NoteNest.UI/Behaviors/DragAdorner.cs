using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace NoteNest.UI.Behaviors
{
    // Lightweight adorner that renders a VisualBrush of the dragged item at the current cursor position
    internal sealed class DragAdorner : Adorner
    {
        private readonly VisualBrush _visualBrush;
        private Point _offset;

        public DragAdorner(UIElement adornedElement, Visual adornedVisual) : base(adornedElement)
        {
            _visualBrush = new VisualBrush(adornedVisual)
            {
                Opacity = 0.7,
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };
            IsHitTestVisible = false;
        }

        public void UpdatePosition(Point offset)
        {
            _offset = offset;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var size = (AdornedElement as FrameworkElement);
            var width = size?.ActualWidth ?? 200;
            var height = 24.0;
            var rect = new Rect(_offset.X + 8, _offset.Y + 8, width, height);
            dc.DrawRectangle(_visualBrush, null, rect);
        }
    }
}


