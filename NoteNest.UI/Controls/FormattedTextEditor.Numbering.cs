using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using NoteNest.UI.Controls.ListHandling;

namespace NoteNest.UI.Controls
{
    public partial class FormattedTextEditor
    {
        private readonly NumberingEngine _numberingEngine = new();
        private DispatcherTimer _renumberTimer;
        private NumberingScheme _activeNumberingScheme = NumberingScheme.Default;
        
        // Commands for numbering operations
        public static readonly RoutedCommand ApplyNumberingSchemeCommand = new RoutedCommand(
            "ApplyNumberingScheme", typeof(FormattedTextEditor));
        
        public static readonly RoutedCommand RenumberListsCommand = new RoutedCommand(
            "RenumberLists", typeof(FormattedTextEditor));

        private void InitializeNumberingSystem()
        {
            // Set up renumbering timer (debounced for performance)
            _renumberTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _renumberTimer.Tick += (s, e) =>
            {
                _renumberTimer.Stop();
                PerformRenumbering();
            };

            // Register numbering commands
            CommandBindings.Add(new CommandBinding(
                ApplyNumberingSchemeCommand,
                ExecuteApplyNumberingScheme));
            
            CommandBindings.Add(new CommandBinding(
                RenumberListsCommand,
                ExecuteRenumberLists));
        }

        private void ExecuteApplyNumberingScheme(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Parameter is NumberingScheme scheme)
            {
                ApplyNumberingScheme(scheme);
            }
        }

        private void ExecuteRenumberLists(object sender, ExecutedRoutedEventArgs e)
        {
            PerformRenumbering();
        }

        public void ApplyNumberingScheme(NumberingScheme scheme)
        {
            _activeNumberingScheme = scheme ?? NumberingScheme.Default;
            _numberingEngine.SetNumberingScheme(_activeNumberingScheme);
            
            // Trigger renumbering
            ScheduleRenumbering();
        }

        public void ScheduleRenumbering()
        {
            // Debounce renumbering for performance
            _renumberTimer.Stop();
            _renumberTimer.Start();
        }

        private void PerformRenumbering()
        {
            if (_isUpdating || Document == null) return;
            
            try
            {
                // Save caret position
                var savedIndex = GetCaretCharacterIndex();
                
                // Perform renumbering
                _numberingEngine.UpdateListNumbering(Document);
                
                // Update visual representation
                UpdateNumberedListVisuals();
                
                // Restore caret position
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    SetCaretAtCharacterIndex(savedIndex);
                }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] PerformRenumbering failed: {ex.Message}");
            }
        }

        private void UpdateNumberedListVisuals()
        {
            // Find all numbered lists
            var lists = _numberingEngine.FindNumberedLists(Document);
            
            foreach (var list in lists)
            {
                UpdateListVisual(list);
            }
        }

        private void UpdateListVisual(List list)
        {
            if (list == null) return;
            
            try
            {
                foreach (ListItem item in list.ListItems)
                {
                    // Get the custom number
                    var customNumber = NumberingEngine.GetListItemNumber(item);
                    
                    if (!string.IsNullOrEmpty(customNumber))
                    {
                        // Update the visual representation
                        // This could be done via custom rendering or by modifying the list marker
                        ApplyCustomNumberToListItem(item, customNumber);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] UpdateListVisual failed: {ex.Message}");
            }
        }

        private void ApplyCustomNumberToListItem(ListItem item, string number)
        {
            // For now, we'll store the number and rely on custom rendering
            // In a full implementation, this could modify the actual list marker
            
            // Option 1: Use Run at start of first paragraph
            if (item.Blocks.FirstBlock is Paragraph para)
            {
                // Check if we already have a number run
                if (para.Inlines.FirstInline is Run run && 
                    run.Tag as string == "CustomNumber")
                {
                    // Update existing
                    run.Text = number + " ";
                }
                else
                {
                    // Note: This is a simplified approach
                    // In production, we'd use custom rendering
                    System.Diagnostics.Debug.WriteLine($"[NUMBER] {number}");
                }
            }
        }

        // Enhanced numbered list insertion with numbering system
        public void InsertNumberedListWithNumbering()
        {
            BeginChange();
            try
            {
                // First apply the list
                SmartToggleList(TextMarkerStyle.Decimal);
                
                // Then schedule renumbering
                ScheduleRenumbering();
            }
            finally
            {
                EndChange();
            }
        }

        // Hook into list structure changes
        protected override void OnTextChanged(TextChangedEventArgs e)
        {
            base.OnTextChanged(e);
            
            if (!_isUpdating)
            {
                // Check if we have numbered lists that need renumbering
                if (HasNumberedLists())
                {
                    ScheduleRenumbering();
                }
            }
        }

        private bool HasNumberedLists()
        {
            if (Document == null) return false;
            
            foreach (var block in Document.Blocks)
            {
                if (HasNumberedListsInBlock(block))
                    return true;
            }
            
            return false;
        }

        private bool HasNumberedListsInBlock(Block block)
        {
            if (block is List list && list.MarkerStyle == TextMarkerStyle.Decimal)
                return true;
            
            if (block is Section section)
            {
                foreach (var child in section.Blocks)
                {
                    if (HasNumberedListsInBlock(child))
                        return true;
                }
            }
            
            return false;
        }

        // Handle list operations that affect numbering
        private void OnListStructureChanged()
        {
            if (HasNumberedLists())
            {
                ScheduleRenumbering();
            }
        }

        // Public API for getting/setting numbering scheme
        public NumberingScheme CurrentNumberingScheme
        {
            get => _activeNumberingScheme;
            set
            {
                if (value != _activeNumberingScheme)
                {
                    ApplyNumberingScheme(value);
                }
            }
        }

        // Support for different numbering styles via toolbar or context menu
        public void SetNumberingStyle(ListHandling.NumberingStyle style, int level = 0)
        {
            // Create a custom scheme with the specified style
            var customScheme = new NumberingScheme
            {
                Name = "Custom",
                LevelStyles = new Dictionary<int, ListHandling.NumberingStyle>
                {
                    { level, style }
                }
            };
            
            // Copy other levels from current scheme
            for (int i = 0; i < 5; i++)
            {
                if (i != level)
                {
                    customScheme.LevelStyles[i] = _activeNumberingScheme.GetStyleForLevel(i);
                }
            }
            
            ApplyNumberingScheme(customScheme);
        }
    }
}
