using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Parsing;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Sync;
using NoteNest.UI.Plugins.TodoPlugin.Services;
using NoteNest.UI.Plugins.TodoPlugin.UI.ViewModels;
using NoteNest.UI.Plugins.TodoPlugin.UI.Views;
using NoteNest.UI.ViewModels.Shell;

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
            
            // ✨ TAG MVP: Tag Management Services
            services.AddSingleton<ITodoTagRepository>(provider => 
                new TodoTagRepository(connectionString, provider.GetRequiredService<IAppLogger>()));
                
            services.AddSingleton<IGlobalTagRepository>(provider => 
                new GlobalTagRepository(connectionString, provider.GetRequiredService<IAppLogger>()));
                
            services.AddSingleton<ITagGeneratorService, TagGeneratorService>();
            
            // =================================================================
            // TODO PLUGIN RTF INTEGRATION
            // =================================================================
            
            // Bracket todo parser
            services.AddSingleton<BracketTodoParser>();
            
            // Background sync service (IHostedService)
            services.AddHostedService<TodoSyncService>();
            
            // =================================================================
            // TODO PLUGIN SERVICES
            // =================================================================
            
            // Register plugin infrastructure
            services.AddSingleton<TodoPlugin>();
            
            // NEW: Category sync service (syncs with main app's tree database)
            services.AddSingleton<ICategorySyncService, CategorySyncService>();
            
            // NEW: Category cleanup service (orphaned category handling)
            services.AddSingleton<ICategoryCleanupService, CategoryCleanupService>();
            
            // NEW: Category persistence service (saves selected categories to database)
            services.AddSingleton<ICategoryPersistenceService>(provider =>
                new CategoryPersistenceService(connectionString, provider.GetRequiredService<IAppLogger>()));
            
            // Register stores (now with database backing)
            services.AddSingleton<ITodoStore, TodoStore>();
            services.AddSingleton<ICategoryStore, CategoryStore>();
            
            // Register UI services
            services.AddTransient<TodoListViewModel>();
            services.AddTransient<CategoryTreeViewModel>();
            services.AddTransient<TodoPanelViewModel>(); // Composite ViewModel
            services.AddTransient<TodoPanelView>();
            
            return services;
        }
    }
}
