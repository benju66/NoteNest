using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace NoteNest.UI.Dialogs
{
    public partial class ModernInputDialog : Window
    {
        private readonly DispatcherTimer _validationTimer;
        private bool _isValidating = false;

        public string ResponseText { get; private set; }
        public Func<string, string> ValidationFunction { get; set; }
        public bool SelectAllOnFocus { get; set; } = true;
        public bool ShowRealTimeValidation { get; set; } = true;
        public bool AllowEmpty { get; set; } = false;

        public ModernInputDialog(string title, string prompt, string defaultValue = "", bool selectAllOnFocus = true)
        {
            InitializeComponent();
            
            // Initialize validation timer FIRST (before setting Text property which triggers TextChanged)
            _validationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300) // Debounce validation
            };
            _validationTimer.Tick += ValidationTimer_Tick;
            
            // Now safe to set properties that trigger events
            Title = title;
            PromptLabel.Text = prompt;
            InputTextBox.Text = defaultValue;
            SelectAllOnFocus = selectAllOnFocus;
            
            // Set up initial focus and selection
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Ensure proper focus and text selection after window is fully loaded
            InputTextBox.Focus();
            
            if (SelectAllOnFocus && !string.IsNullOrEmpty(InputTextBox.Text))
            {
                // Smart text selection - for "New Note 2024-01-15" select just "New Note" part
                var text = InputTextBox.Text;
                if (text.StartsWith("New Note", StringComparison.OrdinalIgnoreCase))
                {
                    InputTextBox.SelectAll();
                }
                else
                {
                    // For existing names, select all text for easy replacement
                    InputTextBox.SelectAll();
                }
            }
            else if (string.IsNullOrEmpty(InputTextBox.Text))
            {
                // Empty field - just focus, no selection needed
                InputTextBox.CaretIndex = 0;
            }
            
            // Initial validation
            if (ShowRealTimeValidation)
            {
                ValidateInputRealTime();
            }
        }

        private void InputTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // Safety check: timer might not be initialized yet if called during constructor
            if (ShowRealTimeValidation && !_isValidating && _validationTimer != null)
            {
                // Reset validation timer for debounced real-time validation
                _validationTimer.Stop();
                _validationTimer.Start();
                
                // Clear previous feedback immediately for responsive feel
                ErrorLabel.Visibility = Visibility.Collapsed;
                SuccessLabel.Visibility = Visibility.Collapsed;
            }
        }

        private void ValidationTimer_Tick(object sender, EventArgs e)
        {
            _validationTimer.Stop();
            ValidateInputRealTime();
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    if (ValidateInput())
                    {
                        OkButton_Click(sender, e);
                    }
                    e.Handled = true;
                    break;
                    
                case Key.Escape:
                    CancelButton_Click(sender, e);
                    e.Handled = true;
                    break;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateInput())
            {
                ResponseText = InputTextBox.Text?.Trim();
                DialogResult = true;
                Close();
            }
            else
            {
                // Focus back to input for correction
                InputTextBox.Focus();
                InputTextBox.SelectAll();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ResponseText = null;
            DialogResult = false;
            Close();
        }

        private void ValidateInputRealTime()
        {
            if (_isValidating) return;
            
            _isValidating = true;
            try
            {
                var text = InputTextBox.Text?.Trim() ?? string.Empty;
                
                // Basic empty check
                if (!AllowEmpty && string.IsNullOrWhiteSpace(text))
                {
                    ShowValidationFeedback(null, null); // No feedback for empty during typing
                    return;
                }
                
                // Custom validation
                if (ValidationFunction != null)
                {
                    string error = ValidationFunction(text);
                    if (!string.IsNullOrEmpty(error))
                    {
                        ShowValidationFeedback(error, false);
                        return;
                    }
                }
                
                // Success state
                if (!string.IsNullOrWhiteSpace(text))
                {
                    ShowValidationFeedback(null, true);
                }
            }
            finally
            {
                _isValidating = false;
            }
        }

        private bool ValidateInput()
        {
            var text = InputTextBox.Text?.Trim() ?? string.Empty;
            
            // Basic empty validation
            if (!AllowEmpty && string.IsNullOrWhiteSpace(text))
            {
                ShowValidationFeedback("Name cannot be empty.", false);
                return false;
            }

            // Custom validation
            if (ValidationFunction != null)
            {
                string error = ValidationFunction(text);
                if (!string.IsNullOrEmpty(error))
                {
                    ShowValidationFeedback(error, false);
                    return false;
                }
            }

            ShowValidationFeedback(null, true);
            return true;
        }

        private void ShowValidationFeedback(string errorMessage, bool? isValid)
        {
            if (!string.IsNullOrEmpty(errorMessage))
            {
                // Show error
                ErrorLabel.Text = errorMessage;
                ErrorLabel.Visibility = Visibility.Visible;
                SuccessLabel.Visibility = Visibility.Collapsed;
                OkButton.IsEnabled = false;
            }
            else if (isValid == true)
            {
                // Show success
                ErrorLabel.Visibility = Visibility.Collapsed;
                SuccessLabel.Visibility = Visibility.Visible;
                OkButton.IsEnabled = true;
            }
            else
            {
                // Neutral state (e.g., empty input during typing)
                ErrorLabel.Visibility = Visibility.Collapsed;
                SuccessLabel.Visibility = Visibility.Collapsed;
                OkButton.IsEnabled = !string.IsNullOrWhiteSpace(InputTextBox.Text?.Trim());
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _validationTimer?.Stop();
            base.OnClosed(e);
        }
    }
}
