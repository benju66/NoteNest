using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NoteNest.Plugins.TodoPlugin.UI.ViewModels;

namespace NoteNest.Plugins.TodoPlugin.UI.Views
{
    /// <summary>
    /// Interaction logic for TodoPanelView.xaml
    /// </summary>
    public partial class TodoPanelView : UserControl
    {
        public TodoPanelView()
        {
            InitializeComponent();
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
