using System;
using System.Windows;
using System.Windows.Controls;
using NoteNest.UI.Controls.Editor.Core;

namespace NoteNest.UI.Controls.Editor
{
    public partial class EditorToolbar : UserControl
    {
        public static readonly DependencyProperty EditorProperty =
            DependencyProperty.Register(nameof(Editor), typeof(FormattedTextEditor),
                typeof(EditorToolbar), new PropertyMetadata(null, OnEditorChanged));

        public FormattedTextEditor Editor
        {
            get => (FormattedTextEditor)GetValue(EditorProperty);
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
                if (e.OldValue is FormattedTextEditor oldEditor)
                {
                    oldEditor.ListStateChanged -= toolbar.OnListStateChanged;
                }
                
                // Wire up new editor
                if (e.NewValue is FormattedTextEditor newEditor)
                {
                    newEditor.ListStateChanged += toolbar.OnListStateChanged;
                    
                    // Update initial state
                    toolbar.UpdateButtonStates(newEditor.GetCurrentListState());
                }
            }
        }

        public EditorToolbar()
        {
            InitializeComponent();
        }

        // Move all toolbar click handlers from SplitPaneView here
        private void BulletList_Click(object sender, RoutedEventArgs e)
        {
            Editor?.Focus();
            Editor?.InsertBulletList();
        }

        private void NumberedList_Click(object sender, RoutedEventArgs e)
        {
            Editor?.Focus();
            Editor?.InsertNumberedList();
        }

        private void TaskList_Click(object sender, RoutedEventArgs e)
        {
            // Task list not yet implemented
        }

        private void Indent_Click(object sender, RoutedEventArgs e)
        {
            Editor?.Focus();
            Editor?.IndentSelection();
        }

        private void Outdent_Click(object sender, RoutedEventArgs e)
        {
            Editor?.Focus();
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
