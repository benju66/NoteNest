using System;
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
    }
}
