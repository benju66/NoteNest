using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.UI.Windows;
using NoteNest.Core.Services;
using NoteNest.UI.ViewModels.Categories;
using NoteNest.UI.ViewModels.Shell;

namespace NoteNest.UI
{
    public partial class NewMainWindow : Window
    {
        public NewMainWindow()
        {
            InitializeComponent();
        }

        private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is NoteNest.UI.ViewModels.Shell.MainShellViewModel viewModel && e.NewValue is NoteNest.UI.ViewModels.Categories.CategoryViewModel category)
            {
                viewModel.CategoryTree.SelectedCategory = category;
            }
        }
        
        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenSettings();
        }
        
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        private void DatabaseHealthMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement database health window when database architecture is active
            MessageBox.Show("Database health monitoring will be available when database architecture is enabled.", 
                "Database Architecture", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        public void OpenSettings()
        {
            try
            {
                // Get ConfigurationService from DI container
                var app = (App)System.Windows.Application.Current;
                var configService = app.ServiceProvider?.GetService<ConfigurationService>();
                
                if (configService == null)
                {
                    // Fallback - create a basic config service
                    var fileSystem = new NoteNest.Core.Services.DefaultFileSystemProvider();
                    configService = new ConfigurationService(fileSystem);
                }
                
                var settingsWindow = new SettingsWindow(configService);
                settingsWindow.Owner = this;
                settingsWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open settings: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =============================================================================
        // TREE VIEW INTERACTION HANDLERS - Clean Architecture Event Forwarding
        // =============================================================================

        private void TreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainShellViewModel viewModel)
            {
                var treeView = sender as TreeView;
                
                // Check if double-clicked item is a note
                if (treeView?.SelectedItem is NoteItemViewModel note)
                {
                    // Request note opening through CategoryTreeViewModel
                    viewModel.CategoryTree.OpenNote(note);
                    e.Handled = true;
                }
            }
        }

        private void TreeView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is MainShellViewModel viewModel)
            {
                var treeView = sender as TreeView;
                
                // Check if Enter was pressed on a note item
                if (treeView?.SelectedItem is NoteItemViewModel note)
                {
                    // Request note opening through CategoryTreeViewModel
                    viewModel.CategoryTree.OpenNote(note);
                    e.Handled = true;
                }
                // Check if Enter was pressed on a category
                else if (treeView?.SelectedItem is CategoryViewModel category)
                {
                    // Toggle expansion of category
                    _ = category.ToggleExpandAsync();
                    e.Handled = true;
                }
            }
        }
    }

    public class BoolToTextConverter : IValueConverter
    {
        public static readonly BoolToTextConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)(value ?? false) ? "Loading..." : "Ready";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}