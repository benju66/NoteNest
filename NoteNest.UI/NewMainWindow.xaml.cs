using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.UI.Windows;
using NoteNest.Core.Services;
using NoteNest.UI.ViewModels.Categories;
using static NoteNest.UI.ViewModels.Categories.CategoryViewModel;
using NoteNest.UI.ViewModels;
using NoteNest.UI.ViewModels.Shell;
using NoteNest.UI.Services;

namespace NoteNest.UI
{
    public partial class NewMainWindow : Window
    {
        public NewMainWindow()
        {
            InitializeComponent();
            Loaded += OnWindowLoaded;
            StateChanged += OnWindowStateChanged;
        }
        
        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            UpdateMaximizeRestoreIcon();
            
            // Set theme selector to current theme
            try
            {
                var app = (App)System.Windows.Application.Current;
                var themeService = app.ServiceProvider?.GetService<IThemeService>();
                if (themeService != null)
                {
                    // Select the current theme in the dropdown
                    var currentTheme = themeService.CurrentTheme.ToString();
                    foreach (ComboBoxItem item in ThemeSelector.Items)
                    {
                        if (item.Tag?.ToString() == currentTheme)
                        {
                            ThemeSelector.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            catch { /* Ignore errors setting initial theme */ }
        }
        
        private void OnTreeViewLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TreeView treeView && 
                DataContext is MainShellViewModel shell)
            {
                shell.CategoryTree.EnableDragDrop(treeView, shell.CategoryOperations);
                System.Diagnostics.Debug.WriteLine("[MainWindow] ✅ Drag & drop enabled for category tree view");
            }
        }
        
        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            UpdateMaximizeRestoreIcon();
        }
        
        private void UpdateMaximizeRestoreIcon()
        {
            if (MaximizeIcon != null && MaximizeRestoreButton != null)
            {
                if (WindowState == WindowState.Maximized)
                {
                    // Restore icon (two overlapping squares)
                    MaximizeIcon.Data = Geometry.Parse("M0,3 H7 V10 H0 Z M3,0 H10 V7");
                    MaximizeRestoreButton.ToolTip = "Restore Down";
                }
                else
                {
                    // Maximize icon (single square)
                    MaximizeIcon.Data = Geometry.Parse("M0,0 H10 V10 H0 Z");
                    MaximizeRestoreButton.ToolTip = "Maximize";
                }
            }
        }

        private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is NoteNest.UI.ViewModels.Shell.MainShellViewModel viewModel)
            {
                // Update the unified SelectedItem property (handles both categories and notes)
                viewModel.CategoryTree.SelectedItem = e.NewValue;
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
        
        // =============================================================================
        // WINDOW CONTROL BUTTON HANDLERS
        // =============================================================================
        
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        
        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized 
                ? WindowState.Normal 
                : WindowState.Maximized;
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        private void MoreMenuButton_Click(object sender, RoutedEventArgs e)
        {
            MoreMenuPopup.IsOpen = !MoreMenuPopup.IsOpen;
        }
        
        // =============================================================================
        // MORE MENU ITEM HANDLERS
        // =============================================================================
        
        private void NewNote_Click(object sender, RoutedEventArgs e)
        {
            MoreMenuPopup.IsOpen = false;
            var viewModel = DataContext as MainShellViewModel;
            if (viewModel?.NoteOperations?.CreateNoteCommand?.CanExecute(null) == true)
            {
                viewModel.NoteOperations.CreateNoteCommand.Execute(null);
            }
        }
        
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            MoreMenuPopup.IsOpen = false;
            var viewModel = DataContext as MainShellViewModel;
            if (viewModel?.Workspace?.SaveTabCommand?.CanExecute(viewModel.Workspace.SelectedTab) == true)
            {
                viewModel.Workspace.SaveTabCommand.Execute(viewModel.Workspace.SelectedTab);
            }
        }
        
        private void SaveAll_Click(object sender, RoutedEventArgs e)
        {
            MoreMenuPopup.IsOpen = false;
            var viewModel = DataContext as MainShellViewModel;
            if (viewModel?.Workspace?.SaveAllTabsCommand?.CanExecute(null) == true)
            {
                viewModel.Workspace.SaveAllTabsCommand.Execute(null);
            }
        }
        
        private void SplitEditor_Click(object sender, RoutedEventArgs e)
        {
            MoreMenuPopup.IsOpen = false;
            var viewModel = DataContext as MainShellViewModel;
            if (viewModel?.Workspace?.SplitVerticalCommand?.CanExecute(null) == true)
            {
                viewModel.Workspace.SplitVerticalCommand.Execute(null);
            }
        }
        
        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            MoreMenuPopup.IsOpen = false;
            var viewModel = DataContext as MainShellViewModel;
            if (viewModel?.RefreshCommand?.CanExecute(null) == true)
            {
                viewModel.RefreshCommand.Execute(null);
            }
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
            
            var settingsWindow = new SettingsWindow();
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

        // =============================================================================
        // THEME SWITCHER HANDLER
        // =============================================================================
        
        private async void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;
            
            try
            {
                var selectedItem = e.AddedItems[0] as ComboBoxItem;
                var themeTag = selectedItem?.Tag?.ToString();
                
                if (string.IsNullOrEmpty(themeTag)) return;
                
                if (Enum.TryParse<ThemeType>(themeTag, out var theme))
                {
                    var app = (App)System.Windows.Application.Current;
                    var themeService = app.ServiceProvider?.GetService<IThemeService>();
                    
                    if (themeService != null)
                    {
                        await themeService.SetThemeAsync(theme);
                        
                        // Update status message if we have a ViewModel
                        if (DataContext is MainShellViewModel viewModel)
                        {
                            viewModel.StatusMessage = $"Theme changed to: {selectedItem.Content}";
                        }
                        
                        // Close the More menu popup after theme selection
                        MoreMenuPopup.IsOpen = false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to change theme: {ex.Message}", "Theme Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // =============================================================================
        // YOUR EXISTING SEARCH EVENT HANDLERS
        // =============================================================================

        private void SmartSearch_ResultSelected(object sender, SearchResultSelectedEventArgs e)
        {
            // Forward to MainShellViewModel (which handles opening notes in RTF editor)
            var viewModel = DataContext as NoteNest.UI.ViewModels.Shell.MainShellViewModel;
            if (viewModel != null)
            {
                // Call the existing event handler we updated
                viewModel.GetType().GetMethod("OnSearchResultSelected",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.Invoke(viewModel, new object[] { sender, e });
            }
        }

        // ✨ HYBRID FOLDER TAGGING: Folder context menu handlers
        private async void SetFolderTags_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menuItem = sender as MenuItem;
                var category = menuItem?.Tag as CategoryViewModel;
                
                if (category == null)
                    return;

                var app = (App)System.Windows.Application.Current;
                var mediator = app.ServiceProvider?.GetService<MediatR.IMediator>();
                var tagQueryService = app.ServiceProvider?.GetService<NoteNest.Application.Queries.ITagQueryService>();
                var logger = app.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();

                if (mediator == null || tagQueryService == null || logger == null)
                {
                    MessageBox.Show("Required services not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var dialog = new FolderTagDialog(Guid.Parse(category.Id), category.BreadcrumbPath, mediator, tagQueryService, logger)
                {
                    Owner = this
                };

                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening folder tag dialog: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RemoveFolderTags_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menuItem = sender as MenuItem;
                var category = menuItem?.Tag as CategoryViewModel;
                
                if (category == null)
                    return;

                var result = MessageBox.Show(
                    $"Remove all tags from folder '{category.Name}'?\n\nExisting items will keep their tags.",
                    "Confirm Remove Tags",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                var app = (App)System.Windows.Application.Current;
                var mediator = app.ServiceProvider?.GetService<MediatR.IMediator>();
                
                if (mediator == null)
                {
                    MessageBox.Show("Mediator service not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var command = new NoteNest.Application.FolderTags.Commands.RemoveFolderTag.RemoveFolderTagCommand
                {
                    FolderId = Guid.Parse(category.Id)
                };

                var commandResult = await mediator.Send(command);
                
                if (commandResult.Success)
                {
                    MessageBox.Show("Folder tags removed successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to remove tags: {commandResult.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error removing folder tags: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ✨ NOTE TAGGING: Note context menu handlers
        private async void SetNoteTags_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menuItem = sender as MenuItem;
                var note = menuItem?.Tag as NoteItemViewModel;
                
                if (note == null)
                    return;

                var app = (App)System.Windows.Application.Current;
                var mediator = app.ServiceProvider?.GetService<MediatR.IMediator>();
                var tagQueryService = app.ServiceProvider?.GetService<NoteNest.Application.Queries.ITagQueryService>();
                var treeQueryService = app.ServiceProvider?.GetService<NoteNest.Application.Queries.ITreeQueryService>();
                var logger = app.ServiceProvider?.GetService<NoteNest.Core.Services.Logging.IAppLogger>();

                if (mediator == null || tagQueryService == null || treeQueryService == null || logger == null)
                {
                    MessageBox.Show("Required services not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var dialog = new NoteTagDialog(Guid.Parse(note.Id), note.Title, mediator, tagQueryService, treeQueryService, logger)
                {
                    Owner = this
                };

                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening note tag dialog: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RemoveNoteTags_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menuItem = sender as MenuItem;
                var note = menuItem?.Tag as NoteItemViewModel;
                
                if (note == null)
                    return;

                var result = MessageBox.Show(
                    $"Remove all tags from note '{note.Title}'?",
                    "Confirm Remove Tags",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                var app = (App)System.Windows.Application.Current;
                var mediator = app.ServiceProvider?.GetService<MediatR.IMediator>();
                
                if (mediator == null)
                {
                    MessageBox.Show("Mediator service not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var command = new NoteNest.Application.NoteTags.Commands.RemoveNoteTag.RemoveNoteTagCommand
                {
                    NoteId = Guid.Parse(note.Id)
                };

                var commandResult = await mediator.Send(command);
                
                if (commandResult.Success)
                {
                    MessageBox.Show("Note tags removed successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to remove tags: {commandResult.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error removing note tags: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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