using System;
using System.Windows;
using System.Windows.Input;

namespace NoteNest.UI.Dialogs
{
    public partial class InputDialog : Window
    {
        public string ResponseText { get; private set; }
        public Func<string, string> ValidationFunction { get; set; }

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            InitializeComponent();
            Title = title;
            PromptLabel.Text = prompt;
            InputTextBox.Text = defaultValue;
            InputTextBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateInput())
            {
                ResponseText = InputTextBox.Text;
                DialogResult = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OkButton_Click(sender, e);
            }
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(InputTextBox.Text))
            {
                ErrorLabel.Text = "Value cannot be empty.";
                ErrorLabel.Visibility = Visibility.Visible;
                return false;
            }

            if (ValidationFunction != null)
            {
                string error = ValidationFunction(InputTextBox.Text);
                if (!string.IsNullOrEmpty(error))
                {
                    ErrorLabel.Text = error;
                    ErrorLabel.Visibility = Visibility.Visible;
                    return false;
                }
            }

            ErrorLabel.Visibility = Visibility.Collapsed;
            return true;
        }
    }
}
