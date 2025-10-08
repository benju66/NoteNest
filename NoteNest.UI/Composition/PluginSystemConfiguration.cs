using System;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.UI.Plugins.TodoPlugin;
using NoteNest.UI.Plugins.TodoPlugin.Services;
using NoteNest.UI.Plugins.TodoPlugin.UI.ViewModels;
using NoteNest.UI.Plugins.TodoPlugin.UI.Views;

namespace NoteNest.UI.Composition
{
    /// <summary>
    /// Configures plugin system services in the DI container.
    /// </summary>
    public static class PluginSystemConfiguration
    {
        public static IServiceCollection AddPluginSystem(this IServiceCollection services)
        {
            // Register plugin infrastructure
            services.AddSingleton<TodoPlugin>();
            
            // Register stores
            services.AddSingleton<ITodoStore, TodoStore>();
            services.AddSingleton<ICategoryStore, CategoryStore>();
            
            // Register UI services
            services.AddTransient<TodoListViewModel>();
            services.AddTransient<CategoryTreeViewModel>();
            services.AddTransient<TodoPanelView>();
            
            return services;
        }
    }
}