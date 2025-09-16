using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Controls;

namespace NoteNest.UI.Controls.Editor.RTF.Features
{
    /// <summary>
    /// Highlight feature for RTF editor
    /// Single Responsibility: Text highlighting with color cycling
    /// Clean, focused feature module following SRP
    /// </summary>
    public class HighlightFeature
    {
        private readonly Color[] _colors = 
        {
            Colors.Yellow,
            Colors.LightGreen,
            Colors.LightBlue,
            Colors.LightPink,
            Colors.Transparent // Remove highlight
        };
        
        private int _currentIndex = 0;
        
        /// <summary>
        /// Cycle through highlight colors for selected text
        /// </summary>
        public void CycleHighlight(RichTextBox editor)
        {
            if (editor?.Selection?.IsEmpty != false) return;
            
            try
            {
                _currentIndex = (_currentIndex + 1) % _colors.Length;
                var color = _colors[_currentIndex];
                
                // Apply or remove highlight
                if (color == Colors.Transparent)
                {
                    editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);
                }
                else
                {
                    editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(color));
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HighlightFeature] Highlight application failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get current highlight color for UI display
        /// </summary>
        public Color CurrentColor => _colors[_currentIndex];
        
        /// <summary>
        /// Get current highlight color name for accessibility
        /// </summary>
        public string CurrentColorName => _colors[_currentIndex] switch
        {
            var c when c == Colors.Yellow => "Yellow",
            var c when c == Colors.LightGreen => "Light Green",
            var c when c == Colors.LightBlue => "Light Blue", 
            var c when c == Colors.LightPink => "Light Pink",
            var c when c == Colors.Transparent => "No Highlight",
            _ => "Unknown"
        };
        
        /// <summary>
        /// Reset highlight cycle to beginning
        /// </summary>
        public void Reset()
        {
            _currentIndex = 0;
        }
    }
}
