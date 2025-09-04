using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.UI.Plugins.Todo.Services;
using NoteNest.UI.Plugins.Todo.Models;
using NoteNest.Core.Plugins;

namespace NoteNest.UI.Services
{
	public class IntegrityIssue
	{
		public string Type { get; set; }
		public string Description { get; set; }
		public TodoItem Task { get; set; }
	}

	public class IntegrityCheckerService
	{
		private readonly ITodoService _todoService;
		private readonly IPluginDataStore _dataStore;
		private const string PluginId = "todo-plugin";

		public IntegrityCheckerService(ITodoService todoService, IPluginDataStore dataStore)
		{
			_todoService = todoService;
			_dataStore = dataStore;
		}

		public async Task<List<IntegrityIssue>> ScanAsync()
		{
			var issues = new List<IntegrityIssue>();
			var tasks = await _todoService.GetAllTasksAsync();
			var ignored = await LoadIgnoredAsync();
			foreach (var t in tasks)
			{
				if (ignored.Contains(t.Id)) continue;
				if (!string.IsNullOrWhiteSpace(t.LinkedNoteId) && string.IsNullOrWhiteSpace(t.LinkedNoteFilePath))
				{
					issues.Add(new IntegrityIssue
					{
						Type = "BrokenNoteLink",
						Description = $"Task '{t.Text}' has a note link that cannot be resolved.",
						Task = t
					});
				}
			}
			return issues;
		}

		public async Task<bool> ClearLinkAsync(TodoItem task)
		{
			if (task == null) return false;
			task.LinkedNoteId = null;
			task.LinkedNoteFilePath = null;
			return await _todoService.UpdateTaskAsync(task);
		}

		public async Task<bool> IgnoreTaskAsync(string taskId)
		{
			if (string.IsNullOrWhiteSpace(taskId)) return false;
			var settings = await _dataStore.LoadSettingsAsync(PluginId) ?? new System.Collections.Generic.Dictionary<string, object>();
			var list = ExtractIgnored(settings);
			if (!list.Contains(taskId)) list.Add(taskId);
			settings["IgnoredTaskIds"] = list;
			await _dataStore.SaveSettingsAsync(PluginId, settings);
			return true;
		}

		public async Task<bool> UnignoreTaskAsync(string taskId)
		{
			if (string.IsNullOrWhiteSpace(taskId)) return false;
			var settings = await _dataStore.LoadSettingsAsync(PluginId) ?? new System.Collections.Generic.Dictionary<string, object>();
			var list = ExtractIgnored(settings);
			if (list.Remove(taskId))
			{
				settings["IgnoredTaskIds"] = list;
				await _dataStore.SaveSettingsAsync(PluginId, settings);
				return true;
			}
			return false;
		}

		private async Task<System.Collections.Generic.HashSet<string>> LoadIgnoredAsync()
		{
			var settings = await _dataStore.LoadSettingsAsync(PluginId) ?? new System.Collections.Generic.Dictionary<string, object>();
			var list = ExtractIgnored(settings);
			return new System.Collections.Generic.HashSet<string>(list, System.StringComparer.OrdinalIgnoreCase);
		}

		private static System.Collections.Generic.List<string> ExtractIgnored(System.Collections.Generic.Dictionary<string, object> settings)
		{
			if (settings.TryGetValue("IgnoredTaskIds", out var ig) && ig is System.Collections.IEnumerable seq)
			{
				var outList = new System.Collections.Generic.List<string>();
				foreach (var o in seq)
				{
					var s = o?.ToString();
					if (!string.IsNullOrWhiteSpace(s)) outList.Add(s);
				}
				return outList.Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
			}
			return new System.Collections.Generic.List<string>();
		}
	}
}


