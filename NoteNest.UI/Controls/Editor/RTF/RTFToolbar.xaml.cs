using System;
using System.Windows;
using System.Windows.Controls;
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
            if (d is RTFToolbar toolbar && e.NewValue is RTFEditor editor)
            {
                // Update highlight color when editor changes
                toolbar.UpdateHighlightColor();
            }
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

        private void BoldButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFormattingCommand(() =>
            {
                EditingCommands.ToggleBold.Execute(null, TargetEditor);
            });
        }

        private void ItalicButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFormattingCommand(() =>
            {
                EditingCommands.ToggleItalic.Execute(null, TargetEditor);
            });
        }

        private void UnderlineButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFormattingCommand(() =>
            {
                EditingCommands.ToggleUnderline.Execute(null, TargetEditor);
            });
        }

        private void HighlightButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFormattingCommand(() =>
            {
                // Use keyboard shortcut to trigger highlight cycling
                var highlightCommand = new KeyGesture(Key.H, ModifierKeys.Control);
                var keyBinding = new KeyBinding(null, highlightCommand);
                
                // Simulate Ctrl+H key press
                var keyEventArgs = new KeyEventArgs(Keyboard.PrimaryDevice, 
                    PresentationSource.FromVisual(TargetEditor), 0, Key.H)
                {
                    RoutedEvent = UIElement.KeyDownEvent
                };
                
                // Send to target editor
                TargetEditor?.RaiseEvent(keyEventArgs);
                
                // Update highlight color display
                UpdateHighlightColor();
            });
        }

        private void BulletListButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFormattingCommand(() =>
            {
                TargetEditor?.InsertBulletList();
            });
        }

        private void NumberedListButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFormattingCommand(() =>
            {
                TargetEditor?.InsertNumberedList();
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
    }
}
