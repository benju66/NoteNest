using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NoteNest.UI.Converters
{
    public class BoolToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string param)
            {
                var parts = param.Split('|');
                if (parts.Length == 2)
                {
                    var trueValue = parts[0];
                    var falseValue = parts[1];
                    var selectedValue = boolValue ? trueValue : falseValue;
                    
                    if (selectedValue == "0")
                    {
                        return new GridLength(0);
                    }
                    
                    if (double.TryParse(selectedValue, out double length))
                    {
                        return new GridLength(length);
                    }
                    
                    if (selectedValue == "*")
                    {
                        return new GridLength(1, GridUnitType.Star);
                    }
                    
                    if (selectedValue == "Auto")
                    {
                        return new GridLength(0, GridUnitType.Auto);
                    }
                }
            }
            
            return new GridLength(250); // Default fallback
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
