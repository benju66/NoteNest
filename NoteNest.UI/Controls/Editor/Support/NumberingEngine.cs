using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Documents;

namespace NoteNest.UI.Controls.Editor.Support
{
    public class NumberingEngine
    {
        private readonly Dictionary<List, NumberingContext> _contexts = new();
        private NumberingScheme _currentScheme = NumberingScheme.Default;
        
        public class NumberingContext
        {
            public Dictionary<int, int> LevelCounters { get; } = new();
            public NumberingScheme Scheme { get; set; }
            public bool IsOutlineMode { get; set; }
            public string LastAppliedNumber { get; set; }
            
            public void ResetLevel(int level)
            {
                // Reset this level and all deeper levels
                var keysToRemove = LevelCounters.Keys.Where(k => k >= level).ToList();
                foreach (var key in keysToRemove)
                {
                    LevelCounters.Remove(key);
                }
            }
            
            public void IncrementLevel(int level)
            {
                if (!LevelCounters.ContainsKey(level))
                    LevelCounters[level] = 0;
                
                LevelCounters[level]++;
                
                // Reset deeper levels
                var keysToRemove = LevelCounters.Keys.Where(k => k > level).ToList();
                foreach (var key in keysToRemove)
                {
                    LevelCounters.Remove(key);
                }
            }
            
            public string GetFormattedNumber(int level)
            {
                if (IsOutlineMode)
                {
                    return BuildOutlineNumber(level);
                }
                
                if (!LevelCounters.ContainsKey(level))
                    return "1";
                
                var style = Scheme.GetStyleForLevel(level);
                var number = LevelCounters[level];
                var formatted = NumberingFormatter.FormatNumber(number, style);
                var suffix = NumberingFormatter.GetMarkerSuffix(style);
                
                return formatted + suffix;
            }
            
            private string BuildOutlineNumber(int level)
            {
                var parts = new List<string>();
                
                for (int i = 0; i <= level; i++)
                {
                    if (LevelCounters.ContainsKey(i))
                    {
                        parts.Add(LevelCounters[i].ToString());
                    }
                    else
                    {
                        parts.Add("1");
                    }
                }
                
                return string.Join(".", parts) + ".";
            }
        }

        public void SetNumberingScheme(NumberingScheme scheme)
        {
            _currentScheme = scheme ?? NumberingScheme.Default;
        }

        public void UpdateListNumbering(FlowDocument document)
        {
            if (document == null) return;
            
            try
            {
                // Clear contexts for fresh numbering
                _contexts.Clear();
                
                // Process all blocks in document
                foreach (var block in document.Blocks)
                {
                    ProcessBlock(block, null, 0);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] UpdateListNumbering failed: {ex.Message}");
            }
        }

        private void ProcessBlock(Block block, NumberingContext parentContext, int level)
        {
            if (block is List list && list.MarkerStyle == TextMarkerStyle.Decimal)
            {
                // Get or create context for this list
                if (!_contexts.TryGetValue(list, out var context))
                {
                    context = new NumberingContext
                    {
                        Scheme = _currentScheme,
                        IsOutlineMode = _currentScheme.Name == "Legal"
                    };
                    _contexts[list] = context;
                    
                    // Inherit from parent context if exists
                    if (parentContext != null && context.IsOutlineMode)
                    {
                        foreach (var kvp in parentContext.LevelCounters)
                        {
                            if (kvp.Key < level)
                            {
                                context.LevelCounters[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }
                
                // Process each list item
                int itemIndex = 0;
                foreach (ListItem item in list.ListItems)
                {
                    ProcessListItem(item, context, level, itemIndex++);
                }
            }
            else if (block is Section section)
            {
                foreach (var child in section.Blocks)
                {
                    ProcessBlock(child, parentContext, level);
                }
            }
        }

        private void ProcessListItem(ListItem item, NumberingContext context, int level, int index)
        {
            if (item == null || context == null) return;
            
            try
            {
                // Increment counter for this level
                context.IncrementLevel(level);
                
                // Get formatted number
                var formattedNumber = context.GetFormattedNumber(level);
                
                // Apply number to the list item (via marker offset or custom rendering)
                ApplyNumberToListItem(item, formattedNumber, level);
                
                // Store for reference
                context.LastAppliedNumber = formattedNumber;
                
                // Process nested lists
                foreach (var block in item.Blocks)
                {
                    if (block is List nestedList)
                    {
                        ProcessBlock(nestedList, context, level + 1);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] ProcessListItem failed: {ex.Message}");
            }
        }

        private void ApplyNumberToListItem(ListItem item, string number, int level)
        {
            // Store the number as attached property for custom rendering
            item.SetValue(ListItemNumberProperty, number);
            
            // Log for debugging
            Debug.WriteLine($"[NUMBERING] Level {level}: {number}");
        }

        // Attached property to store custom numbering
        public static readonly System.Windows.DependencyProperty ListItemNumberProperty =
            System.Windows.DependencyProperty.RegisterAttached(
                "ListItemNumber",
                typeof(string),
                typeof(NumberingEngine),
                new System.Windows.PropertyMetadata(string.Empty));

        public static void SetListItemNumber(ListItem item, string value)
        {
            item.SetValue(ListItemNumberProperty, value);
        }

        public static string GetListItemNumber(ListItem item)
        {
            return (string)item.GetValue(ListItemNumberProperty);
        }

        // Utility method to find all numbered lists
        public List<List> FindNumberedLists(FlowDocument document)
        {
            var lists = new List<List>();
            
            if (document == null) return lists;
            
            foreach (var block in document.Blocks)
            {
                FindNumberedListsInBlock(block, lists);
            }
            
            return lists;
        }

        private void FindNumberedListsInBlock(Block block, List<List> lists)
        {
            if (block is List list && list.MarkerStyle == TextMarkerStyle.Decimal)
            {
                lists.Add(list);
                
                // Check nested lists
                foreach (ListItem item in list.ListItems)
                {
                    foreach (var child in item.Blocks)
                    {
                        FindNumberedListsInBlock(child, lists);
                    }
                }
            }
            else if (block is Section section)
            {
                foreach (var child in section.Blocks)
                {
                    FindNumberedListsInBlock(child, lists);
                }
            }
        }
    }
}
