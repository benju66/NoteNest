using System;
using System.Threading.Tasks;
using System.Windows;
using NoteNest.Core.Plugins;
using NoteNest.Core.Services;
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

		public System.Collections.Generic.Dictionary<string, object> ToDictionary()
		{
			return new System.Collections.Generic.Dictionary<string, object>
			{
				[nameof(ShowCompletedTasks)] = ShowCompletedTasks,
				[nameof(AutoDeleteCompleted)] = AutoDeleteCompleted,
				[nameof(AutoDeleteAfterDays)] = AutoDeleteAfterDays
			};
		}

		public void FromDictionary(System.Collections.Generic.Dictionary<string, object> settings)
		{
			if (settings == null) return;
			if (settings.TryGetValue(nameof(ShowCompletedTasks), out var sc)) ShowCompletedTasks = Convert.ToBoolean(sc);
			if (settings.TryGetValue(nameof(AutoDeleteCompleted), out var ad)) AutoDeleteCompleted = Convert.ToBoolean(ad);
			if (settings.TryGetValue(nameof(AutoDeleteAfterDays), out var days)) AutoDeleteAfterDays = Convert.ToInt32(days);
		}

		public void ResetToDefaults()
		{
			ShowCompletedTasks = true;
			AutoDeleteCompleted = false;
			AutoDeleteAfterDays = 30;
		}

		public bool Validate(out string errorMessage)
		{
			errorMessage = null;
			return true;
		}
	}
}


