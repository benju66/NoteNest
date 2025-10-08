using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Domain.Common;
using NoteNest.Domain.Plugins;

namespace NoteNest.Application.Plugins.Contracts
{
    /// <summary>
    /// Core plugin interface that all plugins must implement.
    /// Defines the lifecycle and capabilities of a plugin.
    /// </summary>
    public interface IPlugin : IDisposable
    {
        /// <summary>
        /// Unique plugin identifier (e.g., "todo-plugin", "calendar-plugin")
        /// </summary>
        PluginId Id { get; }
        
        /// <summary>
        /// Plugin metadata (name, version, description, etc.)
        /// </summary>
        PluginMetadata Metadata { get; }
        
        /// <summary>
        /// Capabilities requested by this plugin
        /// </summary>
        IReadOnlyList<string> RequestedCapabilities { get; }

        /// <summary>
        /// Initialize the plugin with the provided context.
        /// Called when plugin is loaded.
        /// </summary>
        Task<Result> InitializeAsync(IPluginContext context);

        /// <summary>
        /// Shutdown the plugin and release resources.
        /// Called when plugin is unloaded.
        /// </summary>
        Task<Result> ShutdownAsync();

        /// <summary>
        /// Get the plugin's UI panel descriptor if it provides UI.
        /// Returns null if plugin has no UI.
        /// </summary>
        Task<IPluginPanelDescriptor> GetPanelDescriptorAsync();

        /// <summary>
        /// Get the plugin's current health status.
        /// Used for monitoring and diagnostics.
        /// </summary>
        Task<PluginHealthStatus> GetHealthAsync();
    }

    /// <summary>
    /// Plugin context providing access to host services and capabilities.
    /// Services are resolved at runtime from the host's DI container.
    /// </summary>
    public interface IPluginContext
    {
        /// <summary>
        /// Plugin's unique identifier
        /// </summary>
        PluginId PluginId { get; }

        /// <summary>
        /// Check if plugin has a specific capability
        /// </summary>
        bool HasCapability(string capability);

        /// <summary>
        /// Get a service from the host application.
        /// Validates capability requirements before returning.
        /// </summary>
        Task<Result<T>> GetServiceAsync<T>() where T : class;

        /// <summary>
        /// Log message with plugin context
        /// </summary>
        void Log(string level, string message);
    }

    /// <summary>
    /// Plugin panel descriptor for UI integration.
    /// </summary>
    public interface IPluginPanelDescriptor
    {
        string Title { get; }
        string Icon { get; }
        Type ViewModelType { get; }
        Security.UISlotType PreferredSlot { get; }
        double PreferredWidth { get; }
        double MinWidth { get; }
        double MaxWidth { get; }
    }

    /// <summary>
    /// Plugin health status for monitoring.
    /// </summary>
    public class PluginHealthStatus
    {
        public bool IsHealthy { get; set; }
        public string StatusMessage { get; set; }
        public DateTime LastChecked { get; set; }
        public long MemoryUsageBytes { get; set; }
        public int ActiveSubscriptions { get; set; }
    }
}

