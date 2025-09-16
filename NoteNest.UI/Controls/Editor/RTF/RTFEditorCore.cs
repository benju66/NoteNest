using System;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace NoteNest.UI.Controls.Editor.RTF
{
    /// <summary>
    /// Minimal RTF editor core - just the WPF RichTextBox with basic setup
    /// Single Responsibility: Basic RTF editing foundation
    /// Clean, focused, ~50 lines as designed
    /// </summary>
    public class RTFEditorCore : RichTextBox
    {
        public event EventHandler ContentChanged;
        
        public RTFEditorCore()
        {
            InitializeBasicSetup();
            RegisterBasicKeyboardShortcuts();
            WireUpEvents();
        }
        
        private void InitializeBasicSetup()
        {
            // Essential editor properties
            AcceptsReturn = true;
            AcceptsTab = true;
            IsDocumentEnabled = true;
            Focusable = true;
            IsHitTestVisible = true;
            
            // Scrolling behavior
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            
            // Initialize with empty document
            Document = new FlowDocument();
            
            // Apply single spacing document styles
            InitializeDocumentStyles();
            
            // Basic visual setup
            FocusVisualStyle = null;
        }
        
        private void RegisterBasicKeyboardShortcuts()
        {
            // Essential formatting shortcuts
            InputBindings.Add(new KeyBinding(EditingCommands.ToggleBold, Key.B, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(EditingCommands.ToggleItalic, Key.I, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(EditingCommands.ToggleUnderline, Key.U, ModifierKeys.Control));
            
            // List shortcuts
            InputBindings.Add(new KeyBinding(EditingCommands.ToggleBullets, Key.L, ModifierKeys.Control | ModifierKeys.Shift));
            InputBindings.Add(new KeyBinding(EditingCommands.ToggleNumbering, Key.N, ModifierKeys.Control | ModifierKeys.Shift));
        }
        
        private void InitializeDocumentStyles()
        {
            try
            {
                // Remove excessive page padding for single spacing
                Document.PagePadding = new System.Windows.Thickness(0);
                
                // Set auto line height for single spacing
                Document.LineHeight = double.NaN;
                
                // Create single spacing paragraph style
                var paragraphStyle = new System.Windows.Style(typeof(Paragraph));
                paragraphStyle.Setters.Add(new System.Windows.Setter(Paragraph.MarginProperty, new System.Windows.Thickness(0, 0, 0, 0)));
                paragraphStyle.Setters.Add(new System.Windows.Setter(Paragraph.LineHeightProperty, double.NaN));
                Document.Resources.Add(typeof(Paragraph), paragraphStyle);
                
                // Create minimal spacing list style
                var listStyle = new System.Windows.Style(typeof(List));
                listStyle.Setters.Add(new System.Windows.Setter(List.MarginProperty, new System.Windows.Thickness(0, 0, 0, 6)));
                listStyle.Setters.Add(new System.Windows.Setter(List.PaddingProperty, new System.Windows.Thickness(0)));
                Document.Resources.Add(typeof(List), listStyle);
                
                // Create minimal spacing list item style
                var listItemStyle = new System.Windows.Style(typeof(ListItem));
                listItemStyle.Setters.Add(new System.Windows.Setter(ListItem.MarginProperty, new System.Windows.Thickness(0)));
                listItemStyle.Setters.Add(new System.Windows.Setter(ListItem.PaddingProperty, new System.Windows.Thickness(0, 0, 0, 2)));
                Document.Resources.Add(typeof(ListItem), listItemStyle);
                
                System.Diagnostics.Debug.WriteLine("[RTFEditorCore] Single spacing document styles initialized");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTFEditorCore] Document style initialization failed: {ex.Message}");
            }
        }
        
        private void WireUpEvents()
        {
            // Track content changes
            TextChanged += (s, e) => ContentChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
