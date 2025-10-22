using System;
using System.Globalization;
using System.Windows.Data;

namespace NoteNest.UI.Plugins.TodoPlugin.UI.Converters
{
    /// <summary>
    /// Converts a boolean value to opacity for visual feedback.
    /// Used to fade completed todo items (0.35 for completed, 1.0 for active).
    /// </summary>
    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCompleted && isCompleted)
            {
                return 0.35; // Faded for completed (more pronounced than typical 0.5)
            }
            return 1.0; // Full opacity for active tasks
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

