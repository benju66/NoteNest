using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Plugins;
using NoteNest.UI.Plugins.Todo.Models;

namespace NoteNest.UI.Plugins.Todo.Services
{
	public interface ITodoService
	{
		Task<TodoStorage> LoadTasksAsync();
		Task SaveTasksAsync(TodoStorage storage = null);
		Task<TodoItem> AddTaskAsync(string text, string category = "General");
		Task<bool> UpdateTaskAsync(TodoItem task);
		Task<bool> DeleteTaskAsync(string taskId);

		Task<bool> CompleteTaskAsync(string taskId);
		Task ProcessRecurringTasksAsync();
		Task CleanupCompletedTasksAsync();
		List<string> GetCategories();
		void SyncCategories(IEnumerable<string> noteCategories);
	}

	public class TodoService : ITodoService
	{
		private readonly IPluginDataStore _dataStore;
		private TodoStorage _storage;
		private readonly string _pluginId = "todo-plugin";

		public TodoService(IPluginDataStore dataStore)
		{
			_dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
		}

		public async Task<TodoStorage> LoadTasksAsync()
		{
			if (_storage != null) return _storage;
			_storage = await _dataStore.LoadDataAsync<TodoStorage>(_pluginId, "tasks");
			if (_storage == null)
			{
				_storage = new TodoStorage();
				_storage.AddCategory("General");
			}
			await ProcessRecurringTasksAsync();
			if (_storage.Settings.AutoDeleteCompleted)
			{
				await CleanupCompletedTasksAsync();
			}
			return _storage;
		}

		public async Task SaveTasksAsync(TodoStorage storage = null)
		{
			if (storage != null) _storage = storage;
			if (_storage == null) return;
			_storage.LastModified = DateTime.Now;
			await _dataStore.SaveDataAsync(_pluginId, "tasks", _storage);
		}

		public async Task<TodoItem> AddTaskAsync(string text, string category = "General")
		{
			if (string.IsNullOrWhiteSpace(text)) return null;
			await LoadTasksAsync();
			var task = new TodoItem
			{
				Text = text.Trim(),
				Category = category,
				Order = _storage.GetTasksByCategory(category).Count
			};
			_storage.AddTask(task);
			await SaveTasksAsync();
			return task;
		}

		public async Task<bool> UpdateTaskAsync(TodoItem task)
		{
			if (task == null) return false;
			await LoadTasksAsync();
			var existing = _storage.GetAllTasks().FirstOrDefault(t => t.Id == task.Id);
			if (existing == null) return false;
			existing.Text = task.Text;
			existing.IsCompleted = task.IsCompleted;
			existing.DueDate = task.DueDate;
			existing.Priority = task.Priority;
			existing.Recurrence = task.Recurrence;
			existing.Notes = task.Notes;
			if (existing.Category != task.Category)
			{
				_storage.MoveTask(existing, task.Category);
			}
			await SaveTasksAsync();
			return true;
		}

		public async Task<bool> DeleteTaskAsync(string taskId)
		{
			await LoadTasksAsync();
			var task = _storage.GetAllTasks().FirstOrDefault(t => t.Id == taskId);
			if (task == null) return false;
			_storage.RemoveTask(task);
			await SaveTasksAsync();
			return true;
		}

		public async Task<bool> CompleteTaskAsync(string taskId)
		{
			await LoadTasksAsync();
			var task = _storage.GetAllTasks().FirstOrDefault(t => t.Id == taskId);
			if (task == null) return false;
			task.IsCompleted = !task.IsCompleted;
			if (task.IsCompleted && task.IsRecurring && task.Recurrence.ShouldRecur())
			{
				var nextTask = task.Clone();
				nextTask.UpdateFromRecurrence();
				_storage.AddTask(nextTask);
				task.Recurrence.CurrentOccurrence++;
			}
			await SaveTasksAsync();
			return true;
		}

		public async Task ProcessRecurringTasksAsync()
		{
			await LoadTasksAsync();
			var recurring = _storage.GetAllTasks().Where(t => t.IsRecurring && t.IsCompleted).ToList();
			var changed = false;
			foreach (var t in recurring)
			{
				if (t.Recurrence.ShouldRecur())
				{
					var nextDate = t.Recurrence.GetNextDate(t.DueDate ?? DateTime.Today);
					if (nextDate <= DateTime.Today)
					{
						var nextTask = t.Clone();
						nextTask.UpdateFromRecurrence();
						_storage.AddTask(nextTask);
						changed = true;
					}
				}
			}
			if (changed) await SaveTasksAsync();
		}

		public async Task CleanupCompletedTasksAsync()
		{
			await LoadTasksAsync();
			if (!_storage.Settings.AutoDeleteCompleted) return;
			var cutoff = DateTime.Now.AddDays(-_storage.Settings.AutoDeleteAfterDays);
			var toDelete = _storage.GetAllTasks()
				.Where(t => t.IsCompleted && t.CompletedDate.HasValue && t.CompletedDate.Value < cutoff)
				.ToList();
			if (toDelete.Any())
			{
				foreach (var t in toDelete) _storage.RemoveTask(t);
				await SaveTasksAsync();
			}
		}

		public List<string> GetCategories()
		{
			if (_storage == null) return new List<string> { "General" };
			return _storage.Categories.Keys.OrderBy(c => c).ToList();
		}

		public void SyncCategories(IEnumerable<string> noteCategories)
		{
			if (_storage == null || noteCategories == null) return;
			foreach (var c in noteCategories)
			{
				if (!string.IsNullOrWhiteSpace(c)) _storage.AddCategory(c);
			}
			var empty = _storage.Categories
				.Where(kvp => kvp.Key != "General" && !kvp.Value.Any() && !noteCategories.Contains(kvp.Key))
				.Select(kvp => kvp.Key)
				.ToList();
			foreach (var c in empty) _storage.RemoveCategory(c);
		}
	}
}


