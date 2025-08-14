using System;
using System.Windows;
using ModernWpf.Controls;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Windows
{
    public partial class SettingsWindow : Window
    {
        private SettingsViewModel _viewModel;

        public SettingsWindow(Core.Services.ConfigurationService configService)
        {
            InitializeComponent();
            _viewModel = new SettingsViewModel(configService);
            DataContext = _viewModel;
        }

        private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem item)
            {
                // Hide all pages
                GeneralSettings.Visibility = Visibility.Collapsed;
                AppearanceSettings.Visibility = Visibility.Collapsed;
                EditorSettings.Visibility = Visibility.Collapsed;
                FilesSettings.Visibility = Visibility.Collapsed;
                StorageSettings.Visibility = Visibility.Collapsed;
                FeaturesSettings.Visibility = Visibility.Collapsed;

                // Show selected page
                switch (item.Tag?.ToString())
                {
                    case "General":
                        GeneralSettings.Visibility = Visibility.Visible;
                        break;
                    case "Appearance":
                        AppearanceSettings.Visibility = Visibility.Visible;
                        break;
                    case "Editor":
                        EditorSettings.Visibility = Visibility.Visible;
                        break;
                    case "Files":
                        FilesSettings.Visibility = Visibility.Visible;
                        break;
                    case "Storage":
                        StorageSettings.Visibility = Visibility.Visible;
                        break;
                    case "Features":
                        FeaturesSettings.Visibility = Visibility.Visible;
                        break;
                }
            }
        }

        private async void OK_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _viewModel.CommitSettings();
                DialogResult = true;
                Close();
            }
            catch (Exception)
            {
                // Optionally show an error; keep window open if save failed
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void MigrateNotes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var storageService = new Core.Services.StorageLocationService();
                var currentPath = _viewModel.GetCurrentSavedPath();
                var newPath = _viewModel.GetSelectedDestinationPath();
                
                if (string.IsNullOrEmpty(currentPath) || string.IsNullOrEmpty(newPath))
                {
                    MessageBox.Show(
                        "Invalid path configuration. Please check your settings.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
                
                string Normalize(string p) => string.IsNullOrWhiteSpace(p) ? string.Empty : System.IO.Path.GetFullPath(p).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                if (string.Equals(Normalize(currentPath), Normalize(newPath), StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        "Source and destination are the same. Please select a different storage location first.",
                        "No Change Required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
                
                var result = MessageBox.Show(
                    $"This will move all your notes from:\n\n{currentPath}\n\nTo:\n\n{newPath}\n\nDo you want to continue?",
                    "Confirm Migration",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                    return;
                
                var migrationWindow = new MigrationWindow(currentPath, newPath)
                {
                    Owner = this
                };
                
                if (migrationWindow.ShowDialog() == true && migrationWindow.MigrationSuccessful)
                {
                    _viewModel.Settings.DefaultNotePath = newPath;
                    _viewModel.Settings.MetadataPath = System.IO.Path.Combine(newPath, ".metadata");
                    await _viewModel.CommitSettings();
                    
                    _viewModel.RefreshStorageProperties();
                    
                    MessageBox.Show(
                        "Migration completed and saved. You may need to restart NoteNest.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"An error occurred: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
