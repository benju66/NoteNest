using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NoteNest.UI.Plugins.Todo.Models;
using NoteNest.UI.Plugins.Todo.Services;

namespace NoteNest.UI.Plugins.Todo.UI
{
	public partial class TodoPanel : UserControl, INotifyPropertyChanged
	{
		private readonly ITodoService _todoService;
		private ObservableCollection<TaskCategoryViewModel> _taskCategories;

		public ObservableCollection<TaskCategoryViewModel> TaskCategories
		{
			get => _taskCategories;
			set { _taskCategories = value; OnPropertyChanged(); }
		}

		public int ActiveTaskCount => TaskCategories?.Sum(c => c.Tasks.Count(t => !t.IsCompleted)) ?? 0;
		public bool ShowCompleted { get; set; } = true;

		public TodoPanel(ITodoService todoService)
		{
			InitializeComponent();
			DataContext = this;
			_todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
			TaskCategories = new ObservableCollection<TaskCategoryViewModel>();
			LoadTasks();
		}

		public async void LoadTasks()
		{
			var storage = await _todoService.LoadTasksAsync();
			TaskCategories.Clear();
			foreach (var kvp in storage.Categories.OrderBy(k => k.Key))
			{
				TaskCategories.Add(new TaskCategoryViewModel
				{
					Name = kvp.Key,
					Tasks = new ObservableCollection<TodoItem>((ShowCompleted ? kvp.Value : kvp.Value.Where(t => !t.IsCompleted)).OrderBy(t => t.IsCompleted).ThenBy(t => t.Order))
				});
			}
			OnPropertyChanged(nameof(ActiveTaskCount));
		}

		private async void QuickAdd_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(QuickAddTextBox.Text))
			{
				await AddTask();
			}
		}

		private async void AddButton_Click(object sender, RoutedEventArgs e)
		{
			await AddTask();
		}

		private async Task AddTask()
		{
			var text = QuickAddTextBox.Text?.Trim();
			if (string.IsNullOrWhiteSpace(text)) return;
			var category = ParseCategory(ref text);
			var (priority, due) = ParsePriorityAndDue(ref text);
			var task = await _todoService.AddTaskAsync(text, category);
			if (task != null)
			{
				task.Priority = priority;
				task.DueDate = due;
				await _todoService.UpdateTaskAsync(task);
				QuickAddTextBox.Clear();
				LoadTasks();
			}
		}

		private string ParseCategory(ref string text)
		{
			var category = "General";
			var at = text.IndexOf('@');
			if (at >= 0)
			{
				var end = text.IndexOf(' ', at);
				var tag = end > at ? text.Substring(at + 1, end - at - 1) : text[(at + 1)..];
				if (!string.IsNullOrWhiteSpace(tag))
				{
					category = tag.Trim();
					text = (text.Remove(at, Math.Min(text.Length - at, tag.Length + 1))).Trim();
				}
			}
			return category;
		}

		private (TodoPriority priority, DateTime? due) ParsePriorityAndDue(ref string text)
		{
			var priority = TodoPriority.Normal;
			DateTime? due = null;
			if (text.StartsWith("!!!")) { priority = TodoPriority.Urgent; text = text[3..].Trim(); }
			else if (text.StartsWith("!!")) { priority = TodoPriority.High; text = text[2..].Trim(); }
			else if (text.StartsWith("!")) { priority = TodoPriority.High; text = text[1..].Trim(); }
			if (text.Contains("tomorrow", StringComparison.OrdinalIgnoreCase)) { due = DateTime.Today.AddDays(1); text = text.Replace("tomorrow", "", StringComparison.OrdinalIgnoreCase).Trim(); }
			else if (text.Contains("today", StringComparison.OrdinalIgnoreCase)) { due = DateTime.Today; text = text.Replace("today", "", StringComparison.OrdinalIgnoreCase).Trim(); }
			return (priority, due);
		}

		private async void ClearCompleted_Click(object sender, RoutedEventArgs e)
		{
			await _todoService.CleanupCompletedTasksAsync();
			LoadTasks();
		}

		// Basic drag-over handler for tasks
		private void TaskItem_DragOver(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(typeof(TodoItem))) { e.Effects = DragDropEffects.Move; e.Handled = true; }
		}

		// Basic drop handler: pushes dropped item to end of its category for now
		private async void TaskItem_Drop(object sender, DragEventArgs e)
		{
			if (!e.Data.GetDataPresent(typeof(TodoItem))) return;
			var dropped = e.Data.GetData(typeof(TodoItem)) as TodoItem;
			if (dropped == null) return;
			// If dropping on category header, move to that category
			if (sender is FrameworkElement fe && fe.DataContext is TaskCategoryViewModel cat)
			{
				dropped.Category = cat.Name;
				dropped.Order = int.MaxValue;
			}
			else
			{
				dropped.Order = int.MaxValue;
			}
			await _todoService.UpdateTaskAsync(dropped);
			LoadTasks();
		}

		private Point _dragStart;
		private void TaskItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			_dragStart = e.GetPosition(null);
		}

		private void TaskItem_PreviewMouseMove(object sender, MouseEventArgs e)
		{
			if (e.LeftButton != MouseButtonState.Pressed) return;
			var pos = e.GetPosition(null);
			if (Math.Abs(pos.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(pos.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
			{
				if (sender is FrameworkElement fe && fe.DataContext is TodoItem t)
				{
					DragDrop.DoDragDrop(fe, new DataObject(typeof(TodoItem), t), DragDropEffects.Move);
				}
			}
		}

		private async void TaskCheckBox_Checked(object sender, RoutedEventArgs e)
		{
			if (sender is CheckBox cb && cb.DataContext is TodoItem t)
			{
				await _todoService.CompleteTaskAsync(t.Id);
				LoadTasks();
			}
		}

		private async void TaskCheckBox_Unchecked(object sender, RoutedEventArgs e)
		{
			if (sender is CheckBox cb && cb.DataContext is TodoItem t)
			{
				await _todoService.CompleteTaskAsync(t.Id);
				LoadTasks();
			}
		}

		private void TaskMenu_Click(object sender, RoutedEventArgs e)
		{
			if (sender is Button b && b.ContextMenu != null)
			{
				b.ContextMenu.DataContext = b.DataContext;
				b.ContextMenu.IsOpen = true;
			}
		}

		private async void DeleteTask_Click(object sender, RoutedEventArgs e)
		{
			if (sender is MenuItem mi && mi.DataContext is TodoItem task)
			{
				await _todoService.DeleteTaskAsync(task.Id);
				LoadTasks();
			}
		}

		private async void EditTask_Click(object sender, RoutedEventArgs e)
		{
			if (sender is MenuItem mi && mi.DataContext is TodoItem task)
			{
				var dialog = new TaskEditDialog { Owner = Application.Current.MainWindow };
				dialog.DataContext = new {
					Text = task.Text,
					Category = task.Category,
					Priority = task.Priority,
					DueDate = task.DueDate,
					Notes = task.Notes,
					Categories = TaskCategories.Select(c => c.Name).ToList()
				};
				try
				{
					var result = await dialog.ShowAsync();
					if (result == ModernWpf.Controls.ContentDialogResult.Primary)
					{
						// Pull updated values back from dialog
						task.Text = ((dynamic)dialog.DataContext).Text;
						task.Category = ((dynamic)dialog.DataContext).Category;
						task.Priority = ((dynamic)dialog.DataContext).Priority;
						task.DueDate = ((dynamic)dialog.DataContext).DueDate;
						task.Notes = ((dynamic)dialog.DataContext).Notes;
						await _todoService.UpdateTaskAsync(task);
						LoadTasks();
					}
				}
				catch { }
			}
		}

		private void SetDueDate_Click(object sender, RoutedEventArgs e)
		{
			// Placeholder: open date picker in later step
		}

		private void SetPriority_Click(object sender, RoutedEventArgs e)
		{
			// Placeholder: open priority selector in later step
		}

		private void ViewSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			// TODO: filter views
		}

		public event PropertyChangedEventHandler PropertyChanged;
		protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}

	public class TaskCategoryViewModel : INotifyPropertyChanged
	{
		private string _name;
		private ObservableCollection<TodoItem> _tasks;

		public string Name
		{
			get => _name;
			set { _name = value; OnPropertyChanged(); }
		}

		public ObservableCollection<TodoItem> Tasks
		{
			get => _tasks;
			set { _tasks = value; OnPropertyChanged(); OnPropertyChanged(nameof(TaskCount)); }
		}

		public int TaskCount => Tasks?.Count(t => !t.IsCompleted) ?? 0;

		public event PropertyChangedEventHandler PropertyChanged;
		protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}


