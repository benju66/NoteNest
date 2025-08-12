using System;
using System.Windows;
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

            CategoryList.SelectionChanged += CategoryList_SelectionChanged;
        }

        private void CategoryList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            GeneralSettings.Visibility = Visibility.Collapsed;
            EditorSettings.Visibility = Visibility.Collapsed;
            AppearanceSettings.Visibility = Visibility.Collapsed;
            FilesSettings.Visibility = Visibility.Collapsed;

            switch (CategoryList.SelectedIndex)
            {
                case 0:
                    GeneralSettings.Visibility = Visibility.Visible;
                    break;
                case 1:
                    EditorSettings.Visibility = Visibility.Visible;
                    break;
                case 2:
                    AppearanceSettings.Visibility = Visibility.Visible;
                    break;
                case 3:
                    FilesSettings.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SaveSettings();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
