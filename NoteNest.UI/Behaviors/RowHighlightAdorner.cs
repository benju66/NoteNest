using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace NoteNest.UI.Behaviors
{
    internal sealed class RowHighlightAdorner : Adorner
    {
        private readonly Brush _brush;

        public RowHighlightAdorner(UIElement adornedElement, Brush brush) : base(adornedElement)
        {
            _brush = brush;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var fe = AdornedElement as FrameworkElement;
            if (fe == null) return;
            var rect = new Rect(0, 0, fe.ActualWidth, fe.ActualHeight);
            dc.DrawRectangle(_brush, null, rect);
        }
    }
}


