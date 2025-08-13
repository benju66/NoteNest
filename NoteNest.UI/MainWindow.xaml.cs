using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NoteNest.UI.ViewModels;
using System.Collections.Generic;
using ModernWpf.Controls;
using NoteNest.UI.Services;
using NoteNest.UI.Windows;

namespace NoteNest.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            UpdateThemeMenuChecks();
        }

        private void SaveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MainPanel?.ViewModel?.SaveNoteCommand.Execute(null);
        }

        private void SaveAllMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MainPanel?.ViewModel?.SaveAllCommand.Execute(null);
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Create a new ConfigurationService instance for the settings window
            var fileSystem = new NoteNest.Core.Services.DefaultFileSystemProvider();
            var configService = new NoteNest.Core.Services.ConfigurationService(fileSystem);
            
            var win = new SettingsWindow(configService);
            win.Owner = this;
            if (win.ShowDialog() == true)
            {
                // Settings saved - trigger reload if needed
            }
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void FindMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Hook up to panel method if present
            // Placeholder per guide; to be implemented in Phase 9
        }

        private void ReplaceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Hook up to panel method if present
            // Placeholder per guide; to be implemented in Phase 9
        }

        private void LightTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.SetTheme(AppTheme.Light);
            UpdateThemeMenuChecks();
        }

        private void DarkTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.SetTheme(AppTheme.Dark);
            UpdateThemeMenuChecks();
        }

        private void SystemTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.SetTheme(AppTheme.System);
            UpdateThemeMenuChecks();
        }

        private void UpdateThemeMenuChecks()
        {
            var currentTheme = ThemeService.GetSavedTheme();
            LightThemeMenuItem.IsChecked = currentTheme == AppTheme.Light;
            DarkThemeMenuItem.IsChecked = currentTheme == AppTheme.Dark;
            SystemThemeMenuItem.IsChecked = currentTheme == AppTheme.System;
        }

        private async void DocumentationMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ModernWpf.Controls.ContentDialog
            {
                Title = "Documentation",
                Content = "Visit https://github.com/yourusername/NoteNest for documentation.",
                PrimaryButtonText = "OK",
                DefaultButton = ContentDialogButton.Primary,
                Owner = this
            };
            await dialog.ShowAsync();
        }

        private async void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ModernWpf.Controls.ContentDialog
            {
                Title = "About NoteNest",
                Content = "NoteNest v1.0.0\nA modern note-taking application\n\n© 2024 Your Name",
                PrimaryButtonText = "OK",
                DefaultButton = ContentDialogButton.Primary,
                Owner = this
            };
            await dialog.ShowAsync();
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