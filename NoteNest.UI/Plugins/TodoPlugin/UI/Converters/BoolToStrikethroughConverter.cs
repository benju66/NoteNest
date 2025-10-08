using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NoteNest.UI.Plugins.TodoPlugin.UI.Converters
{
    /// <summary>
    /// Converts a boolean value to TextDecorations for strikethrough effect.
    /// </summary>
    public class BoolToStrikethroughConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCompleted && isCompleted)
            {
                return TextDecorations.Strikethrough;
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
