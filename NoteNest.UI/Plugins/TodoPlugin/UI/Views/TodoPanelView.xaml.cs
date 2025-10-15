using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.UI.Plugins.TodoPlugin.UI.ViewModels;

namespace NoteNest.UI.Plugins.TodoPlugin.UI.Views
{
    /// <summary>
    /// Interaction logic for TodoPanelView.xaml
    /// </summary>
    public partial class TodoPanelView : UserControl
    {
        private readonly NoteNest.Core.Services.Logging.IAppLogger _logger;
        
        public TodoPanelView(TodoPanelViewModel viewModel, NoteNest.Core.Services.Logging.IAppLogger logger)
        {
            _logger = logger;
            
            try
            {
                _logger.Info("🎨 [TodoPanelView] Constructor called");
                
                InitializeComponent();
                _logger.Info("🎨 [TodoPanelView] InitializeComponent completed");
                
                // ✨ TAG MVP: Initialize context menu for dynamic tag list
                InitializeContextMenus();
                
                DataContext = viewModel;
                _logger.Info($"🎨 [TodoPanelView] DataContext set: {viewModel != null}");
                _logger.Info($"🎨 [TodoPanelView] CategoryTree not null: {viewModel?.CategoryTree != null}");
                _logger.Info($"🎨 [TodoPanelView] Initial Categories.Count: {viewModel?.CategoryTree?.Categories?.Count ?? -1}");
                
                // Monitor Categories collection changes for diagnostics
                if (viewModel?.CategoryTree?.Categories != null)
                {
                    viewModel.CategoryTree.Categories.CollectionChanged += (s, e) =>
                    {
                        _logger.Info($"🎨 [TodoPanelView] Categories CollectionChanged! Action={e.Action}, Count={viewModel.CategoryTree.Categories.Count}");
                        
                        // Log items in collection
                        foreach (var item in viewModel.CategoryTree.Categories)
                        {
                            _logger.Info($"🎨 [TodoPanelView] - Category: DisplayPath='{item.DisplayPath}', Name='{item.Name}'");
                        }
                        
                        // Removed popup - was for diagnostics only
                    };
                    
                    _logger.Info("🎨 [TodoPanelView] Subscribed to Categories.CollectionChanged");
                }
                
                // Check view after loaded
                this.Loaded += (s, e) =>
                {
                    _logger.Info($"🎨 [TodoPanelView] View LOADED - Categories in ViewModel: {viewModel?.CategoryTree?.Categories?.Count ?? 0}");
                };
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "❌ [TodoPanelView] Constructor failed");
                throw;
            }
        }

        private void QuickAddTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (DataContext is TodoPanelViewModel panelVm && panelVm.TodoList.QuickAddCommand.CanExecute(null))
                {
                    panelVm.TodoList.QuickAddCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private void CategoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is TodoPanelViewModel panelVm)
            {
                // Use unified selection property (matches main app pattern)
                // ViewModel handles CategoryNodeViewModel and TodoItemViewModel appropriately
                panelVm.CategoryTree.SelectedItem = e.NewValue;
                
                // Log for diagnostics
                if (e.NewValue is CategoryNodeViewModel categoryNode)
                {
                    _logger.Debug($"[TodoPanelView] Category selected: {categoryNode.Name} (ID: {categoryNode.CategoryId})");
                }
                else if (e.NewValue is TodoItemViewModel todoVm)
                {
                    _logger.Debug($"[TodoPanelView] Todo selected: {todoVm.Text} (CategoryId: {todoVm.CategoryId})");
                }
            }
        }
        
        private void CategoryTreeView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && sender is TreeView treeView)
            {
                // Get selected item
                var selectedItem = treeView.SelectedItem;
                
                // Only delete categories, not todo items
                if (selectedItem is CategoryNodeViewModel categoryNode)
                {
                    if (DataContext is TodoPanelViewModel panelVm)
                    {
                        // Get CategoryStore and delete
                        var categoryStore = panelVm.CategoryTree.CategoryStore;
                        if (categoryStore != null)
                        {
                            categoryStore.Delete(categoryNode.CategoryId);
                            _logger.Info($"[TodoPanelView] Category removed from todo tree: {categoryNode.Name}");
                            e.Handled = true;
                        }
                    }
                }
                else if (selectedItem is TodoItemViewModel todoVm)
                {
                    // Delete todo using hybrid strategy (hard for manual, soft for note-linked)
                    if (DataContext is TodoPanelViewModel panelVm)
                    {
                        // Execute delete command (has hybrid logic built-in)
                        if (panelVm.TodoList.DeleteTodoCommand.CanExecute(todoVm))
                        {
                            panelVm.TodoList.DeleteTodoCommand.Execute(todoVm);
                            _logger.Info($"[TodoPanelView] Todo deleted: {todoVm.Text}");
                        }
                    }
                    e.Handled = true; // CRITICAL: Prevent event bubbling to parent controls!
                }
            }
        }

        // Category selection - not needed for ListBox (SelectionChanged handled by binding)

        private void EditTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                var todoItem = textBox.DataContext as TodoItemViewModel;
                if (todoItem == null) return;

                if (e.Key == Key.Enter)
                {
                    todoItem.SaveEditCommand.Execute(null);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    todoItem.CancelEditCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private void EditTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                var todoItem = textBox.DataContext as TodoItemViewModel;
                todoItem?.SaveEditCommand.Execute(null);
            }
        }

        private void TodoText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                var border = sender as Border;
                var todoVm = border?.DataContext as TodoItemViewModel;
                
                if (todoVm != null)
                {
                    todoVm.StartEditCommand.Execute(null);
                    
                    // Focus the edit box after UI updates
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var editBox = FindVisualChild<TextBox>(border, "EditTextBox");
                        if (editBox != null)
                        {
                            editBox.Focus();
                            editBox.SelectAll();
                            _logger.Debug($"[TodoPanelView] Edit box focused for: {todoVm.Text}");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                    
                    e.Handled = true;
                    _logger.Info($"[TodoPanelView] Double-click edit started for: {todoVm.Text}");
                }
            }
        }
        
        private T FindVisualChild<T>(DependencyObject parent, string name) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild && (string.IsNullOrEmpty(name) || (child as FrameworkElement)?.Name == name))
                {
                    return typedChild;
                }

                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }

            return null;
        }
        
        #region Tag Management (✨ TAG MVP)
        
        /// <summary>
        /// Initialize context menu event handlers for dynamic tag population.
        /// </summary>
        private void InitializeContextMenus()
        {
            try
            {
                var contextMenu = (ContextMenu)this.Resources["TodoContextMenu"];
                if (contextMenu != null)
                {
                    contextMenu.Opened += TodoContextMenu_Opened;
                    _logger.Info("[TodoPanelView] ✅ Context menu event wired for dynamic tags");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoPanelView] Failed to initialize context menus");
            }
        }
        
        /// <summary>
        /// Dynamically populate tag list in context menu when opened.
        /// Shows auto-tags (read-only, italic) and manual tags (clickable to remove).
        /// </summary>
        private void TodoContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            try
            {
                var menu = sender as ContextMenu;
                var todoVm = menu?.DataContext as TodoItemViewModel;
                
                if (todoVm == null || !todoVm.HasTags)
                {
                    _logger.Info("[TodoPanelView] Context menu opened but no tags to display");
                    return;
                }
                
                // Find the Tags submenu (we named it in XAML)
                var tagsMenuItem = menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header?.ToString() == "_Tags");
                if (tagsMenuItem == null)
                {
                    _logger.Info("[TodoPanelView] Tags menu item not found");
                    return;
                }
                
                // Remove any existing dynamic tag items (from previous opens)
                var itemsToRemove = tagsMenuItem.Items.OfType<MenuItem>()
                    .Where(m => m.Tag?.ToString() == "DynamicTag").ToList();
                foreach (var item in itemsToRemove)
                {
                    tagsMenuItem.Items.Remove(item);
                }
                
                // Find the separator and make it visible if we have tags
                var separator = tagsMenuItem.Items.OfType<Separator>()
                    .FirstOrDefault(s => s.Name == "TagListSeparator");
                
                // Insert dynamic items after "Add Tag..." menu item (index 1)
                int insertIndex = 1;
                
                // Add auto-tags section
                var autoTags = todoVm.AutoTags.ToList();
                if (autoTags.Any())
                {
                    if (separator != null) separator.Visibility = Visibility.Visible;
                    
                    var autoHeader = new MenuItem 
                    { 
                        Header = "Auto-tags:",
                        IsEnabled = false,
                        Tag = "DynamicTag",
                        FontStyle = FontStyles.Italic,
                        Foreground = System.Windows.Media.Brushes.Gray
                    };
                    tagsMenuItem.Items.Insert(insertIndex++, autoHeader);
                    
                    foreach (var tag in autoTags)
                    {
                        var tagItem = new MenuItem 
                        { 
                            Header = $"  • {tag}",
                            IsEnabled = false,
                            Tag = "DynamicTag",
                            FontStyle = FontStyles.Italic,
                            Foreground = System.Windows.Media.Brushes.Gray,
                            ToolTip = "Auto-generated from folder path (read-only)"
                        };
                        tagsMenuItem.Items.Insert(insertIndex++, tagItem);
                    }
                }
                
                // Add manual tags section
                var manualTags = todoVm.ManualTags.ToList();
                if (manualTags.Any())
                {
                    if (separator != null) separator.Visibility = Visibility.Visible;
                    
                    if (autoTags.Any())
                    {
                        // Add separator between auto and manual sections
                        var sectionSeparator = new Separator { Tag = "DynamicTag" };
                        tagsMenuItem.Items.Insert(insertIndex++, sectionSeparator);
                    }
                    
                    var manualHeader = new MenuItem 
                    { 
                        Header = "Manual tags:",
                        IsEnabled = false,
                        Tag = "DynamicTag"
                    };
                    tagsMenuItem.Items.Insert(insertIndex++, manualHeader);
                    
                    foreach (var tag in manualTags)
                    {
                        var tagItem = new MenuItem 
                        { 
                            Header = $"  • {tag}",
                            Tag = "DynamicTag",
                            ToolTip = "Click to remove this tag",
                            Command = todoVm.RemoveTagCommand,
                            CommandParameter = tag
                        };
                        tagsMenuItem.Items.Insert(insertIndex++, tagItem);
                    }
                }
                
                _logger.Info($"[TodoPanelView] Populated tag menu: {autoTags.Count} auto, {manualTags.Count} manual");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoPanelView] Error populating tag menu");
            }
        }
        
        /// <summary>
        /// Handle Add Tag menu click - show input dialog and add tag to selected todo.
        /// </summary>
        private void AddTag_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Info("[TodoPanelView] AddTag_Click");
                
                // Get the todo from the menu item's DataContext
                var menuItem = sender as MenuItem;
                var todoVm = menuItem?.DataContext as TodoItemViewModel;
                
                if (todoVm == null)
                {
                    _logger.Info("[TodoPanelView] AddTag_Click: No todo selected");
                    return;
                }
                
                // Show input dialog for tag name
                var dialog = new Window
                {
                    Title = "Add Tag",
                    Width = 300,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this)
                };
                
                var stackPanel = new StackPanel { Margin = new Thickness(10) };
                stackPanel.Children.Add(new TextBlock 
                { 
                    Text = "Enter tag name:", 
                    Margin = new Thickness(0, 0, 0, 5) 
                });
                
                var textBox = new TextBox 
                { 
                    Margin = new Thickness(0, 0, 0, 10) 
                };
                stackPanel.Children.Add(textBox);
                
                var buttonPanel = new StackPanel 
                { 
                    Orientation = Orientation.Horizontal, 
                    HorizontalAlignment = HorizontalAlignment.Right 
                };
                
                var okButton = new Button 
                { 
                    Content = "OK", 
                    Width = 75, 
                    Margin = new Thickness(0, 0, 5, 0),
                    IsDefault = true
                };
                okButton.Click += (s, args) => dialog.DialogResult = true;
                
                var cancelButton = new Button 
                { 
                    Content = "Cancel", 
                    Width = 75,
                    IsCancel = true
                };
                cancelButton.Click += (s, args) => dialog.DialogResult = false;
                
                buttonPanel.Children.Add(okButton);
                buttonPanel.Children.Add(cancelButton);
                stackPanel.Children.Add(buttonPanel);
                
                dialog.Content = stackPanel;
                
                textBox.Focus();
                
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(textBox.Text))
                {
                    var tagName = textBox.Text.Trim();
                    _logger.Info($"[TodoPanelView] Adding tag '{tagName}' to todo {todoVm.Id}");
                    
                    // Execute AddTag command
                    if (todoVm.AddTagCommand.CanExecute(tagName))
                    {
                        todoVm.AddTagCommand.Execute(tagName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoPanelView] Error in AddTag_Click");
            }
        }
        
        /// <summary>
        /// Handle Remove Tag menu click - show list of tags and remove selected one.
        /// </summary>
        private void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Info("[TodoPanelView] RemoveTag_Click");
                
                // Get the todo from the menu item's DataContext
                var menuItem = sender as MenuItem;
                var todoVm = menuItem?.DataContext as TodoItemViewModel;
                
                if (todoVm == null || !todoVm.HasTags)
                {
                    _logger.Info("[TodoPanelView] RemoveTag_Click: No todo selected or no tags");
                    return;
                }
                
                var tags = todoVm.Tags.ToList();
                
                // Show dialog to select tag to remove
                var dialog = new Window
                {
                    Title = "Remove Tag",
                    Width = 300,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this)
                };
                
                var stackPanel = new StackPanel { Margin = new Thickness(10) };
                stackPanel.Children.Add(new TextBlock 
                { 
                    Text = "Select tag to remove:", 
                    Margin = new Thickness(0, 0, 0, 5) 
                });
                
                var listBox = new ListBox 
                { 
                    Margin = new Thickness(0, 0, 0, 10),
                    Height = 100,
                    ItemsSource = tags
                };
                stackPanel.Children.Add(listBox);
                
                var buttonPanel = new StackPanel 
                { 
                    Orientation = Orientation.Horizontal, 
                    HorizontalAlignment = HorizontalAlignment.Right 
                };
                
                var okButton = new Button 
                { 
                    Content = "Remove", 
                    Width = 75, 
                    Margin = new Thickness(0, 0, 5, 0),
                    IsDefault = true
                };
                okButton.Click += (s, args) => dialog.DialogResult = true;
                
                var cancelButton = new Button 
                { 
                    Content = "Cancel", 
                    Width = 75,
                    IsCancel = true
                };
                cancelButton.Click += (s, args) => dialog.DialogResult = false;
                
                buttonPanel.Children.Add(okButton);
                buttonPanel.Children.Add(cancelButton);
                stackPanel.Children.Add(buttonPanel);
                
                dialog.Content = stackPanel;
                
                if (dialog.ShowDialog() == true && listBox.SelectedItem != null)
                {
                    var tagName = listBox.SelectedItem.ToString();
                    _logger.Info($"[TodoPanelView] Removing tag '{tagName}' from todo {todoVm.Id}");
                    
                    // Execute RemoveTag command
                    if (todoVm.RemoveTagCommand.CanExecute(tagName))
                    {
                        todoVm.RemoveTagCommand.Execute(tagName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoPanelView] Error in RemoveTag_Click");
            }
        }
        
        #endregion

        #region Folder Tagging (✨ HYBRID FOLDER TAGGING)

        private async void SetFolderTags_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menuItem = sender as MenuItem;
                var category = menuItem?.Tag as CategoryNodeViewModel;
                
                if (category == null)
                {
                    _logger.Warning("[TodoPanelView] SetFolderTags_Click: Category is null");
                    return;
                }

                var app = (App)System.Windows.Application.Current;
                var mediator = app.ServiceProvider?.GetService<MediatR.IMediator>();
                var folderTagRepo = app.ServiceProvider?.GetService<NoteNest.Application.FolderTags.Repositories.IFolderTagRepository>();

                if (mediator == null || folderTagRepo == null)
                {
                    MessageBox.Show("Required services not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var dialog = new NoteNest.UI.Windows.FolderTagDialog(
                    category.CategoryId, 
                    category.DisplayPath, 
                    mediator, 
                    folderTagRepo, 
                    _logger)
                {
                    Owner = Window.GetWindow(this)
                };

                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoPanelView] Error in SetFolderTags_Click");
                MessageBox.Show($"Error opening folder tag dialog: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RemoveFolderTags_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menuItem = sender as MenuItem;
                var category = menuItem?.Tag as CategoryNodeViewModel;
                
                if (category == null)
                {
                    _logger.Warning("[TodoPanelView] RemoveFolderTags_Click: Category is null");
                    return;
                }

                var result = MessageBox.Show(
                    $"Remove all tags from folder '{category.Name}'?\n\nExisting todos will keep their tags.",
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
                    FolderId = category.CategoryId
                };

                var commandResult = await mediator.Send(command);
                
                if (commandResult.Success)
                {
                    _logger.Info($"[TodoPanelView] Folder tags removed successfully for category {category.CategoryId}");
                    MessageBox.Show("Folder tags removed successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    _logger.Error($"[TodoPanelView] Failed to remove tags: {commandResult.Error}");
                    MessageBox.Show($"Failed to remove tags: {commandResult.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoPanelView] Error in RemoveFolderTags_Click");
                MessageBox.Show($"Error removing folder tags: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}
