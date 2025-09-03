using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace NoteNest.UI.Behaviors
{
    internal enum InsertionPlacement { Top, Bottom }

    internal sealed class InsertionAdorner : Adorner
    {
        private readonly InsertionPlacement _placement;
        private readonly Pen _pen;

        public InsertionAdorner(UIElement adornedElement, InsertionPlacement placement, Brush brush, double thickness = 2.0)
            : base(adornedElement)
        {
            _placement = placement;
            _pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var fe = AdornedElement as FrameworkElement;
            if (fe == null) return;
            var y = _placement == InsertionPlacement.Top ? 0.5 : fe.ActualHeight - 0.5;
            var start = new Point(0, y);
            var end = new Point(fe.ActualWidth, y);
            dc.DrawLine(_pen, start, end);
        }
    }
}


