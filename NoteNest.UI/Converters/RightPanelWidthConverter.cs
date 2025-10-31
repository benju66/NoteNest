using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NoteNest.UI.Converters
{
    /// <summary>
    /// Converts IsRightPanelVisible (bool) + RightPanelWidth (double) to GridLength
    /// Enables resizable right panel that remembers width during session
    /// When visible: Returns GridLength(width) - allows user resizing
    /// When hidden: Returns GridLength(0) - collapses panel
    /// </summary>
    public class RightPanelWidthConverter : IMultiValueConverter
    {
        /// <summary>
        /// Convert from ViewModel properties to GridLength
        /// Called when: IsRightPanelVisible or RightPanelWidth changes
        /// </summary>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                // Handle WPF initialization - bindings may not be ready yet
                if (values == null || values.Length < 2 || 
                    values[0] == DependencyProperty.UnsetValue || 
                    values[1] == DependencyProperty.UnsetValue)
                {
                    return new GridLength(0); // Safe default - panel hidden
                }
                
                // Extract values
                bool isVisible = values[0] is bool b && b;
                double width = values[1] is double d ? d : 300.0;
                
                // Safety: Clamp to reasonable bounds (belt + suspenders with MinWidth/MaxWidth)
                width = Math.Max(150, Math.Min(800, width));
                
                // Return result
                var result = isVisible ? new GridLength(width) : new GridLength(0);
                
                System.Diagnostics.Debug.WriteLine($"[RightPanelWidthConverter] Convert: Visible={isVisible}, Width={width}px → GridLength({result.Value})");
                
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RightPanelWidthConverter] Convert ERROR: {ex.Message}");
                return new GridLength(0); // Safe fallback - hide panel on error
            }
        }

        /// <summary>
        /// Convert back from GridLength to ViewModel properties
        /// Called when: User drags GridSplitter (changes column width)
        /// </summary>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is GridLength gridLength)
                {
                    double newWidth = gridLength.Value;
                    
                    // Safety: Clamp to reasonable bounds
                    newWidth = Math.Max(150, Math.Min(800, newWidth));
                    
                    System.Diagnostics.Debug.WriteLine($"[RightPanelWidthConverter] ConvertBack: GridLength({gridLength.Value}) → Width={newWidth}px (Visibility unchanged)");
                    
                    // CRITICAL DESIGN DECISION:
                    // Return array: [IsRightPanelVisible, RightPanelWidth]
                    // - Use Binding.DoNothing for visibility (only Alt+T should toggle, not user dragging)
                    // - Update width from user's drag operation
                    return new object[] { Binding.DoNothing, newWidth };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RightPanelWidthConverter] ConvertBack ERROR: {ex.Message}");
            }
            
            // On error, don't update anything
            return new object[] { Binding.DoNothing, Binding.DoNothing };
        }
    }
}

