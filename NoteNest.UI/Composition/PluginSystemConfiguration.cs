using Microsoft.Extensions.DependencyInjection;
using NoteNest.Application.Plugins.Interfaces;
using NoteNest.Application.Plugins.Services;
using NoteNest.Infrastructure.Plugins;

namespace NoteNest.UI.Composition
{
    /// <summary>
    /// Plugin system service configuration.
    /// Registers all plugin infrastructure services with dependency injection.
    /// </summary>
    public static class PluginSystemConfiguration
    {
        public static IServiceCollection AddPluginSystem(this IServiceCollection services)
        {
            // Core Plugin Services
            services.AddSingleton<IPluginManager, PluginManager>();
            services.AddSingleton<IPluginRepository, PluginRepository>();
            services.AddSingleton<IPluginDataStore, PluginDataStore>();
            
            // Note: Individual plugin implementations will be registered separately
            // Example: services.AddTransient<TodoPlugin>();
            
            return services;
        }
    }
}

