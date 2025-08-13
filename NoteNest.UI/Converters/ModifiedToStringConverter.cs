using System;
using System.Globalization;
using System.Windows.Data;

namespace NoteNest.UI.Converters
{
    public class ModifiedToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dt)
            {
                return dt.ToString("g");
            }
            if (value is string s)
            {
                return s;
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


