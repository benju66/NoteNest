using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace NoteNest.UI.Converters
{
    /// <summary>
    /// Converts IsActive bool to border brush (highlight active pane)
    /// </summary>
    public class ActivePaneBorderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isActive && isActive)
            {
                // Active pane gets blue border
                return new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF)); // DodgerBlue
            }
            
            // Inactive pane gets transparent border
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
