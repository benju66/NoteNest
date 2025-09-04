using System;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Plugins;
using NoteNest.UI.Plugins.Todo.Models;
using NoteNest.UI.Plugins.Todo.Services;
using NUnit.Framework;

namespace NoteNest.Tests.Services
{
	public class InMemoryPluginDataStore : IPluginDataStore
	{
		private readonly System.Collections.Generic.Dictionary<string, object> _store = new();
		public Task<T> LoadDataAsync<T>(string pluginId, string key) where T : class
		{
			var composite = pluginId + ":" + key;
			if (_store.TryGetValue(composite, out var obj) && obj is T t) return Task.FromResult(t);
			return Task.FromResult<T>(null);
		}
		public Task SaveDataAsync<T>(string pluginId, string key, T data) where T : class
		{
			var composite = pluginId + ":" + key;
			_store[composite] = data!;
			return Task.CompletedTask;
		}

		public Task<System.Collections.Generic.Dictionary<string, object>> LoadSettingsAsync(string pluginId)
		{
			return Task.FromResult(new System.Collections.Generic.Dictionary<string, object>());
		}

		public Task SaveSettingsAsync(string pluginId, System.Collections.Generic.Dictionary<string, object> settings)
		{
			return Task.CompletedTask;
		}

		public Task<bool> DeleteDataAsync(string pluginId, string key)
		{
			var composite = pluginId + ":" + key;
			var removed = _store.Remove(composite);
			return Task.FromResult(removed);
		}

		public Task<bool> BackupPluginDataAsync(string pluginId) => Task.FromResult(true);
		public Task<bool> RestorePluginDataAsync(string pluginId, DateTime backupDate) => Task.FromResult(true);
	}

	public class TodoServiceTests
	{
		private static TodoService CreateService(out InMemoryPluginDataStore store)
		{
			store = new InMemoryPluginDataStore();
			return new TodoService(store);
		}

		[Test]
		public async Task Migration_Sets_Version_AtLeast_2()
		{
			var svc = CreateService(out var store);
			// Save a v1 storage
			await store.SaveDataAsync("todo-plugin", "tasks", new TodoStorage { Version = 1 });
			var loaded = await svc.LoadTasksAsync();
			Assert.That(loaded.Version, Is.GreaterThanOrEqualTo(2));
		}

		[Test]
		public async Task NoteMoved_Updates_LinkedNoteFilePath()
		{
			var svc = CreateService(out var store);
			var s = await svc.LoadTasksAsync();
			var t = await svc.AddTaskAsync("test", "General");
			t.LinkedNoteId = "id-1";
			t.LinkedNoteFilePath = "old.md";
			await svc.UpdateTaskAsync(t);

			await svc.OnNoteMovedAsync("id-1", "old.md", "new.md");
			var all = await svc.GetAllTasksAsync();
			Assert.That(all.First().LinkedNoteFilePath, Is.EqualTo("new.md"));
		}

		[Test]
		public async Task NoteDeleted_Clears_LinkedPath_But_Keeps_Id()
		{
			var svc = CreateService(out var store);
			await svc.LoadTasksAsync();
			var t = await svc.AddTaskAsync("test", "General");
			t.LinkedNoteId = "id-2";
			t.LinkedNoteFilePath = "file.md";
			await svc.UpdateTaskAsync(t);

			await svc.OnNoteDeletedAsync("id-2", "file.md");
			var all = await svc.GetAllTasksAsync();
			Assert.That(all.First().LinkedNoteFilePath, Is.Null);
			Assert.That(all.First().LinkedNoteId, Is.EqualTo("id-2"));
		}

		[Test]
		public async Task CategoryRenamed_Renames_Or_Merges_Bucket()
		{
			var svc = CreateService(out var store);
			var st = await svc.LoadTasksAsync();
			await svc.AddTaskAsync("a", "Work");
			await svc.AddTaskAsync("b", "Work");
			await svc.OnCategoryRenamedAsync("Work", "Projects");
			var all = await svc.GetAllTasksAsync();
			Assert.That(all.All(x => x.Category == "Projects"), Is.True);
		}
	}
}
