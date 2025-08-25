using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Plugins
{
	public interface IPluginManager
	{
		IReadOnlyList<IPlugin> LoadedPlugins { get; }
		IPlugin ActivePlugin { get; }
		event Action PluginsChanged;
		Task<bool> LoadPluginAsync(Type pluginType);
		Task<bool> LoadPluginAsync(IPlugin plugin);
		Task UnloadPluginAsync(string pluginId);
		IPlugin GetPlugin(string pluginId);
		Task ActivatePluginAsync(string pluginId);
		Task DeactivatePluginAsync(string pluginId);
		Task InitializeAllAsync();
		Task ShutdownAllAsync();
	}

	public class PluginManager : IPluginManager
	{
		private readonly Dictionary<string, IPlugin> _plugins = new();
		private readonly IPluginDataStore _dataStore;
		private readonly IAppLogger _logger;
		private IPlugin _activePlugin;

		public IReadOnlyList<IPlugin> LoadedPlugins => _plugins.Values.ToList();
		public IPlugin ActivePlugin => _activePlugin;
		public event Action PluginsChanged;

		public PluginManager(IPluginDataStore dataStore, IAppLogger logger = null)
		{
			_dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
			_logger = logger ?? AppLogger.Instance;
		}

		public async Task<bool> LoadPluginAsync(Type pluginType)
		{
			try
			{
				if (!typeof(IPlugin).IsAssignableFrom(pluginType))
				{
					_logger?.Warning($"Type {pluginType.Name} does not implement IPlugin");
					return false;
				}

				var plugin = Activator.CreateInstance(pluginType) as IPlugin;
				return await LoadPluginAsync(plugin);
			}
			catch (Exception ex)
			{
				_logger?.Error(ex, $"Failed to load plugin type: {pluginType.Name}");
				return false;
			}
		}

		public async Task<bool> LoadPluginAsync(IPlugin plugin)
		{
			if (plugin == null)
				return false;

			if (_plugins.ContainsKey(plugin.Id))
			{
				_logger?.Warning($"Plugin {plugin.Id} is already loaded");
				return false;
			}

			try
			{
				var settings = await _dataStore.LoadSettingsAsync(plugin.Id);
				plugin.GetSettings()?.FromDictionary(settings ?? new Dictionary<string, object>());

				_plugins[plugin.Id] = plugin;

				if (plugin.IsEnabled)
				{
					await plugin.InitializeAsync();
				}

				_logger?.Info($"Plugin loaded: {plugin.Name} v{plugin.Version}");
				PluginsChanged?.Invoke();
				return true;
			}
			catch (Exception ex)
			{
				_logger?.Error(ex, $"Failed to load plugin: {plugin.Name}");
				_plugins.Remove(plugin.Id);
				return false;
			}
		}

		public async Task UnloadPluginAsync(string pluginId)
		{
			if (!_plugins.TryGetValue(pluginId, out var plugin))
				return;

			try
			{
				var settings = plugin.GetSettings()?.ToDictionary();
				if (settings != null)
				{
					await _dataStore.SaveSettingsAsync(pluginId, settings);
				}

				await plugin.ShutdownAsync();
				_plugins.Remove(pluginId);

				if (_activePlugin?.Id == pluginId)
				{
					_activePlugin = null;
				}

				plugin.Dispose();
				_logger?.Info($"Plugin unloaded: {plugin.Name}");
				PluginsChanged?.Invoke();
			}
			catch (Exception ex)
			{
				_logger?.Error(ex, $"Error unloading plugin: {pluginId}");
			}
		}

		public IPlugin GetPlugin(string pluginId)
		{
			return _plugins.TryGetValue(pluginId, out var plugin) ? plugin : null;
		}

		public async Task ActivatePluginAsync(string pluginId)
		{
			var plugin = GetPlugin(pluginId);
			if (plugin == null)
				return;

			if (_activePlugin != null && _activePlugin.Id != pluginId)
			{
				await DeactivatePluginAsync(_activePlugin.Id);
			}

			_activePlugin = plugin;
			var panel = plugin.GetPanel();
			panel.IsVisible = true;
			panel.OnPanelOpened();
			_logger?.Debug($"Plugin activated: {plugin.Name}");
		}

		public async Task DeactivatePluginAsync(string pluginId)
		{
			var plugin = GetPlugin(pluginId);
			if (plugin == null)
				return;

			var panel = plugin.GetPanel();
			panel.IsVisible = false;
			panel.OnPanelClosed();

			if (_activePlugin?.Id == pluginId)
			{
				_activePlugin = null;
			}

			await Task.CompletedTask;
			_logger?.Debug($"Plugin deactivated: {plugin.Name}");
		}

		public async Task InitializeAllAsync()
		{
			var tasks = _plugins.Values
				.Where(p => p.IsEnabled)
				.Select(p => p.InitializeAsync());
			await Task.WhenAll(tasks);
		}

		public async Task ShutdownAllAsync()
		{
			foreach (var plugin in _plugins.Values)
			{
				var settings = plugin.GetSettings()?.ToDictionary();
				if (settings != null)
				{
					await _dataStore.SaveSettingsAsync(plugin.Id, settings);
				}
			}

			var tasks = _plugins.Values.Select(p => p.ShutdownAsync());
			await Task.WhenAll(tasks);
			_plugins.Clear();
			_activePlugin = null;
		}
	}
}


