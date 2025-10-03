using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace NoteNest.UI.Controls.Editor.RTF
{
    /// <summary>
    /// RTF formatting toolbar
    /// Single Responsibility: Provide formatting controls for RTF editor
    /// Clean, focused toolbar following SRP principles
    /// </summary>
    public partial class RTFToolbar : UserControl
    {
        public static readonly DependencyProperty TargetEditorProperty =
            DependencyProperty.Register(
                nameof(TargetEditor),
                typeof(RTFEditor),
                typeof(RTFToolbar),
                new PropertyMetadata(null, OnTargetEditorChanged));

        public static readonly DependencyProperty HighlightColorProperty =
            DependencyProperty.Register(
                nameof(HighlightColor),
                typeof(Brush),
                typeof(RTFToolbar),
                new PropertyMetadata(Brushes.Yellow));

        public RTFEditor TargetEditor
        {
            get => (RTFEditor)GetValue(TargetEditorProperty);
            set => SetValue(TargetEditorProperty, value);
        }

        public Brush HighlightColor
        {
            get => (Brush)GetValue(HighlightColorProperty);
            set => SetValue(HighlightColorProperty, value);
        }

        public RTFToolbar()
        {
            InitializeComponent();
        }

        private static void OnTargetEditorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RTFToolbar toolbar)
            {
                // Unsubscribe from old editor
                if (e.OldValue is RTFEditor oldEditor)
                {
                    oldEditor.SelectionChanged -= toolbar.OnEditorSelectionChanged;
                }
                
                // Subscribe to new editor
                if (e.NewValue is RTFEditor newEditor)
                {
                    newEditor.SelectionChanged += toolbar.OnEditorSelectionChanged;
                    toolbar.UpdateHighlightColor();
                    toolbar.UpdateAllToggleStates();
                }
            }
        }

        private void OnEditorSelectionChanged(object sender, RoutedEventArgs e)
        {
            // Update toggle states when selection changes
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateAllToggleStates();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpdateHighlightColor()
        {
            if (TargetEditor != null)
            {
                var color = TargetEditor.CurrentHighlightColor;
                HighlightColor = new SolidColorBrush(color);
            }
        }

        #region Formatting Commands

        private void SplitVerticalButton_Click(object sender, RoutedEventArgs e)
        {
            // Split command moved to main toolbar (Ctrl+\ in NewMainWindow)
            // This button is deprecated - split functionality is now in WorkspaceViewModel
            System.Diagnostics.Debug.WriteLine("[RTFToolbar] Split command deprecated - use main toolbar split button");
        }

        private void BoldButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFormattingCommand(() =>
            {
                EditingCommands.ToggleBold.Execute(null, TargetEditor);
                UpdateToggleButtonState(sender as ToggleButton, IsSelectionBold());
            });
        }

        private void ItalicButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFormattingCommand(() =>
            {
                EditingCommands.ToggleItalic.Execute(null, TargetEditor);
                UpdateToggleButtonState(sender as ToggleButton, IsSelectionItalic());
            });
        }

        private void UnderlineButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFormattingCommand(() =>
            {
                EditingCommands.ToggleUnderline.Execute(null, TargetEditor);
                UpdateToggleButtonState(sender as ToggleButton, IsSelectionUnderlined());
            });
        }

        private void HighlightButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFormattingCommand(() =>
            {
                // Direct method call - no complex key simulation needed
                TargetEditor?.CycleHighlight();
                
                // Update highlight color display
                UpdateHighlightColor();
            });
        }

        private void BulletListButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFormattingCommand(() =>
            {
                TargetEditor?.InsertBulletList();
                UpdateToggleButtonState(sender as ToggleButton, IsSelectionInBulletList());
            });
        }

        private void NumberedListButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFormattingCommand(() =>
            {
                TargetEditor?.InsertNumberedList();
                UpdateToggleButtonState(sender as ToggleButton, IsSelectionInNumberedList());
            });
        }

        private void IndentButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFormattingCommand(() =>
            {
                EditingCommands.IncreaseIndentation.Execute(null, TargetEditor);
            });
        }

        private void OutdentButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFormattingCommand(() =>
            {
                EditingCommands.DecreaseIndentation.Execute(null, TargetEditor);
            });
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Find the SplitWorkspace parent for split commands
        /// </summary>
        // Split workspace helper methods removed - functionality moved to WorkspaceViewModel

        /// <summary>
        /// Update toggle button state based on formatting
        /// </summary>
        private void UpdateToggleButtonState(ToggleButton toggleButton, bool isActive)
        {
            if (toggleButton != null)
            {
                toggleButton.IsChecked = isActive;
            }
        }

        /// <summary>
        /// Check if current selection is bold
        /// </summary>
        private bool IsSelectionBold()
        {
            if (TargetEditor?.Selection == null) return false;
            try
            {
                var value = TargetEditor.Selection.GetPropertyValue(TextElement.FontWeightProperty);
                return value != DependencyProperty.UnsetValue && 
                       ((FontWeight)value).ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight();
            }
            catch { return false; }
        }

        /// <summary>
        /// Check if current selection is italic
        /// </summary>
        private bool IsSelectionItalic()
        {
            if (TargetEditor?.Selection == null) return false;
            try
            {
                var value = TargetEditor.Selection.GetPropertyValue(TextElement.FontStyleProperty);
                return value != DependencyProperty.UnsetValue && 
                       (FontStyle)value == FontStyles.Italic;
            }
            catch { return false; }
        }

        /// <summary>
        /// Check if current selection is underlined
        /// </summary>
        private bool IsSelectionUnderlined()
        {
            if (TargetEditor?.Selection == null) return false;
            try
            {
                var value = TargetEditor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
                return value != DependencyProperty.UnsetValue && 
                       value is TextDecorationCollection decorations &&
                       decorations.Contains(TextDecorations.Underline[0]);
            }
            catch { return false; }
        }

        /// <summary>
        /// Check if current selection is in a bullet list
        /// Fixed: Added paragraph-parent fallback logic from GetCurrentListContext()
        /// </summary>
        private bool IsSelectionInBulletList()
        {
            if (TargetEditor?.CaretPosition == null) return false;
            try
            {
                // Check adjacent elements first (same as before)
                var listItem = TargetEditor.CaretPosition.GetAdjacentElement(LogicalDirection.Backward) as ListItem ??
                               TargetEditor.CaretPosition.GetAdjacentElement(LogicalDirection.Forward) as ListItem;
                
                // THE FIX: Add missing paragraph-parent check (copied from GetCurrentListContext)
                if (listItem == null)
                {
                    var paragraph = TargetEditor.CaretPosition.Paragraph;
                    listItem = paragraph?.Parent as ListItem;
                }
                
                if (listItem?.Parent is List list)
                {
                    return list.MarkerStyle == TextMarkerStyle.Disc || 
                           list.MarkerStyle == TextMarkerStyle.Circle || 
                           list.MarkerStyle == TextMarkerStyle.Square;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Check if current selection is in a numbered list
        /// Fixed: Added paragraph-parent fallback logic for consistency
        /// </summary>
        private bool IsSelectionInNumberedList()
        {
            if (TargetEditor?.CaretPosition == null) return false;
            try
            {
                // Check adjacent elements first (same as before)
                var listItem = TargetEditor.CaretPosition.GetAdjacentElement(LogicalDirection.Backward) as ListItem ??
                               TargetEditor.CaretPosition.GetAdjacentElement(LogicalDirection.Forward) as ListItem;
                
                // CONSISTENCY FIX: Add missing paragraph-parent check
                if (listItem == null)
                {
                    var paragraph = TargetEditor.CaretPosition.Paragraph;
                    listItem = paragraph?.Parent as ListItem;
                }
                
                if (listItem?.Parent is List list)
                {
                    return list.MarkerStyle == TextMarkerStyle.Decimal || 
                           list.MarkerStyle == TextMarkerStyle.LowerLatin || 
                           list.MarkerStyle == TextMarkerStyle.UpperLatin;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Execute a formatting command with error handling and focus management
        /// </summary>
        private void ExecuteFormattingCommand(Action command)
        {
            if (TargetEditor == null) return;

            try
            {
                // Ensure editor has focus before executing command
                if (!TargetEditor.IsFocused)
                {
                    TargetEditor.Focus();
                }

                // Execute the formatting command
                command?.Invoke();

                // Update all toggle button states after command
                UpdateAllToggleStates();

                // Keep focus on editor for continued editing
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    TargetEditor?.Focus();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFToolbar] Formatting command failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Update all toggle button states to reflect current selection
        /// </summary>
        private void UpdateAllToggleStates()
        {
            try
            {
                if (BoldButton is ToggleButton boldToggle)
                    boldToggle.IsChecked = IsSelectionBold();
                
                if (ItalicButton is ToggleButton italicToggle)
                    italicToggle.IsChecked = IsSelectionItalic();
                
                if (UnderlineButton is ToggleButton underlineToggle)
                    underlineToggle.IsChecked = IsSelectionUnderlined();
                
                if (BulletListButton is ToggleButton bulletToggle)
                    bulletToggle.IsChecked = IsSelectionInBulletList();
                
                if (NumberedListButton is ToggleButton numberedToggle)
                    numberedToggle.IsChecked = IsSelectionInNumberedList();
                
                // Update highlight color
                UpdateHighlightColor();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFToolbar] Toggle state update failed: {ex.Message}");
            }
        }

        #endregion
    }
}
