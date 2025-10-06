using System;
using System.Globalization;
using System.Windows.Data;

namespace NoteNest.UI.Converters
{
    /// <summary>
    /// Converts category properties to icon glyphs (Segoe MDL2 Assets)
    /// Note: This converter is legacy - consider using Lucide icons instead
    /// </summary>
    public class CategoryToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPinned && isPinned)
            {
                // Pin icon from Segoe MDL2 Assets
                return "\uE718"; // Pin glyph
            }
            // Folder icon from Segoe MDL2 Assets
            return "\uE8B7"; // Folder glyph
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


