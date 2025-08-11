using System;
using System.Globalization;
using System.Windows.Data;

namespace NoteNest.UI.Converters
{
    public class LevelToIndentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int level)
            {
                return level * 16; // pixels per level
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


