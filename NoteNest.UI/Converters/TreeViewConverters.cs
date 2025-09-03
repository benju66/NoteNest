using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using NoteNest.Core.Models;

namespace NoteNest.UI.Converters
{
    public class FolderStateToIconConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is bool isExpanded && values[1] is bool isPinned)
            {
                if (isPinned) return "\uE840";
                return isExpanded ? "\uE838" : "\uE8B7";
            }
            return "\uE8B7";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class NoteFormatToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is NoteFormat format)
            {
                return format switch
                {
                    NoteFormat.Markdown => "\uE943",
                    NoteFormat.PlainText => "\uE8A4",
                    _ => "\uE7C3"
                };
            }
            return "\uE7C3";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class NoteFormatToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is NoteFormat format)
            {
                return format switch
                {
                    NoteFormat.Markdown => Application.Current.FindResource("MarkdownIconBrush"),
                    NoteFormat.PlainText => Application.Current.FindResource("TextFileIconBrush"),
                    _ => Application.Current.FindResource("GenericFileIconBrush")
                };
            }
            return Application.Current.FindResource("GenericFileIconBrush");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
                return count > 0 ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}


