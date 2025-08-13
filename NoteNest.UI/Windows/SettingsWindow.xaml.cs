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
                }
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SaveSettings();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
