using System;
using System.Threading.Tasks;
using System.Windows;
using NoteNest.Core.Plugins;
using NoteNest.Core.Services;
using System.Linq;
using NoteNest.UI.Plugins.Todo.Services;
using NoteNest.UI.Plugins.Todo.UI;

namespace NoteNest.UI.Plugins.Todo
{
	public class TodoPlugin : PluginBase
	{
		private ITodoService _todoService;
		private TodoPluginPanel _panel;
		private TodoPluginSettings _settings;
		private IEventBus _eventBus;

		public override string Id => "todo-plugin";
		public override string Name => "Todo List";
		public override string Icon => "✓";
		public override Version Version => new Version(1, 0, 0);
		public override string Description => "Manage tasks and to-do lists";

		public TodoPlugin(ITodoService todoService)
		{
			_todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
			_settings = new TodoPluginSettings();
		}

		protected override async Task OnInitializeAsync()
		{
			await _todoService.LoadTasksAsync();
			var panel = new TodoPanel(_todoService);
			_panel = new TodoPluginPanel(panel);
			// Subscribe to core note events to keep linked tasks in sync
			try
			{
				var bus = (Application.Current as NoteNest.UI.App)?.ServiceProvider?.GetService(typeof(IEventBus)) as IEventBus;
				if (bus != null)
				{
					_eventBus = bus;
					_eventBus.Subscribe<NoteNest.Core.Events.NoteMovedEvent>(async e => { try { await _todoService.OnNoteMovedAsync(e.NoteId, e.OldPath, e.NewPath); } catch { } });
					_eventBus.Subscribe<NoteNest.Core.Events.NoteRenamedEvent>(async e => { try { await _todoService.OnNoteRenamedAsync(e.NoteId, e.OldPath, e.NewPath, e.OldTitle, e.NewTitle); } catch { } });
					_eventBus.Subscribe<NoteNest.Core.Events.NoteDeletedEvent>(async e => { try { await _todoService.OnNoteDeletedAsync(e.NoteId, e.FilePath); } catch { } });
					_eventBus.Subscribe<NoteNest.Core.Events.CategoryRenamedEvent>(async e =>
					{
						try { await _todoService.OnCategoryRenamedAsync(e.OldName, e.NewName); } catch { }
					});
				}
			}
			catch { }
			// Background timer for recurring tasks (hourly)
			var _ = Task.Run(async () =>
			{
				while (true)
				{
					try { await _todoService.ProcessRecurringTasksAsync(); } catch { }
					await Task.Delay(TimeSpan.FromHours(1));
				}
			});
		}

		protected override async Task OnShutdownAsync()
		{
			await _todoService.SaveTasksAsync();
			_panel = null;
		}

		protected override void OnActivate()
		{
			_panel?.FocusQuickAdd();
		}

		public override IPluginPanel GetPanel() => _panel;
		public override IPluginSettings GetSettings() => _settings;
	}

	public class TodoPluginPanel : IPluginPanel
	{
		private readonly TodoPanel _todoPanel;
		public object Content => _todoPanel;
		public double PreferredWidth => 350;
		public double MinWidth => 250;
		public double MaxWidth => 500;
		public bool IsVisible { get; set; }

		public TodoPluginPanel(TodoPanel todoPanel)
		{
			_todoPanel = todoPanel ?? throw new ArgumentNullException(nameof(todoPanel));
		}

		public void OnPanelOpened()
		{
			IsVisible = true;
			_todoPanel.LoadTasks();
		}

		public void OnPanelClosed()
		{
			IsVisible = false;
		}

		public void Refresh()
		{
			_todoPanel.LoadTasks();
		}

		public void FocusQuickAdd()
		{
			_todoPanel?.Focus();
		}
	}

	public class TodoPluginSettings : IPluginSettings
	{
		public bool ShowCompletedTasks { get; set; } = true;
		public bool AutoDeleteCompleted { get; set; } = false;
		public int AutoDeleteAfterDays { get; set; } = 30;
		public bool ShowLinkErrorToasts { get; set; } = true;
		public System.Collections.Generic.List<string> IgnoredTaskIds { get; set; } = new System.Collections.Generic.List<string>();

		public System.Collections.Generic.Dictionary<string, object> ToDictionary()
		{
			return new System.Collections.Generic.Dictionary<string, object>
			{
				[nameof(ShowCompletedTasks)] = ShowCompletedTasks,
				[nameof(AutoDeleteCompleted)] = AutoDeleteCompleted,
				[nameof(AutoDeleteAfterDays)] = AutoDeleteAfterDays,
				[nameof(ShowLinkErrorToasts)] = ShowLinkErrorToasts,
				[nameof(IgnoredTaskIds)] = IgnoredTaskIds
			};
		}

		public void FromDictionary(System.Collections.Generic.Dictionary<string, object> settings)
		{
			if (settings == null) return;

			bool TryGetBool(string key, out bool value)
			{
				value = false;
				if (!settings.TryGetValue(key, out var obj))
				{
					var kv = settings.FirstOrDefault(kvp => string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase));
					obj = string.IsNullOrEmpty(kv.Key) ? null : kv.Value;
				}
				if (obj is null) return false;
				if (obj is bool b) { value = b; return true; }
				if (obj is string s && bool.TryParse(s, out var bs)) { value = bs; return true; }
				if (obj is System.Text.Json.JsonElement je)
				{
					if (je.ValueKind == System.Text.Json.JsonValueKind.True) { value = true; return true; }
					if (je.ValueKind == System.Text.Json.JsonValueKind.False) { value = false; return true; }
					if (je.ValueKind == System.Text.Json.JsonValueKind.String && bool.TryParse(je.GetString(), out var jb)) { value = jb; return true; }
				}
				return false;
			}

			int TryGetInt(string key, int fallback)
			{
				if (!settings.TryGetValue(key, out var obj))
				{
					var kv = settings.FirstOrDefault(kvp => string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase));
					obj = string.IsNullOrEmpty(kv.Key) ? null : kv.Value;
				}
				if (obj is null) return fallback;
				if (obj is int i) return i;
				if (obj is long l) return (int)l;
				if (obj is string s && int.TryParse(s, out var si)) return si;
				if (obj is System.Text.Json.JsonElement je)
				{
					if (je.ValueKind == System.Text.Json.JsonValueKind.Number && je.TryGetInt32(out var ji)) return ji;
					if (je.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(je.GetString(), out var js)) return js;
				}
				return fallback;
			}

			System.Collections.Generic.List<string> TryGetStringList(string key)
			{
				var result = new System.Collections.Generic.List<string>();
				if (!settings.TryGetValue(key, out var obj))
				{
					var kv = settings.FirstOrDefault(kvp => string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase));
					obj = string.IsNullOrEmpty(kv.Key) ? null : kv.Value;
				}
				if (obj is System.Collections.Generic.IEnumerable<object> seq)
				{
					foreach (var o in seq)
					{
						var s = o?.ToString();
						if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
					}
					return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				}
				if (obj is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
				{
					foreach (var el in je.EnumerateArray())
					{
						if (el.ValueKind == System.Text.Json.JsonValueKind.String)
						{
							var s = el.GetString();
							if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
						}
					}
					return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				}
				return result;
			}

			if (TryGetBool(nameof(ShowCompletedTasks), out var showCompleted)) ShowCompletedTasks = showCompleted;
			if (TryGetBool(nameof(AutoDeleteCompleted), out var autoDelete)) AutoDeleteCompleted = autoDelete;
			AutoDeleteAfterDays = TryGetInt(nameof(AutoDeleteAfterDays), AutoDeleteAfterDays);
			if (TryGetBool(nameof(ShowLinkErrorToasts), out var showToasts)) ShowLinkErrorToasts = showToasts;
			IgnoredTaskIds = TryGetStringList(nameof(IgnoredTaskIds));
		}

		public void ResetToDefaults()
		{
			ShowCompletedTasks = true;
			AutoDeleteCompleted = false;
			AutoDeleteAfterDays = 30;
			ShowLinkErrorToasts = true;
			IgnoredTaskIds = new System.Collections.Generic.List<string>();
		}

		public bool Validate(out string errorMessage)
		{
			errorMessage = null;
			return true;
		}
	}
}


