using System;
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

        private void Window_Closing(object sender, CancelEventArgs e)
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
                    try
                    {
                        // Save synchronously to avoid async issues during shutdown
                        foreach (var tab in dirtyTabs)
                        {
                            viewModel.SaveNoteCommand.Execute(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log error but don't prevent shutdown
                        System.Diagnostics.Debug.WriteLine($"Error saving during shutdown: {ex.Message}");
                    }
                }
            }

            // Save window settings synchronously
            try
            {
                var settings = viewModel.GetConfigService().Settings;
                if (settings != null)
                {
                    settings.WindowSettings.Width = this.ActualWidth;
                    settings.WindowSettings.Height = this.ActualHeight;
                    settings.WindowSettings.Left = this.Left;
                    settings.WindowSettings.Top = this.Top;
                    settings.WindowSettings.IsMaximized = this.WindowState == WindowState.Maximized;
                    
                    // Save synchronously to avoid async issues during shutdown
                    viewModel.GetConfigService().SaveSettingsAsync().GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                // Log error but don't prevent shutdown
                System.Diagnostics.Debug.WriteLine($"Error saving settings during shutdown: {ex.Message}");
            }
        }
    }
}