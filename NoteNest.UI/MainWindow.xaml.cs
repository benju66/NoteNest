using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NoteNest.UI.ViewModels;
using System.Collections.Generic;

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
            var dirtyTabs = viewModel.OpenTabs?.Where(t => t.IsDirty).ToList() ?? new List<NoteTabItem>();
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
                        // Use SaveAllCommand which handles the saving properly
                        if (viewModel.SaveAllCommand.CanExecute(null))
                        {
                            viewModel.SaveAllCommand.Execute(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log error but don't prevent shutdown
                        System.Diagnostics.Debug.WriteLine($"Error saving during shutdown: {ex.Message}");
                        // Show warning but allow shutdown to continue
                        MessageBox.Show(
                            "Some files could not be saved. The application will still close.",
                            "Save Warning",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
            }

            // Save window settings - use fire-and-forget to avoid blocking
            try
            {
                var settings = viewModel.GetConfigService()?.Settings;
                if (settings?.WindowSettings != null)
                {
                    settings.WindowSettings.Width = this.ActualWidth;
                    settings.WindowSettings.Height = this.ActualHeight;
                    settings.WindowSettings.Left = this.Left;
                    settings.WindowSettings.Top = this.Top;
                    settings.WindowSettings.IsMaximized = this.WindowState == WindowState.Maximized;
                    
                    // Fire-and-forget - don't block UI thread
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await viewModel.GetConfigService().SaveSettingsAsync();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error saving settings during shutdown: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // Log error but don't prevent shutdown
                System.Diagnostics.Debug.WriteLine($"Error preparing settings save during shutdown: {ex.Message}");
            }

            // Initiate cleanup in background - don't wait for it
            _ = Task.Run(() =>
            {
                try
                {
                    viewModel?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during ViewModel disposal: {ex.Message}");
                }
            });
        }
    }
}