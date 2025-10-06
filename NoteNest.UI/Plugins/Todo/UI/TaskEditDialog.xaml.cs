using System.Windows;

namespace NoteNest.UI.Plugins.Todo.UI
{
    /// <summary>
    /// Simple dialog for editing task properties
    /// Converted from ModernWPF ContentDialog to standard Window
    /// </summary>
    public partial class TaskEditDialog : Window
    {
        public string TaskTitle { get; set; } = string.Empty;
        public string TaskDescription { get; set; } = string.Empty;
        public bool DialogResult { get; private set; } = false;

        public TaskEditDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Set initial values from properties
            TitleTextBox.Text = TaskTitle;
            DescriptionTextBox.Text = TaskDescription;
            
            // Focus the title textbox
            TitleTextBox.Focus();
            TitleTextBox.SelectAll();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Get values from textboxes
            TaskTitle = TitleTextBox.Text;
            TaskDescription = DescriptionTextBox.Text;
            
            // Set dialog result and close
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Cancel - just close without saving
            DialogResult = false;
            Close();
        }
    }
}
