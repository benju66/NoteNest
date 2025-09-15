using System;
using System.Windows;
using System.Windows.Controls;
using NoteNest.UI.Controls.Editor.Core;
using NoteNest.Core.Models;

namespace NoteNest.UI.Controls.Editor
{
    public partial class EditorToolbar : UserControl
    {
        // Change from FormattedTextEditor to INotesEditor interface
        public static readonly DependencyProperty EditorProperty =
            DependencyProperty.Register(nameof(Editor), typeof(INotesEditor),
                typeof(EditorToolbar), new PropertyMetadata(null, OnEditorChanged));

        public INotesEditor Editor
        {
            get => (INotesEditor)GetValue(EditorProperty);
            set => SetValue(EditorProperty, value);
        }

        /// <summary>
        /// UX POLISH: Handle editor changes to wire up list state feedback
        /// </summary>
        private static void OnEditorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EditorToolbar toolbar)
            {
                // Unwire old editor
                if (e.OldValue is INotesEditor oldEditor)
                {
                    oldEditor.ListStateChanged -= toolbar.OnListStateChanged;
                }
                
                // Wire up new editor
                if (e.NewValue is INotesEditor newEditor)
                {
                    newEditor.ListStateChanged += toolbar.OnListStateChanged;
                    
                    // Update button visibility based on format
                    toolbar.UpdateToolbarVisibility();
                    
                    // Update initial state
                    if (newEditor is FormattedTextEditor formattedEditor)
                    {
                        toolbar.UpdateButtonStates(formattedEditor.GetCurrentListState());
                    }
                }
            }
        }

        public EditorToolbar()
        {
            InitializeComponent();
        }

        private void UpdateToolbarVisibility()
        {
            // Only reference buttons that actually exist
            var taskButton = FindName("TaskListButton") as Button;
            if (taskButton != null)
            {
                taskButton.Visibility = Editor?.Format == NoteFormat.RTF ? 
                    Visibility.Collapsed : Visibility.Visible;
            }
            
            // Could add more format-specific UI adaptations here
            System.Diagnostics.Debug.WriteLine($"[TOOLBAR] Updated visibility for {Editor?.Format} editor");
        }

        // Update all click handlers to work with interface
        private void BulletList_Click(object sender, RoutedEventArgs e)
        {
            if (Editor is UIElement element)
                element.Focus();
            Editor?.InsertBulletList();
        }

        private void NumberedList_Click(object sender, RoutedEventArgs e)
        {
            if (Editor is UIElement element)
                element.Focus();
            Editor?.InsertNumberedList();
        }

        private void TaskList_Click(object sender, RoutedEventArgs e)
        {
            // Task list not yet implemented
        }

        private void Bold_Click(object sender, RoutedEventArgs e)
        {
            Editor?.ToggleBold();
        }

        private void Italic_Click(object sender, RoutedEventArgs e)
        {
            Editor?.ToggleItalic();
        }

        private void Indent_Click(object sender, RoutedEventArgs e)
        {
            if (Editor is UIElement element)
                element.Focus();
            Editor?.IndentSelection();
        }

        private void Outdent_Click(object sender, RoutedEventArgs e)
        {
            if (Editor is UIElement element)
                element.Focus();
            Editor?.OutdentSelection();
        }
        
        /// <summary>
        /// UX POLISH: Handle list state changes from editor
        /// </summary>
        private void OnListStateChanged(object sender, ListStateChangedEventArgs e)
        {
            UpdateButtonStates(e.State);
        }
        
        /// <summary>
        /// UX POLISH: Update button visual states based on current list context
        /// </summary>
        private void UpdateButtonStates(ListState state)
        {
            try
            {
                // Update bullet button state
                if (BulletButton != null)
                {
                    BulletButton.IsChecked = state.IsInBulletList;
                }
                
                // Update numbered button state  
                if (NumberedButton != null)
                {
                    NumberedButton.IsChecked = state.IsInNumberedList;
                }
                
                System.Diagnostics.Debug.WriteLine($"[TOOLBAR] Updated button states: Bullets={state.IsInBulletList}, Numbers={state.IsInNumberedList}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Button state update failed: {ex.Message}");
            }
        }
    }
}
