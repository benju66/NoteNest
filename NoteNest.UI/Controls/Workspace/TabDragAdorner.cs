using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace NoteNest.UI.Controls.Workspace
{
    /// <summary>
    /// Adorner that displays a semi-transparent ghost image of the tab being dragged
    /// Part of Milestone 2B: Drag & Drop
    /// </summary>
    public class TabDragAdorner : Adorner
    {
        private readonly VisualBrush _brush;
        private Point _currentPosition;
        private readonly Size _size;
        
        public TabDragAdorner(UIElement adornedElement, UIElement draggedElement) : base(adornedElement)
        {
            IsHitTestVisible = false; // Don't interfere with drag/drop events
            
            // Create visual brush from the dragged tab's visual appearance
            _brush = new VisualBrush(draggedElement)
            {
                Opacity = 0.7, // Semi-transparent ghost
                Stretch = Stretch.None
            };
            
            // Capture the size of the dragged element
            _size = new Size(draggedElement.RenderSize.Width, draggedElement.RenderSize.Height);
        }
        
        /// <summary>
        /// Update the position where the ghost image should be drawn
        /// </summary>
        public void UpdatePosition(Point position)
        {
            _currentPosition = position;
            
            // Update adorner position
            var adornerLayer = Parent as AdornerLayer;
            adornerLayer?.Update(AdornedElement);
        }
        
        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            
            // Draw the ghost image at current cursor position
            // Offset so the cursor is in the center of the ghost
            var rect = new Rect(
                _currentPosition.X - _size.Width / 2,
                _currentPosition.Y - _size.Height / 2,
                _size.Width,
                _size.Height
            );
            
            // Add subtle shadow for depth
            var shadowRect = rect;
            shadowRect.Offset(2, 2);
            drawingContext.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
                null,
                shadowRect
            );
            
            // Draw the ghost image
            drawingContext.DrawRectangle(_brush, null, rect);
            
            // Optional: Draw border around ghost
            var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF)), 1);
            drawingContext.DrawRectangle(null, borderPen, rect);
        }
    }
}

