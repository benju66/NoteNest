using System;
using System.Globalization;
using System.Windows.Data;

namespace NoteNest.UI.Converters
{
    /// <summary>
    /// Converts an object to its type name string for DataTrigger binding
    /// </summary>
    public class TypeNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            
            // Return just the type name without namespace
            return value.GetType().Name;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("TypeNameConverter is one-way only");
        }
    }
}
