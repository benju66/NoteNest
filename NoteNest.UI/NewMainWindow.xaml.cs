using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NoteNest.UI
{
    public partial class NewMainWindow : Window
    {
        public NewMainWindow()
        {
            InitializeComponent();
        }
    }

    public class BoolToTextConverter : IValueConverter
    {
        public static readonly BoolToTextConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)(value ?? false) ? "Loading..." : "Ready";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
