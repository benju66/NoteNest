using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using NoteNest.UI.Config;

namespace NoteNest.UI.Services.DragDrop
{
    public class InsertionAdorner : Adorner
    {
        private readonly Pen _pen;
        private Point _start;
        private Point _end;

        public InsertionAdorner(UIElement adornedElement) : base(adornedElement)
        {
            _pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 122, 204)), DragConfig.Instance.InsertionLineThickness);
            IsHitTestVisible = false;
        }

        public void UpdatePosition(double offset, bool vertical)
        {
            if (vertical)
            {
                _start = new Point(offset, 0);
                _end = new Point(offset, AdornedElement.RenderSize.Height);
            }
            else
            {
                _start = new Point(0, offset);
                _end = new Point(AdornedElement.RenderSize.Width, offset);
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.DrawLine(_pen, _start, _end);
        }
    }
}


