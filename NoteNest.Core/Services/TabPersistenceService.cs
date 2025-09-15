using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
		/// <summary>
		/// Force immediate save without debouncing - used for RTF tab close operations to prevent race conditions
		/// </summary>
		Task ForceSaveAsync(IEnumerable<ITabItem> tabs, string? activeTabId, string? embeddedActiveContent);
	}

	public sealed class TabPersistenceService : ITabPersistenceService
	{
		private readonly ConfigurationService _config;
		private readonly IAppLogger _logger;
		private readonly ISaveManager _saveManager;
		// compute lazily from configuration to avoid capturing empty paths before settings load
		private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };
		private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
		private Timer? _safetyTimer;
		private volatile bool _changed;
		private readonly int _debounceMs = 800;
		private Timer? _debounceTimer;

	public TabPersistenceService(ConfigurationService config, IAppLogger logger, ISaveManager saveManager)
	{
		_config = config ?? throw new ArgumentNullException(nameof(config));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
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
				Tabs = new List<TabInfo>(),
				ActiveTabId = activeTabId,
				LastSaved = DateTime.UtcNow
			};
			
			foreach (var tab in tabList)
			{
				var info = new TabInfo
				{
					Id = tab.Note?.Id ?? string.Empty,
					Path = tab.Note?.FilePath ?? string.Empty,
					Title = tab.Note?.Title ?? string.Empty,
					IsDirty = tab.IsDirty
				};
				
			// Only store dirty content and hash
			if (tab.IsDirty && _saveManager != null && !string.IsNullOrEmpty(tab.NoteId))
			{
				info.DirtyContent = _saveManager.GetContent(tab.NoteId);
					
					// Hash the current file content (not saved content)
					if (File.Exists(info.Path))
					{
						try
						{
							var fileContent = await File.ReadAllTextAsync(info.Path);
							using (var sha256 = SHA256.Create())
							{
								var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(fileContent));
								info.FileContentHash = Convert.ToBase64String(bytes);
							}
						}
						catch (Exception ex)
						{
							_logger?.Warning($"Failed to hash file content: {info.Path} - {ex.Message}");
						}
					}
				}
				
				state.Tabs.Add(info);
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

	/// <summary>
	/// Force immediate save bypassing debouncing - critical for RTF tab close operations
	/// </summary>
	public async Task ForceSaveAsync(IEnumerable<ITabItem> tabs, string? activeTabId, string? embeddedActiveContent)
	{
		await _saveLock.WaitAsync();
		try
		{
			// Cancel any pending debounced save
			_debounceTimer?.Dispose();
			_changed = false; // Reset since we're saving immediately
			
			_logger.Debug("Force saving tab persistence state for RTF tab close operation");
			
			// Reuse the same logic as SaveAsync but without debouncing
			var path = GetStateFilePath();
			var dir = Path.GetDirectoryName(path) ?? string.Empty;
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

			var tabList = tabs?.ToList() ?? new List<ITabItem>();
			
			var state = new TabPersistenceState
			{
				Tabs = new List<TabInfo>(),
				ActiveTabId = activeTabId,
				LastSaved = DateTime.UtcNow
			};
			
			foreach (var tab in tabList)
			{
				var info = new TabInfo
				{
					Id = tab.Note?.Id ?? string.Empty,
					Path = tab.Note?.FilePath ?? string.Empty,
					Title = tab.Note?.Title ?? string.Empty,
					IsDirty = tab.IsDirty
				};
				
				// Only store dirty content and hash for RTF content
				if (tab.IsDirty && _saveManager != null && !string.IsNullOrEmpty(tab.NoteId))
				{
					info.DirtyContent = _saveManager.GetContent(tab.NoteId);
						
					// Hash the current file content (not saved content)
					if (File.Exists(info.Path))
					{
						try
						{
							var fileContent = await File.ReadAllTextAsync(info.Path);
							using (var sha256 = SHA256.Create())
							{
								var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(fileContent));
								info.FileContentHash = Convert.ToBase64String(bytes);
							}
						}
						catch (Exception ex)
						{
							_logger?.Warning($"Failed to hash RTF file content: {info.Path} - {ex.Message}");
						}
					}
				}
				
				state.Tabs.Add(info);
			}

			var json = JsonSerializer.Serialize(state, _jsonOptions);
			await File.WriteAllTextAsync(path, json);
			
			_logger.Info($"Force saved tab persistence state with {state.Tabs.Count} RTF tabs");
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Force save of tab persistence state failed");
			throw;
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
				// Note: Debounced saves are now handled by the calling code
				// This method is kept for interface compatibility
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
				// Note: Safety saves are now handled by the calling code
				// This method is kept for interface compatibility
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


