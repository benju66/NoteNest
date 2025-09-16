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
        
        private void WireUpEvents()
        {
            // Track content changes
            TextChanged += (s, e) => ContentChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
