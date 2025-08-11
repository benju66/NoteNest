using System;
using System.Globalization;
using System.Windows.Data;

namespace NoteNest.UI.Converters
{
    public class CompositeCollectionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value; // Children already composed in ViewModel
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


