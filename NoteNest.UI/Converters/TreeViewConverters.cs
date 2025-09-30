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
                
                // 📌 MODERNIZED: Use Fluent UI icons as requested
                if (isPinned) return "\uE840"; // Pin icon
                return isExpanded ? "\uE838" : "\uE8B7"; // FolderOpenRegular : FolderRegular
            }
            catch { return "\uE8B7"; } // Default to FolderRegular
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
                // 📄 MODERNIZED: Use DocumentRegular as requested
                return "\uE8A5"; // DocumentRegular - clean, modern document icon
            }
            catch { }
            return "\uE8A5"; // Default to DocumentRegular
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
                // Use a single brush for all note icons
                return System.Windows.Application.Current.FindResource("NoteIconBrush");
            }
            catch { }
            return System.Windows.Application.Current.FindResource("GenericFileIconBrush");
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
                    // 🔧 FIXED: Show placeholder when count is 0, hide when count > 0
                    return count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
            return Visibility.Visible; // Show by default on error
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 💡 NEW: Creates rich tooltips with metadata for tree items
    /// </summary>
    public class TreeItemTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is NoteNest.UI.ViewModels.Categories.CategoryViewModel category)
                {
                    // 📁 Category tooltip with metadata
                    var childCount = category.Children.Count;
                    var noteCount = category.Notes.Count;
                    var totalItems = childCount + noteCount;
                    
                    return $"Folder\n" +
                           $"Items: {totalItems} ({childCount} folders, {noteCount} notes)\n" +
                           $"Path: {category.Path}";
                }
                else if (value is NoteNest.UI.ViewModels.Categories.NoteItemViewModel note)
                {
                    // 📄 Note tooltip with metadata  
                    var modifiedText = note.UpdatedAt.ToString("MMM dd, yyyy HH:mm");
                    return $"Note\n" +
                           $"Modified: {modifiedText}\n" +
                           $"Created: {note.CreatedAt.ToString("MMM dd, yyyy")}";
                }
            }
            catch { }
            
            return null; // No tooltip for unknown types
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


