using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Application.Plugins.Contracts;
using NoteNest.Application.Plugins.Interfaces;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Domain.Plugins;

namespace NoteNest.Infrastructure.Plugins
{
    /// <summary>
    /// Implementation of plugin context providing secure access to host services.
    /// </summary>
    public class PluginContext : IPluginContext
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly HashSet<string> _grantedCapabilities;

        public PluginId PluginId { get; }
        public IAppLogger Logger { get; }
        public IEventBus EventBus { get; }
        public IPluginDataStore DataStore { get; }

        public PluginContext(
            PluginId pluginId,
            IServiceProvider serviceProvider,
            IReadOnlyList<string> grantedCapabilities)
        {
            PluginId = pluginId ?? throw new ArgumentNullException(nameof(pluginId));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _grantedCapabilities = new HashSet<string>(grantedCapabilities ?? Array.Empty<string>());

            // Provide core services directly
            Logger = new PluginLogger(pluginId, _serviceProvider.GetRequiredService<IAppLogger>());
            EventBus = _serviceProvider.GetRequiredService<IEventBus>();
            DataStore = _serviceProvider.GetRequiredService<IPluginDataStore>();
        }

        public bool HasCapability(string capability)
        {
            return _grantedCapabilities.Contains(capability);
        }

        public void Log(string level, string message)
        {
            switch (level?.ToLower())
            {
                case "debug":
                    Logger.Debug(message);
                    break;
                case "info":
                    Logger.Info(message);
                    break;
                case "warning":
                    Logger.Warning(message);
                    break;
                case "error":
                    Logger.Error(message);
                    break;
                default:
                    Logger.Info(message);
                    break;
            }
        }

        public async Task<Result<T>> GetServiceAsync<T>() where T : class
        {
            await Task.CompletedTask; // Make async for consistency

            try
            {
                // Validate capability requirements
                if (!ValidateServiceAccess(typeof(T)))
                {
                    return Result.Fail<T>($"Plugin lacks capability to access service: {typeof(T).Name}");
                }

                var service = _serviceProvider.GetService<T>();
                
                if (service == null)
                {
                    return Result.Fail<T>($"Service not available: {typeof(T).Name}");
                }

                Logger.Debug($"Plugin accessed service: {typeof(T).Name}");
                return Result.Ok(service);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to get service: {typeof(T).Name}");
                return Result.Fail<T>($"Failed to get service: {ex.Message}");
            }
        }

        private bool ValidateServiceAccess(Type serviceType)
        {
            // Define service access rules based on capabilities
            // For now, allow common safe services
            var safeServiceNames = new[]
            {
                "IDialogService",
                "IUserNotificationService",
                "IAppLogger",
                "ConfigurationService"
            };

            if (safeServiceNames.Contains(serviceType.Name))
            {
                return true; // Safe services always allowed
            }

            // High-risk services require specific capabilities
            // This will be expanded as needed
            return false;
        }
    }

    /// <summary>
    /// Plugin-scoped logger that prefixes all messages with plugin ID.
    /// </summary>
    public class PluginLogger : IAppLogger
    {
        private readonly PluginId _pluginId;
        private readonly IAppLogger _hostLogger;

        public PluginLogger(PluginId pluginId, IAppLogger hostLogger)
        {
            _pluginId = pluginId;
            _hostLogger = hostLogger;
        }

        public void Debug(string message, params object[] args) => _hostLogger.Debug($"[Plugin:{_pluginId}] {message}", args);
        public void Info(string message, params object[] args) => _hostLogger.Info($"[Plugin:{_pluginId}] {message}", args);
        public void Warning(string message, params object[] args) => _hostLogger.Warning($"[Plugin:{_pluginId}] {message}", args);
        public void Error(Exception exception, string message, params object[] args) => _hostLogger.Error(exception, $"[Plugin:{_pluginId}] {message}", args);
        public void Error(string message, params object[] args) => _hostLogger.Error($"[Plugin:{_pluginId}] {message}", args);
        public void Fatal(string message, params object[] args) => _hostLogger.Fatal($"[Plugin:{_pluginId}] {message}", args);
        public void Fatal(Exception exception, string message, params object[] args) => _hostLogger.Fatal(exception, $"[Plugin:{_pluginId}] {message}", args);
    }
}

