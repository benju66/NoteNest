using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Application.Plugins.Contracts;
using NoteNest.Domain.Common;
using NoteNest.Domain.Plugins;

namespace NoteNest.Application.Plugins.Services
{
    /// <summary>
    /// Plugin manager interface for loading, unloading, and managing plugins.
    /// </summary>
    public interface IPluginManager
    {
        /// <summary>
        /// Get all currently loaded plugin instances
        /// </summary>
        IReadOnlyList<IPlugin> LoadedPlugins { get; }

        /// <summary>
        /// Load a plugin by ID
        /// </summary>
        Task<Result<IPlugin>> LoadPluginAsync(PluginId pluginId);

        /// <summary>
        /// Unload a plugin by ID
        /// </summary>
        Task<Result> UnloadPluginAsync(PluginId pluginId);

        /// <summary>
        /// Get a specific loaded plugin
        /// </summary>
        IPlugin GetPlugin(PluginId pluginId);

        /// <summary>
        /// Check if a plugin is loaded
        /// </summary>
        bool IsPluginLoaded(PluginId pluginId);

        /// <summary>
        /// Load all plugins marked as auto-start
        /// </summary>
        Task<Result> LoadAutoStartPluginsAsync();

        /// <summary>
        /// Unload all plugins (called on application shutdown)
        /// </summary>
        Task<Result> UnloadAllPluginsAsync();
    }
}

