using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Documents;

namespace NoteNest.UI.Controls
{
    public enum NumberingStyle
    {
        Decimal,        // 1, 2, 3
        LowerAlpha,     // a, b, c
        UpperAlpha,     // A, B, C
        LowerRoman,     // i, ii, iii
        UpperRoman,     // I, II, III
        Outline         // 1, 1.1, 1.1.1
    }

    public class OutlineNumbering
    {
        private Dictionary<List, NumberingContext> _contexts = new();
        
        public class NumberingContext
        {
            public NumberingStyle Style { get; set; }
            public Dictionary<int, int> Counters { get; set; } = new();
            
            public string GetNumber(int level, int index)
            {
                Counters[level] = index;
                // Clear deeper levels
                var keysToRemove = Counters.Keys.Where(k => k > level).ToList();
                foreach (var key in keysToRemove)
                    Counters.Remove(key);
                
                if (Style == NumberingStyle.Outline)
                {
                    // Build outline number (1.2.3)
                    var parts = new List<string>();
                    for (int i = 0; i <= level; i++)
                    {
                        parts.Add(Counters.GetValueOrDefault(i, 1).ToString());
                    }
                    return string.Join(".", parts);
                }
                
                return ConvertToStyle(index, GetStyleForLevel(level));
            }
            
            private NumberingStyle GetStyleForLevel(int level)
            {
                return level switch
                {
                    0 => NumberingStyle.Decimal,
                    1 => NumberingStyle.LowerAlpha,
                    2 => NumberingStyle.LowerRoman,
                    _ => NumberingStyle.Decimal
                };
            }
            
            private string ConvertToStyle(int number, NumberingStyle style)
            {
                return style switch
                {
                    NumberingStyle.Decimal => number.ToString(),
                    NumberingStyle.LowerAlpha => ConvertToAlpha(number, false),
                    NumberingStyle.UpperAlpha => ConvertToAlpha(number, true),
                    NumberingStyle.LowerRoman => ConvertToRoman(number, false),
                    NumberingStyle.UpperRoman => ConvertToRoman(number, true),
                    _ => number.ToString()
                };
            }
            
            private string ConvertToAlpha(int number, bool uppercase)
            {
                if (number <= 0) return "";
                
                string result = "";
                number--; // Make it 0-based
                
                while (number >= 0)
                {
                    char letter = (char)('a' + (number % 26));
                    if (uppercase) letter = char.ToUpper(letter);
                    result = letter + result;
                    number = (number / 26) - 1;
                }
                
                return result;
            }
            
            private string ConvertToRoman(int number, bool uppercase)
            {
                if (number <= 0 || number > 3999) return number.ToString();
                
                string[] thousands = { "", "M", "MM", "MMM" };
                string[] hundreds = { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" };
                string[] tens = { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" };
                string[] ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
                
                string result = thousands[number / 1000] +
                               hundreds[(number % 1000) / 100] +
                               tens[(number % 100) / 10] +
                               ones[number % 10];
                               
                return uppercase ? result : result.ToLower();
            }
        }
    }
}
