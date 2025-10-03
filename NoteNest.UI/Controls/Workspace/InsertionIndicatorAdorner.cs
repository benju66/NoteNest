using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace NoteNest.UI.Controls.Workspace
{
    /// <summary>
    /// Adorner that displays a blue insertion line indicating where a dragged tab will be dropped
    /// Part of Milestone 2B: Drag & Drop
    /// </summary>
    public class InsertionIndicatorAdorner : Adorner
    {
        private Rect _insertionBounds;
        private readonly Brush _lineBrush;
        private readonly Pen _linePen;
        
        public InsertionIndicatorAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false; // Don't interfere with drag/drop events
            
            // Blue insertion line (matches active pane border color)
            _lineBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF)); // DodgerBlue
            _linePen = new Pen(_lineBrush, 2);
            _linePen.Freeze();
        }
        
        /// <summary>
        /// Update the position where the insertion line should be drawn
        /// </summary>
        public void UpdateInsertionPoint(Rect bounds)
        {
            if (_insertionBounds != bounds)
            {
                _insertionBounds = bounds;
                InvalidateVisual(); // Trigger redraw
            }
        }
        
        /// <summary>
        /// Hide the insertion indicator
        /// </summary>
        public void Hide()
        {
            UpdateInsertionPoint(Rect.Empty);
        }
        
        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            
            if (_insertionBounds.IsEmpty)
                return;
            
            // Draw vertical line at insertion point
            var startPoint = new Point(_insertionBounds.X, _insertionBounds.Y);
            var endPoint = new Point(_insertionBounds.X, _insertionBounds.Bottom);
            
            drawingContext.DrawLine(_linePen, startPoint, endPoint);
            
            // Optional: Draw small triangles at top and bottom for better visibility
            DrawTriangle(drawingContext, startPoint, pointing: true);
            DrawTriangle(drawingContext, endPoint, pointing: false);
        }
        
        private void DrawTriangle(DrawingContext drawingContext, Point center, bool pointing)
        {
            const double size = 4;
            
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                if (pointing) // Pointing down (at top of line)
                {
                    ctx.BeginFigure(new Point(center.X - size, center.Y), true, true);
                    ctx.LineTo(new Point(center.X + size, center.Y), true, false);
                    ctx.LineTo(new Point(center.X, center.Y + size), true, false);
                }
                else // Pointing up (at bottom of line)
                {
                    ctx.BeginFigure(new Point(center.X - size, center.Y), true, true);
                    ctx.LineTo(new Point(center.X + size, center.Y), true, false);
                    ctx.LineTo(new Point(center.X, center.Y - size), true, false);
                }
            }
            
            geometry.Freeze();
            drawingContext.DrawGeometry(_lineBrush, null, geometry);
        }
    }
}

