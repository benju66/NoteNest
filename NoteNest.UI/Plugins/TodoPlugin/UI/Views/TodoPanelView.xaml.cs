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
        public TodoPanelView(TodoListViewModel viewModel)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🎨 TodoPanelView constructor called");
                
                InitializeComponent();
                System.Diagnostics.Debug.WriteLine("🎨 InitializeComponent completed");
                
                DataContext = viewModel;
                System.Diagnostics.Debug.WriteLine($"🎨 DataContext set: {viewModel != null}");
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
                if (DataContext is TodoListViewModel viewModel && viewModel.QuickAddCommand.CanExecute(null))
                {
                    viewModel.QuickAddCommand.Execute(null);
                    e.Handled = true;
                }
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
