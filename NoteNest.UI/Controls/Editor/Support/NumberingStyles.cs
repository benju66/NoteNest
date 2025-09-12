using System;
using System.Collections.Generic;
using System.Text;

namespace NoteNest.UI.Controls.Editor.Support
{
    public enum NumberingStyle
    {
        Decimal,        // 1, 2, 3
        LowerAlpha,     // a, b, c
        UpperAlpha,     // A, B, C
        LowerRoman,     // i, ii, iii
        UpperRoman,     // I, II, III
        Outline         // 1.1, 1.1.1
    }

    public static class NumberingFormatter
    {
        // Roman numeral mappings
        private static readonly List<(int Value, string Numeral)> RomanNumerals = new()
        {
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        };

        public static string FormatNumber(int number, NumberingStyle style)
        {
            if (number <= 0) return "0";

            return style switch
            {
                NumberingStyle.Decimal => number.ToString(),
                NumberingStyle.LowerAlpha => ConvertToAlpha(number, false),
                NumberingStyle.UpperAlpha => ConvertToAlpha(number, true),
                NumberingStyle.LowerRoman => ConvertToRoman(number).ToLower(),
                NumberingStyle.UpperRoman => ConvertToRoman(number),
                NumberingStyle.Outline => number.ToString(), // Handled separately
                _ => number.ToString()
            };
        }

        private static string ConvertToAlpha(int number, bool uppercase)
        {
            if (number <= 0) return "";
            
            var result = new StringBuilder();
            number--; // Make it 0-based
            
            while (number >= 0)
            {
                int remainder = number % 26;
                char letter = (char)('a' + remainder);
                result.Insert(0, uppercase ? char.ToUpper(letter) : letter);
                number = (number / 26) - 1;
                
                if (number < 0) break;
            }
            
            return result.ToString();
        }

        private static string ConvertToRoman(int number)
        {
            if (number <= 0 || number > 3999) return number.ToString();
            
            var result = new StringBuilder();
            
            foreach (var (value, numeral) in RomanNumerals)
            {
                int count = number / value;
                if (count > 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        result.Append(numeral);
                    }
                    number -= value * count;
                }
            }
            
            return result.ToString();
        }

        public static string GetMarkerSuffix(NumberingStyle style)
        {
            return style switch
            {
                NumberingStyle.LowerAlpha or NumberingStyle.UpperAlpha => ")",
                NumberingStyle.LowerRoman or NumberingStyle.UpperRoman => ".",
                _ => "."
            };
        }
    }

    public class NumberingScheme
    {
        public string Name { get; set; }
        public Dictionary<int, NumberingStyle> LevelStyles { get; set; }
        
        public static NumberingScheme Default => new()
        {
            Name = "Default",
            LevelStyles = new Dictionary<int, NumberingStyle>
            {
                { 0, NumberingStyle.Decimal },
                { 1, NumberingStyle.LowerAlpha },
                { 2, NumberingStyle.LowerRoman },
                { 3, NumberingStyle.Decimal },
                { 4, NumberingStyle.LowerAlpha }
            }
        };

        public static NumberingScheme Legal => new()
        {
            Name = "Legal",
            LevelStyles = new Dictionary<int, NumberingStyle>
            {
                { 0, NumberingStyle.Decimal },
                { 1, NumberingStyle.Decimal },
                { 2, NumberingStyle.Decimal },
                { 3, NumberingStyle.Decimal },
                { 4, NumberingStyle.Decimal }
            }
        };

        public static NumberingScheme Academic => new()
        {
            Name = "Academic",
            LevelStyles = new Dictionary<int, NumberingStyle>
            {
                { 0, NumberingStyle.UpperRoman },
                { 1, NumberingStyle.UpperAlpha },
                { 2, NumberingStyle.Decimal },
                { 3, NumberingStyle.LowerAlpha },
                { 4, NumberingStyle.LowerRoman }
            }
        };

        public NumberingStyle GetStyleForLevel(int level)
        {
            if (LevelStyles.TryGetValue(level, out var style))
                return style;
            
            // Default fallback pattern
            return (level % 3) switch
            {
                0 => NumberingStyle.Decimal,
                1 => NumberingStyle.LowerAlpha,
                _ => NumberingStyle.LowerRoman
            };
        }
    }
}
