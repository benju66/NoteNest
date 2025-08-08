using System.ComponentModel;
using System.Linq;
using System.Windows;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            var viewModel = MainPanel.DataContext as MainViewModel;
            if (viewModel == null) return;

            // Check for unsaved changes
            var dirtyTabs = viewModel.OpenTabs.Where(t => t.IsDirty).ToList();
            if (dirtyTabs.Any())
            {
                var result = MessageBox.Show(
                    $"You have {dirtyTabs.Count} unsaved note(s). Save all before closing?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (result == MessageBoxResult.Yes)
                {
                    foreach (var tab in dirtyTabs)
                    {
                        viewModel.SaveNoteCommand.Execute(null);
                    }
                }
            }

            // Save window settings
            var settings = viewModel.GetConfigService().Settings;
            if (settings != null)
            {
                settings.WindowSettings.Width = this.ActualWidth;
                settings.WindowSettings.Height = this.ActualHeight;
                settings.WindowSettings.Left = this.Left;
                settings.WindowSettings.Top = this.Top;
                settings.WindowSettings.IsMaximized = this.WindowState == WindowState.Maximized;
                
                await viewModel.GetConfigService().SaveSettingsAsync();
            }
        }
    }
}