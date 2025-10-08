using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Application.Plugins.Contracts;
using NoteNest.Application.Plugins.Interfaces;
using NoteNest.Application.Plugins.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Domain.Plugins;

namespace NoteNest.Infrastructure.Plugins
{
    /// <summary>
    /// Plugin manager implementation managing plugin lifecycle and state.
    /// </summary>
    public class PluginManager : IPluginManager
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IPluginRepository _pluginRepository;
        private readonly IAppLogger _logger;
        private readonly ConcurrentDictionary<string, IPlugin> _loadedPlugins = new();

        public IReadOnlyList<IPlugin> LoadedPlugins => _loadedPlugins.Values.ToList();

        public PluginManager(
            IServiceProvider serviceProvider,
            IPluginRepository pluginRepository,
            IAppLogger logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _pluginRepository = pluginRepository ?? throw new ArgumentNullException(nameof(pluginRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<IPlugin>> LoadPluginAsync(PluginId pluginId)
        {
            if (_loadedPlugins.ContainsKey(pluginId.Value))
            {
                return Result.Fail<IPlugin>($"Plugin already loaded: {pluginId.Value}");
            }

            try
            {
                // Get plugin from repository
                var pluginResult = await _pluginRepository.GetByIdAsync(pluginId);
                if (pluginResult.IsFailure)
                {
                    return Result.Fail<IPlugin>(pluginResult.Error);
                }

                var pluginDomain = pluginResult.Value;

                // Create plugin instance (must be registered in DI)
                var pluginInstance = _serviceProvider.GetService(GetPluginType(pluginId)) as IPlugin;
                if (pluginInstance == null)
                {
                    return Result.Fail<IPlugin>($"Plugin implementation not found: {pluginId.Value}");
                }

                // Create plugin context with granted capabilities
                var context = new PluginContext(
                    pluginId,
                    _serviceProvider,
                    pluginDomain.GrantedCapabilities);

                // Initialize plugin
                var initResult = await pluginInstance.InitializeAsync(context);
                if (initResult.IsFailure)
                {
                    _logger.Error($"Failed to initialize plugin {pluginId.Value}: {initResult.Error}");
                    return Result.Fail<IPlugin>($"Plugin initialization failed: {initResult.Error}");
                }

                // Update domain model
                var loadResult = pluginDomain.Load(pluginDomain.GrantedCapabilities);
                if (loadResult.IsFailure)
                {
                    await pluginInstance.ShutdownAsync();
                    return Result.Fail<IPlugin>(loadResult.Error);
                }

                await _pluginRepository.UpdateAsync(pluginDomain);

                // Add to loaded plugins
                _loadedPlugins[pluginId.Value] = pluginInstance;

                _logger.Info($"Plugin loaded successfully: {pluginDomain.Metadata.Name} v{pluginDomain.Metadata.Version}");
                return Result.Ok(pluginInstance);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error loading plugin: {pluginId.Value}");
                return Result.Fail<IPlugin>($"Error loading plugin: {ex.Message}");
            }
        }

        public async Task<Result> UnloadPluginAsync(PluginId pluginId)
        {
            if (!_loadedPlugins.TryRemove(pluginId.Value, out var plugin))
            {
                return Result.Fail($"Plugin not loaded: {pluginId.Value}");
            }

            try
            {
                // Shutdown plugin
                var shutdownResult = await plugin.ShutdownAsync();
                if (shutdownResult.IsFailure)
                {
                    _logger.Warning($"Plugin shutdown reported failure: {shutdownResult.Error}");
                }

                // Dispose plugin
                plugin.Dispose();

                // Update repository
                var pluginResult = await _pluginRepository.GetByIdAsync(pluginId);
                if (pluginResult.IsFailure == false)
                {
                    var pluginDomain = pluginResult.Value;
                    pluginDomain.Unload();
                    await _pluginRepository.UpdateAsync(pluginDomain);
                }

                _logger.Info($"Plugin unloaded: {pluginId.Value}");
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error unloading plugin: {pluginId.Value}");
                return Result.Fail($"Error unloading plugin: {ex.Message}");
            }
        }

        public IPlugin GetPlugin(PluginId pluginId)
        {
            return _loadedPlugins.TryGetValue(pluginId.Value, out var plugin) ? plugin : null;
        }

        public bool IsPluginLoaded(PluginId pluginId)
        {
            return _loadedPlugins.ContainsKey(pluginId.Value);
        }

        public async Task<Result> LoadAutoStartPluginsAsync()
        {
            _logger.Info("Loading auto-start plugins...");
            
            // This will be implemented when plugin configuration is added
            // For now, no plugins are auto-started
            await Task.CompletedTask;
            
            return Result.Ok();
        }

        public async Task<Result> UnloadAllPluginsAsync()
        {
            _logger.Info($"Unloading all plugins ({_loadedPlugins.Count})...");
            
            var pluginIds = _loadedPlugins.Keys.ToList();
            
            foreach (var pluginId in pluginIds)
            {
                await UnloadPluginAsync(PluginId.From(pluginId));
            }

            _logger.Info("All plugins unloaded");
            return Result.Ok();
        }

        private Type GetPluginType(PluginId pluginId)
        {
            // Map plugin IDs to implementation types
            // This will be improved with plugin discovery
            var pluginTypeMap = new Dictionary<string, Type>();

            if (pluginTypeMap.TryGetValue(pluginId.Value, out var type))
            {
                return type;
            }

            return null;
        }
    }
}

