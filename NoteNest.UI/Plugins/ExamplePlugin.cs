using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Application.Plugins.Contracts;
using NoteNest.Domain.Common;
using NoteNest.Domain.Plugins;

namespace NoteNest.UI.Plugins
{
    /// <summary>
    /// Example plugin demonstrating the plugin system architecture.
    /// Shows proper capability usage, event subscription, and UI integration.
    /// </summary>
    public class ExamplePlugin : IPlugin
    {
        private IPluginContext _context;
        private bool _disposed;

        public PluginId Id => PluginId.From("example-plugin");

        public PluginMetadata Metadata => new PluginMetadata(
            name: "Example Plugin",
            version: new Version(1, 0, 0),
            description: "Example plugin demonstrating plugin system capabilities",
            author: "NoteNest Team",
            dependencies: Array.Empty<string>(),
            minimumHostVersion: new Version(1, 0),
            category: PluginCategory.Utilities);

        public IReadOnlyList<string> RequestedCapabilities => new[]
        {
            "EventSubscription",
            "DataPersistence",
            "UIIntegration"
        };

        public async Task<Result> InitializeAsync(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));

            try
            {
                _context.Log("Info", "Example plugin initializing...");

                // Subscribe to note events if capability granted
                if (_context.HasCapability("EventSubscription"))
                {
                    var eventBus = await _context.GetServiceAsync<NoteNest.Core.Services.IEventBus>();
                    if (eventBus.IsFailure == false)
                    {
                        // Subscribe to note saved events
                        eventBus.Value.Subscribe<NoteNest.Core.Events.NoteSavedEvent>(async e =>
                        {
                            _context.Log("Debug", $"Note saved: {e.FilePath}");
                            await Task.CompletedTask;
                        });
                    }
                }

                // Load plugin data if capability granted
                if (_context.HasCapability("DataPersistence"))
                {
                    var dataStore = await _context.GetServiceAsync<Application.Plugins.Interfaces.IPluginDataStore>();
                    if (dataStore.IsFailure == false)
                    {
                        var data = await dataStore.Value.LoadDataAsync<ExampleData>(Id, "settings");
                        _context.Log("Info", $"Loaded plugin data: {data.Value?.Message ?? "none"}");
                    }
                }

                _context.Log("Info", "Example plugin initialized successfully");
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _context?.Log("Error", $"Initialization failed: {ex.Message}");
                return Result.Fail($"Example plugin initialization failed: {ex.Message}");
            }
        }

        public async Task<Result> ShutdownAsync()
        {
            try
            {
                _context?.Log("Info", "Example plugin shutting down...");
                
                // Save any pending data
                if (_context?.HasCapability("DataPersistence") == true)
                {
                    var dataStore = await _context.GetServiceAsync<Application.Plugins.Interfaces.IPluginDataStore>();
                    if (dataStore.IsFailure == false)
                    {
                        var exampleData = new ExampleData { Message = "Plugin was unloaded gracefully" };
                        await dataStore.Value.SaveDataAsync(Id, "settings", exampleData);
                    }
                }

                _context?.Log("Info", "Example plugin shutdown complete");
                return Result.Ok();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Shutdown failed: {ex.Message}");
            }
        }

        public async Task<IPluginPanelDescriptor> GetPanelDescriptorAsync()
        {
            // This example plugin has no UI
            await Task.CompletedTask;
            return null;
        }

        public async Task<PluginHealthStatus> GetHealthAsync()
        {
            return new PluginHealthStatus
            {
                IsHealthy = true,
                StatusMessage = "Example plugin running normally",
                LastChecked = DateTime.UtcNow,
                MemoryUsageBytes = GC.GetTotalMemory(false),
                ActiveSubscriptions = 1 // Note saved event subscription
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _context?.Log("Debug", "Example plugin disposed");
            _disposed = true;
        }
    }

    public class ExampleData
    {
        public string Message { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}

