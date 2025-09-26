using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NoteNest.UI
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public static readonly BoolToVisibilityConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)(value ?? false) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (Visibility)(value ?? Visibility.Collapsed) == Visibility.Visible;
        }
    }

    public class CountToVisibilityConverter : IValueConverter
    {
        public static readonly CountToVisibilityConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var count = (int)(value ?? 0);
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}