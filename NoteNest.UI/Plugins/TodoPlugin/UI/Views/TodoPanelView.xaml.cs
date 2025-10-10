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
        public TodoPanelView(TodoPanelViewModel viewModel)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🎨 TodoPanelView constructor called");
                
                InitializeComponent();
                System.Diagnostics.Debug.WriteLine("🎨 InitializeComponent completed");
                
                DataContext = viewModel;
                System.Diagnostics.Debug.WriteLine($"🎨 DataContext set to TodoPanelViewModel: {viewModel != null}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ TodoPanelView constructor failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
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
            if (DataContext is TodoPanelViewModel panelVm && e.NewValue is CategoryNodeViewModel categoryNode)
            {
                panelVm.CategoryTree.SelectedCategory = categoryNode;
            }
        }

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
