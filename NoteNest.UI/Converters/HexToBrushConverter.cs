using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace NoteNest.UI.Converters
{
    public class HexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var hex = value as string;
                if (string.IsNullOrWhiteSpace(hex)) return null;
                if (!hex.StartsWith("#")) hex = "#" + hex.Trim();
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


