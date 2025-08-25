using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using NoteNest.UI.Plugins.Todo.Models;

namespace NoteNest.UI.Plugins.Todo.UI.Converters
{
	public class BoolToStrikethroughConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is bool isCompleted && isCompleted)
			{
				return TextDecorations.Strikethrough;
			}
			return null;
		}
		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
	}

	public class PriorityToBrushConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is TodoPriority p)
			{
				return p switch
				{
					TodoPriority.Urgent => new SolidColorBrush(Color.FromRgb(255, 68, 68)),
					TodoPriority.High => new SolidColorBrush(Color.FromRgb(255, 165, 0)),
					TodoPriority.Normal => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
					TodoPriority.Low => new SolidColorBrush(Color.FromRgb(128, 128, 128)),
					_ => new SolidColorBrush(Colors.Transparent)
				};
			}
			return new SolidColorBrush(Colors.Transparent);
		}
		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
	}

	public class NullToCollapsedConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value == null ? Visibility.Collapsed : Visibility.Visible;
		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
	}
}


