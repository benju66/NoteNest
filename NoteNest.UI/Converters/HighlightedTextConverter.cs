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
                // Handle FTS5 format (<mark>term</mark>) from search results
                if (text.Contains("<mark>"))
                {
                    var parts = Regex.Split(text, @"<mark>(.*?)</mark>", RegexOptions.IgnoreCase);
                    var matches = Regex.Matches(text, @"<mark>(.*?)</mark>", RegexOptions.IgnoreCase);
                    
                    int matchIndex = 0;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(parts[i]))
                        {
                            // Regular text
                            textBlock.Inlines.Add(new Run(parts[i]));
                        }
                        
                        // Add highlighted match if available
                        if (matchIndex < matches.Count && i < parts.Length - 1)
                        {
                            var highlightedText = matches[matchIndex].Groups[1].Value;
                            textBlock.Inlines.Add(new Run(highlightedText)
                            {
                                FontWeight = FontWeights.Bold,
                                Foreground = new SolidColorBrush(Colors.DarkBlue),
                                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 0)) // Light yellow background
                            });
                            matchIndex++;
                        }
                    }
                }
                else
                {
                    // Handle legacy format (◆◆term◆◆) or plain text
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
                }
                
                return textBlock;
            }
            catch (Exception ex)
            {
                // Fallback to plain text if highlighting fails
                return new TextBlock { Text = text.Replace("<mark>", "").Replace("</mark>", "").Replace("◆◆", "") };
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
