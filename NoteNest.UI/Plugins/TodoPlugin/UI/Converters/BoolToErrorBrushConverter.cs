using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace NoteNest.UI.Plugins.TodoPlugin.UI.Converters
{
    /// <summary>
    /// Converts a boolean value to an error brush (red) or default foreground brush.
    /// </summary>
    public class BoolToErrorBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isError && isError)
            {
                // Return error brush (red)
                return System.Windows.Application.Current.TryFindResource("SystemControlErrorTextForegroundBrush") 
                    ?? new SolidColorBrush(Colors.Red);
            }
            
            // Return default foreground brush
            return System.Windows.Application.Current.TryFindResource("SystemControlForegroundBaseHighBrush") 
                ?? new SolidColorBrush(Colors.Black);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
