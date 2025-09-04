using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

		// New read APIs and safe operations
		Task<List<TodoItem>> GetAllTasksAsync();
		List<TodoItem> GetAllTasks();
		Task<TodoItem> GetTaskByIdAsync(string id);
		Task<List<TodoItem>> GetTasksByCategoryAsync(string category);
		Task<TodoOperationResult<TodoItem>> AddTaskSafeAsync(string text, string category = "General");

		// Core event handlers (todo-linked updates)
		Task OnNoteMovedAsync(string noteId, string oldPath, string newPath);
		Task OnNoteRenamedAsync(string noteId, string oldPath, string newPath, string oldTitle, string newTitle);
		Task OnNoteDeletedAsync(string noteId, string filePath);
	}

	public class TodoService : ITodoService
	{
		private readonly IPluginDataStore _dataStore;
		private TodoStorage _storage;
		private readonly string _pluginId = "todo-plugin";
		private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
		private List<TodoItem> _snapshot = new List<TodoItem>();
		private DateTime _snapshotTime = DateTime.MinValue;

		public TodoService(IPluginDataStore dataStore)
		{
			_dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
		}

		public async Task<TodoStorage> LoadTasksAsync()
		{
			await _lock.WaitAsync();
			try
			{
				if (_storage != null) return _storage;
				_storage = await _dataStore.LoadDataAsync<TodoStorage>(_pluginId, "tasks");
				if (_storage == null)
				{
					_storage = new TodoStorage();
					_storage.AddCategory("General");
				}
				// Migrate storage version if needed
				if (_storage.Version < 2)
				{
					_storage.Version = 2;
				}
				await ProcessRecurringTasksInternalAsync();
				if (_storage.Settings.AutoDeleteCompleted)
				{
					await CleanupCompletedTasksInternalAsync();
				}
				RefreshSnapshot();
				return _storage;
			}
			finally
			{
				_lock.Release();
			}
		}

		public async Task SaveTasksAsync(TodoStorage storage = null)
		{
			await _lock.WaitAsync();
			try
			{
				if (storage != null) _storage = storage;
				if (_storage == null) return;
				_storage.LastModified = DateTime.Now;
				await _dataStore.SaveDataAsync(_pluginId, "tasks", _storage);
				RefreshSnapshot();
			}
			finally
			{
				_lock.Release();
			}
		}

		public async Task<TodoItem> AddTaskAsync(string text, string category = "General")
		{
			if (string.IsNullOrWhiteSpace(text)) return null;
			await _lock.WaitAsync();
			try
			{
				await EnsureLoadedInternalAsync();
				var task = new TodoItem
				{
					Text = text.Trim(),
					Category = category,
					Order = _storage.GetTasksByCategory(category).Count * 1000
				};
				_storage.AddTask(task);
				await _dataStore.SaveDataAsync(_pluginId, "tasks", _storage);
				RefreshSnapshot();
				return task;
			}
			finally
			{
				_lock.Release();
			}
		}

		public async Task<bool> UpdateTaskAsync(TodoItem task)
		{
			if (task == null) return false;
			await _lock.WaitAsync();
			try
			{
				await EnsureLoadedInternalAsync();
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
				await _dataStore.SaveDataAsync(_pluginId, "tasks", _storage);
				RefreshSnapshot();
				return true;
			}
			finally
			{
				_lock.Release();
			}
		}

		public async Task<bool> DeleteTaskAsync(string taskId)
		{
			await _lock.WaitAsync();
			try
			{
				await EnsureLoadedInternalAsync();
				var task = _storage.GetAllTasks().FirstOrDefault(t => t.Id == taskId);
				if (task == null) return false;
				_storage.RemoveTask(task);
				await _dataStore.SaveDataAsync(_pluginId, "tasks", _storage);
				RefreshSnapshot();
				return true;
			}
			finally
			{
				_lock.Release();
			}
		}

		public async Task<bool> CompleteTaskAsync(string taskId)
		{
			await _lock.WaitAsync();
			try
			{
				await EnsureLoadedInternalAsync();
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
				await _dataStore.SaveDataAsync(_pluginId, "tasks", _storage);
				RefreshSnapshot();
				return true;
			}
			finally
			{
				_lock.Release();
			}
		}

		public async Task ProcessRecurringTasksAsync()
		{
			await _lock.WaitAsync();
			try
			{
				await EnsureLoadedInternalAsync();
				await ProcessRecurringTasksInternalAsync();
				RefreshSnapshot();
			}
			finally
			{
				_lock.Release();
			}
		}

		public async Task CleanupCompletedTasksAsync()
		{
			await _lock.WaitAsync();
			try
			{
				await EnsureLoadedInternalAsync();
				await CleanupCompletedTasksInternalAsync();
				RefreshSnapshot();
			}
			finally
			{
				_lock.Release();
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
			RefreshSnapshot();
		}

		// New read APIs and safe operations
		public async Task<List<TodoItem>> GetAllTasksAsync()
		{
			await _lock.WaitAsync();
			try
			{
				await EnsureLoadedInternalAsync();
				return _storage.GetAllTasks().ToList();
			}
			finally
			{
				_lock.Release();
			}
		}

		public List<TodoItem> GetAllTasks()
		{
			return _snapshot.ToList();
		}

		public async Task<TodoItem> GetTaskByIdAsync(string id)
		{
			await _lock.WaitAsync();
			try
			{
				await EnsureLoadedInternalAsync();
				return _storage.GetAllTasks().FirstOrDefault(t => t.Id == id);
			}
			finally
			{
				_lock.Release();
			}
		}

		public async Task<List<TodoItem>> GetTasksByCategoryAsync(string category)
		{
			await _lock.WaitAsync();
			try
			{
				await EnsureLoadedInternalAsync();
				return _storage.GetTasksByCategory(category).ToList();
			}
			finally
			{
				_lock.Release();
			}
		}

		public async Task<TodoOperationResult<TodoItem>> AddTaskSafeAsync(string text, string category = "General")
		{
			try
			{
				if (string.IsNullOrWhiteSpace(text))
					return TodoOperationResult<TodoItem>.Fail("Task text is required");
				if (text.Length > 500)
					return TodoOperationResult<TodoItem>.Fail("Task text too long (max 500)");
				var task = await AddTaskAsync(text, category);
				return TodoOperationResult<TodoItem>.Ok(task);
			}
			catch (Exception)
			{
				return TodoOperationResult<TodoItem>.Fail("Failed to add task");
			}
		}

		public async Task OnNoteMovedAsync(string noteId, string oldPath, string newPath)
		{
			await _lock.WaitAsync();
			try
			{
				await EnsureLoadedInternalAsync();
				var changed = false;
				foreach (var t in _storage.GetAllTasks())
				{
					if (!string.IsNullOrWhiteSpace(t.LinkedNoteId) && string.Equals(t.LinkedNoteId, noteId, StringComparison.OrdinalIgnoreCase))
					{
						t.LinkedNoteFilePath = newPath;
						changed = true;
					}
				}
				if (changed)
				{
					await _dataStore.SaveDataAsync(_pluginId, "tasks", _storage);
					RefreshSnapshot();
				}
			}
			finally
			{
				_lock.Release();
			}
		}

		public async Task OnNoteRenamedAsync(string noteId, string oldPath, string newPath, string oldTitle, string newTitle)
		{
			await OnNoteMovedAsync(noteId, oldPath, newPath);
		}

		public async Task OnNoteDeletedAsync(string noteId, string filePath)
		{
			await _lock.WaitAsync();
			try
			{
				await EnsureLoadedInternalAsync();
				var changed = false;
				foreach (var t in _storage.GetAllTasks())
				{
					if (!string.IsNullOrWhiteSpace(t.LinkedNoteId) && string.Equals(t.LinkedNoteId, noteId, StringComparison.OrdinalIgnoreCase))
					{
						// Keep ID for potential recovery; clear path
						t.LinkedNoteFilePath = null;
						changed = true;
					}
				}
				if (changed)
				{
					await _dataStore.SaveDataAsync(_pluginId, "tasks", _storage);
					RefreshSnapshot();
				}
			}
			finally
			{
				_lock.Release();
			}
		}

		private async Task EnsureLoadedInternalAsync()
		{
			if (_storage == null)
			{
				_storage = await _dataStore.LoadDataAsync<TodoStorage>(_pluginId, "tasks");
				if (_storage == null)
				{
					_storage = new TodoStorage();
					_storage.AddCategory("General");
				}
			}
		}

		private async Task ProcessRecurringTasksInternalAsync()
		{
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
			if (changed)
			{
				await _dataStore.SaveDataAsync(_pluginId, "tasks", _storage);
			}
		}

		private async Task CleanupCompletedTasksInternalAsync()
		{
			if (!_storage.Settings.AutoDeleteCompleted) return;
			var cutoff = DateTime.Now.AddDays(-_storage.Settings.AutoDeleteAfterDays);
			var toDelete = _storage.GetAllTasks()
				.Where(t => t.IsCompleted && t.CompletedDate.HasValue && t.CompletedDate.Value < cutoff)
				.ToList();
			if (toDelete.Any())
			{
				foreach (var t in toDelete) _storage.RemoveTask(t);
				await _dataStore.SaveDataAsync(_pluginId, "tasks", _storage);
			}
		}

		private void RefreshSnapshot()
		{
			// Ensure Version bump and forward-compat fields remain serialized
			if (_storage != null && _storage.Version < 2)
			{
				_storage.Version = 2; // Link fields introduced
			}
			_snapshot = _storage?.GetAllTasks().ToList() ?? new List<TodoItem>();
			_snapshotTime = DateTime.UtcNow;
		}
	}
}


