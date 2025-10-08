using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Application.Plugins.Contracts;
using NoteNest.Application.Plugins.Security;
using NoteNest.Core.Services.Logging;
using NoteNest.Infrastructure.Plugins;
using NoteNest.Plugins.TodoPlugin.UI.Views;

namespace NoteNest.Plugins.TodoPlugin
{
    /// <summary>
    /// Main plugin implementation for the Todo plugin.
    /// </summary>
    public class TodoPlugin : IPlugin
    {
        private IPluginContext? _context;
        private IServiceProvider? _serviceProvider;
        private IAppLogger? _logger;
        private TodoPanelView? _panelView;

        public string Id => "NoteNest.TodoPlugin";
        public string Name => "Todo Manager";
        public string Version => "1.0.0";
        public string Description => "Advanced task management with bidirectional note integration";
        public PluginState State { get; private set; } = PluginState.Unloaded;

        public async Task<bool> InitializeAsync(IPluginContext context)
        {
            try
            {
                _context = context ?? throw new ArgumentNullException(nameof(context));
                _logger = _context.GetService<IAppLogger>();
                
                _logger?.Info($"Initializing {Name} v{Version}");
                
                // Request required capabilities
                var requiredCapabilities = new[]
                {
                    PluginCapability.DataPersistence,
                    PluginCapability.UIIntegration,
                    PluginCapability.EventSubscription,
                    PluginCapability.NoteAccess
                };

                foreach (var capability in requiredCapabilities)
                {
                    if (!_context.HasCapability(capability.Name))
                    {
                        _logger?.Error($"Missing required capability: {capability.Name}");
                        return false;
                    }
                }

                // Configure services
                ConfigureServices();
                
                State = PluginState.Loaded;
                _logger?.Info($"{Name} initialized successfully");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to initialize TodoPlugin");
                State = PluginState.Error;
                return false;
            }
        }

        public async Task StartAsync()
        {
            try
            {
                if (State != PluginState.Loaded)
                {
                    _logger?.Warning($"Cannot start {Name} - current state: {State}");
                    return;
                }

                _logger?.Info($"Starting {Name}");
                
                // Register with UI if capability is available
                if (_context?.HasCapability(PluginCapability.UIIntegration.Name) == true)
                {
                    RegisterUI();
                }
                
                // Subscribe to events if capability is available
                if (_context?.HasCapability(PluginCapability.EventSubscription.Name) == true)
                {
                    SubscribeToEvents();
                }
                
                State = PluginState.Running;
                _logger?.Info($"{Name} started successfully");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to start {Name}");
                State = PluginState.Error;
            }
        }

        public async Task StopAsync()
        {
            try
            {
                if (State != PluginState.Running)
                {
                    _logger?.Warning($"Cannot stop {Name} - current state: {State}");
                    return;
                }

                _logger?.Info($"Stopping {Name}");
                
                // Unsubscribe from events
                UnsubscribeFromEvents();
                
                // Clean up UI
                _panelView = null;
                
                State = PluginState.Loaded;
                _logger?.Info($"{Name} stopped successfully");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to stop {Name}");
                State = PluginState.Error;
            }
        }

        public async Task<bool> ShutdownAsync()
        {
            try
            {
                _logger?.Info($"Shutting down {Name}");
                
                if (State == PluginState.Running)
                {
                    await StopAsync();
                }
                
                // Clean up resources
                _context = null;
                _serviceProvider = null;
                _logger = null;
                
                State = PluginState.Unloaded;
                return true;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to shutdown {Name}");
                return false;
            }
        }

        public IPluginPanelDescriptor? GetPanelDescriptor()
        {
            if (_context?.HasCapability(PluginCapability.UIIntegration.Name) != true)
                return null;

            return new TodoPanelDescriptor();
        }

        public object? GetConfigurationUI()
        {
            // TODO: Implement configuration UI if needed
            return null;
        }

        public async Task<Dictionary<string, object>> GetStatusAsync()
        {
            var status = new Dictionary<string, object>
            {
                ["State"] = State.ToString(),
                ["Version"] = Version,
                ["HasUI"] = _panelView != null
            };
            
            // Add todo statistics if available
            if (_serviceProvider != null && State == PluginState.Running)
            {
                try
                {
                    // TODO: Add todo count statistics
                    status["TodoCount"] = 0;
                    status["CompletedToday"] = 0;
                }
                catch { }
            }
            
            return status;
        }

        #region Private Methods

        private void ConfigureServices()
        {
            if (_context == null) return;
            
            // Services are configured in the main application's DI container
            // through PluginSystemConfiguration.cs
            
            // Get the service provider from context
            _serviceProvider = _context.ServiceProvider;
        }

        private void RegisterUI()
        {
            try
            {
                if (_serviceProvider == null) return;
                
                // Create the main panel view
                _panelView = _serviceProvider.GetService<TodoPanelView>();
                
                _logger?.Debug("Todo UI registered successfully");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to register Todo UI");
            }
        }

        private void SubscribeToEvents()
        {
            // TODO: Subscribe to note events for bidirectional sync
            _logger?.Debug("Subscribed to events");
        }

        private void UnsubscribeFromEvents()
        {
            // TODO: Unsubscribe from events
            _logger?.Debug("Unsubscribed from events");
        }

        #endregion
    }

    /// <summary>
    /// Panel descriptor for the Todo plugin.
    /// </summary>
    internal class TodoPanelDescriptor : IPluginPanelDescriptor
    {
        public string PanelId => "TodoPanel";
        public string Title => "Todo";
        public string IconResource => "LucideCheckSquare"; // Lucid icon for todo
        public PanelPosition PreferredPosition => PanelPosition.Right;
        public double PreferredWidth => 350;
        public double MinWidth => 250;
        public double MaxWidth => 600;
        
        public FrameworkElement CreatePanelContent(IServiceProvider serviceProvider)
        {
            return serviceProvider.GetRequiredService<TodoPanelView>();
        }
    }
}
