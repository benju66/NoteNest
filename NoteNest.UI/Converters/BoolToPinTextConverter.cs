using System;
using System.Globalization;
using System.Windows.Data;

namespace NoteNest.UI.Converters
{
    /// <summary>
    /// Converts a boolean pinned state to appropriate menu text
    /// </summary>
    public class BoolToPinTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPinned)
            {
                return isPinned ? "Unpin" : "Pin";
            }
            return "Pin";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("BoolToPinTextConverter is one-way only");
        }
    }
}
