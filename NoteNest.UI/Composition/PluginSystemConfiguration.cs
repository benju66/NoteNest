using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;
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
            // =================================================================
            // TODO PLUGIN DATABASE INFRASTRUCTURE
            // =================================================================
            
            // Database path (plugin-isolated storage)
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var pluginDataDir = Path.Combine(localAppData, "NoteNest", ".plugins", "NoteNest.TodoPlugin");
            
            // Ensure plugin directory exists
            Directory.CreateDirectory(pluginDataDir);
            
            var todosDbPath = Path.Combine(pluginDataDir, "todos.db");
            
            // Connection string with performance optimizations
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = todosDbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true,
                DefaultTimeout = 30
            }.ToString();
            
            // Register database infrastructure
            services.AddSingleton<ITodoDatabaseInitializer>(provider => 
                new TodoDatabaseInitializer(connectionString, provider.GetRequiredService<IAppLogger>()));
                
            services.AddSingleton<ITodoRepository>(provider => 
                new TodoRepository(connectionString, provider.GetRequiredService<IAppLogger>()));
                
            services.AddSingleton<ITodoBackupService>(provider => 
                new TodoBackupService(connectionString, provider.GetRequiredService<IAppLogger>()));
            
            // =================================================================
            // TODO PLUGIN SERVICES
            // =================================================================
            
            // Register plugin infrastructure
            services.AddSingleton<TodoPlugin>();
            
            // Register stores (now with database backing)
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
