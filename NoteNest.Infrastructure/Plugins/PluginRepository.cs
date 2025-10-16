using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using NoteNest.Application.Plugins.Interfaces;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Domain.Plugins;

namespace NoteNest.Infrastructure.Plugins
{
    /// <summary>
    /// In-memory plugin repository for managing loaded plugin state.
    /// Plugins are transient and recreated on application restart.
    /// </summary>
    public class PluginRepository : IPluginRepository
    {
        private readonly ConcurrentDictionary<string, Plugin> _plugins = new();
        private readonly IAppLogger _logger;

        public PluginRepository(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<Plugin>> GetByIdAsync(PluginId pluginId)
        {
            await Task.CompletedTask; // Make async for consistency
            
            if (_plugins.TryGetValue(pluginId.Value, out var plugin))
            {
                return Result.Ok(plugin);
            }
            
            return Result.Fail<Plugin>($"Plugin not found: {pluginId.Value}");
        }

        public async Task<Result<IReadOnlyList<Plugin>>> GetAllAsync()
        {
            await Task.CompletedTask; // Make async for consistency
            return Result.Ok<IReadOnlyList<Plugin>>(_plugins.Values.ToList());
        }

        public async Task<Result<IReadOnlyList<Plugin>>> GetByStatusAsync(PluginStatus status)
        {
            await Task.CompletedTask; // Make async for consistency
            
            var plugins = _plugins.Values.Where(p => p.Status == status).ToList();
            return Result.Ok<IReadOnlyList<Plugin>>(plugins);
        }

        public async Task<Result> AddAsync(Plugin plugin)
        {
            await Task.CompletedTask; // Make async for consistency
            
            if (plugin == null)
                return Result.Fail("Plugin cannot be null");

            if (_plugins.ContainsKey(plugin.PluginId.Value))
            {
                return Result.Fail($"Plugin already exists: {plugin.PluginId.Value}");
            }

            _plugins[plugin.PluginId.Value] = plugin;
            _logger.Info($"Added plugin to repository: {plugin.PluginId.Value}");
            
            return Result.Ok();
        }

        public async Task<Result> UpdateAsync(Plugin plugin)
        {
            await Task.CompletedTask; // Make async for consistency
            
            if (plugin == null)
                return Result.Fail("Plugin cannot be null");

            if (!_plugins.ContainsKey(plugin.PluginId.Value))
            {
                return Result.Fail($"Plugin not found: {plugin.PluginId.Value}");
            }

            _plugins[plugin.PluginId.Value] = plugin;
            _logger.Debug($"Updated plugin in repository: {plugin.PluginId.Value}");
            
            return Result.Ok();
        }

        public async Task<Result> DeleteAsync(PluginId pluginId)
        {
            await Task.CompletedTask; // Make async for consistency
            
            if (_plugins.TryRemove(pluginId.Value, out var plugin))
            {
                _logger.Info($"Removed plugin from repository: {pluginId.Value}");
                return Result.Ok();
            }
            
            return Result.Fail($"Plugin not found: {pluginId.Value}");
        }
    }
}

