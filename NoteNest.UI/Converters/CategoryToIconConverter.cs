using System;
using System.Globalization;
using System.Windows.Data;
using ModernWpf.Controls;

namespace NoteNest.UI.Converters
{
    public class CategoryToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPinned && isPinned)
            {
                return Symbol.Pin;
            }
            return Symbol.Folder;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


