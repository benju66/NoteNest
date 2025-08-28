using System;
using System.Globalization;
using System.Windows.Data;
using NoteNest.UI.Interfaces;

namespace NoteNest.UI.Converters
{
	public class EditorViewModeToBoolConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is EditorViewMode mode)
			{
				return mode == EditorViewMode.RichText;
			}
			return false;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			var isChecked = value as bool? ?? false;
			return isChecked ? EditorViewMode.RichText : EditorViewMode.PlainText;
		}
	}
}
