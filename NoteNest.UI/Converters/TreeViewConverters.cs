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
            try
            {
                bool isExpanded = values.Length > 0 && values[0] is bool b1 && b1;
                bool isPinned = values.Length > 1 && values[1] is bool b2 && b2;
                if (isPinned) return "\uE840"; // Pinned
                return isExpanded ? "\uE838" : "\uE8B7"; // Open : Closed
            }
            catch { return "\uE8B7"; }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NoteFormatToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
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
            }
            catch { }
            return "\uE7C3";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NoteFormatToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
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
            }
            catch { }
            return Application.Current.FindResource("GenericFileIconBrush");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is int count)
                    return count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


