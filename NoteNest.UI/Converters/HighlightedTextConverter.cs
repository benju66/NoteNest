using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace NoteNest.UI.Converters
{
    /// <summary>
    /// Converts text with highlight markers (◆◆term◆◆) into a TextBlock with proper bold formatting
    /// </summary>
    public class HighlightedTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string text || string.IsNullOrEmpty(text))
                return new TextBlock { Text = "" };

            var textBlock = new TextBlock
            {
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.Wrap
            };

            try
            {
                // Split text by highlight markers
                var parts = Regex.Split(text, @"◆◆(.*?)◆◆", RegexOptions.IgnoreCase);
                
                for (int i = 0; i < parts.Length; i++)
                {
                    if (string.IsNullOrEmpty(parts[i])) continue;
                    
                    if (i % 2 == 0)
                    {
                        // Regular text
                        textBlock.Inlines.Add(new Run(parts[i]));
                    }
                    else
                    {
                        // Highlighted text (bold and colored)
                        textBlock.Inlines.Add(new Run(parts[i])
                        {
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Colors.DarkBlue)
                        });
                    }
                }
                
                return textBlock;
            }
            catch
            {
                // Fallback to plain text if highlighting fails
                return new TextBlock { Text = text.Replace("◆◆", "") };
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
