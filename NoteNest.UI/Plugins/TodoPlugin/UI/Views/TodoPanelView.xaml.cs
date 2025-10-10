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
                        
                        // NUCLEAR OPTION: Show dialog with category info
                        try
                        {
                            var categories = viewModel.CategoryTree.Categories;
                            var categoryList = new System.Text.StringBuilder();
                            foreach (var cat in categories)
                            {
                                categoryList.AppendLine(cat.DisplayPath);
                            }
                            
                            System.Windows.MessageBox.Show(
                                $"CATEGORIES COLLECTION CHANGED!\n\nCount: {categories.Count}\n\nCategories:\n{categoryList}",
                                "Category Debug",
                                System.Windows.MessageBoxButton.OK);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to show debug dialog");
                        }
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

        // Removed TreeView selection handler - using ListBox now

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
    }
}
