using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
	public interface ITabPersistenceService : IDisposable
	{
		Task<TabPersistenceState?> LoadAsync();
		Task SaveAsync(IEnumerable<ITabItem> tabs, string? activeTabId, string? embeddedActiveContent);
		void MarkChanged();
	}

	public sealed class TabPersistenceService : ITabPersistenceService
	{
		private readonly ConfigurationService _config;
		private readonly IAppLogger _logger;
		private readonly IWorkspaceService _workspace;
		// compute lazily from configuration to avoid capturing empty paths before settings load
		private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };
		private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
		private Timer? _safetyTimer;
		private volatile bool _changed;
		private readonly int _debounceMs = 800;
		private Timer? _debounceTimer;

		public TabPersistenceService(ConfigurationService config, IAppLogger logger, IWorkspaceService workspace)
		{
			_config = config ?? throw new ArgumentNullException(nameof(config));
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
			_safetyTimer = new Timer(async _ => await SafetySaveAsync(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
		}

		private string GetStateFilePath()
		{
			try
			{
				var meta = _config?.Settings?.MetadataPath;
				if (!string.IsNullOrWhiteSpace(meta))
				{
					return Path.Combine(meta, "tabs.json");
				}
			}
			catch { }
			var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			var noteNest = Path.Combine(appData, "NoteNest");
			return Path.Combine(noteNest, "tabs.json");
		}

		public void MarkChanged()
		{
			_changed = true;
			_debounceTimer?.Dispose();
			_debounceTimer = new Timer(async _ => await DebouncedSaveAsync(), null, _debounceMs, Timeout.Infinite);
		}

		public async Task<TabPersistenceState?> LoadAsync()
		{
			try
			{
				var path = GetStateFilePath();
				var dir = Path.GetDirectoryName(path) ?? string.Empty;
				if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
				if (!File.Exists(path)) return null;
				var json = await File.ReadAllTextAsync(path);
				return JsonSerializer.Deserialize<TabPersistenceState>(json, _jsonOptions);
			}
			catch (Exception ex)
			{
				_logger.Warning($"Tab state load failed: {ex.Message}");
				return null;
			}
		}

		public async Task SaveAsync(IEnumerable<ITabItem> tabs, string? activeTabId, string? embeddedActiveContent)
		{
			await _saveLock.WaitAsync();
			try
			{
				var path = GetStateFilePath();
				var dir = Path.GetDirectoryName(path) ?? string.Empty;
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

				var tabList = tabs?.ToList() ?? new List<ITabItem>();
				var state = new TabPersistenceState
				{
					Tabs = tabList.Select(t => new TabInfo
					{
						Id = t.Note?.Id ?? string.Empty,
						Path = t.Note?.FilePath ?? string.Empty,
						Title = t.Note?.Title ?? string.Empty,
						IsDirty = t.IsDirty
					}).ToList(),
					ActiveTabId = activeTabId,
					LastSaved = DateTime.UtcNow
				};

				if (!string.IsNullOrEmpty(activeTabId))
				{
					var activeTab = tabList.FirstOrDefault(t => t.Note?.Id == activeTabId);
					if (activeTab != null)
					{
						// Cap embedded content to 256 KB
						var content = embeddedActiveContent ?? activeTab.Content ?? string.Empty;
						if (content?.Length > 256 * 1024)
							content = null;
						state.ActiveTabContent = content;
					}
				}

				var json = JsonSerializer.Serialize(state, _jsonOptions);
				await File.WriteAllTextAsync(path, json);
				_changed = false;
			}
			finally
			{
				_saveLock.Release();
			}
		}

		private async Task DebouncedSaveAsync()
		{
			try
			{
				_debounceTimer?.Dispose();
				// Snapshot tabs via injected workspace
				if (_workspace?.OpenTabs?.Count > 0)
				{
					var activeId = _workspace.SelectedTab?.Note?.Id;
					var embedded = _workspace.SelectedTab?.Content;
					await SaveAsync(_workspace.OpenTabs, activeId, embedded);
				}
			}
			catch (Exception ex)
			{
				_logger.Warning($"Debounced tab state save failed: {ex.Message}");
			}
		}

		private async Task SafetySaveAsync()
		{
			if (!_changed) return;
			try
			{
				if (_workspace?.OpenTabs?.Count > 0)
				{
					var activeId = _workspace.SelectedTab?.Note?.Id;
					var embedded = _workspace.SelectedTab?.Content;
					await SaveAsync(_workspace.OpenTabs, activeId, embedded);
				}
			}
			catch (Exception ex)
			{
				_logger.Warning($"Safety tab state save failed: {ex.Message}");
			}
		}

		public void Dispose()
		{
			try
			{
				_debounceTimer?.Dispose();
				_safetyTimer?.Dispose();
			}
			catch { }
		}
	}
}


