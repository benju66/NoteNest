using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Application.Queries;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Services;
using NoteNest.UI.Plugins.TodoPlugin;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Parsing;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Queries;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Sync;
using NoteNest.UI.Plugins.TodoPlugin.Application.Queries;
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
            
            // Get projections database path
            var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var databasePath = Path.Combine(localAppDataPath, "NoteNest", "databases");
            var projectionsDbPath = Path.Combine(databasePath, "projections.db");
            var projectionsConnectionString = $"Data Source={projectionsDbPath};Cache=Shared;";
            
            // Register query service (projection-based)
            services.AddSingleton<ITodoQueryService>(provider => 
                new ProjectionBasedTodoQueryService(
                    projectionsConnectionString,
                    provider.GetRequiredService<IAppLogger>()));
                
            // TodoRepository now reads from projections (CQRS pattern - consistent with notes/categories)
            services.AddSingleton<ITodoRepository>(provider => 
                new NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Queries.TodoQueryRepository(
                    provider.GetRequiredService<NoteNest.UI.Plugins.TodoPlugin.Application.Queries.ITodoQueryService>(),
                    provider.GetRequiredService<IAppLogger>()));
                
            services.AddSingleton<ITodoBackupService>(provider => 
                new TodoBackupService(connectionString, provider.GetRequiredService<IAppLogger>()));
            
            // ✨ TAG MVP: Tag Management Services (Legacy - path-based auto-tagging)
            services.AddSingleton<IGlobalTagRepository>(provider => 
                new GlobalTagRepository(connectionString, provider.GetRequiredService<IAppLogger>()));
                
            services.AddSingleton<ITagGeneratorService, TagGeneratorService>();
            
            // ✨ HYBRID FOLDER TAGGING: User-controlled folder tag system
            // Note: Now uses projection-based implementation for event sourcing
            // Register ProjectionBasedTagInheritanceService for both interfaces it implements
            services.AddSingleton<NoteNest.UI.Plugins.TodoPlugin.Services.ProjectionBasedTagInheritanceService>(provider => 
                new NoteNest.UI.Plugins.TodoPlugin.Services.ProjectionBasedTagInheritanceService(
                    projectionsConnectionString,
                    provider.GetRequiredService<ITagQueryService>(),
                    provider.GetRequiredService<IAppLogger>()));
            
            services.AddSingleton<ITagInheritanceService>(provider => 
                provider.GetRequiredService<NoteNest.UI.Plugins.TodoPlugin.Services.ProjectionBasedTagInheritanceService>());
            
            services.AddSingleton<NoteNest.Application.Tags.Services.ITagPropagationService>(provider => 
                provider.GetRequiredService<NoteNest.UI.Plugins.TodoPlugin.Services.ProjectionBasedTagInheritanceService>());
            
            services.AddSingleton<IFolderTagSuggestionService, NoteNest.UI.Plugins.TodoPlugin.Services.FolderTagSuggestionService>();
            
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
            
            // Register stores (now with projection-based queries)
            services.AddSingleton<ITodoStore>(provider => 
                new TodoStore(
                    provider.GetRequiredService<ITodoQueryService>(),
                    provider.GetRequiredService<NoteNest.Core.Services.IEventBus>(),
                    provider.GetRequiredService<NoteNest.Application.Common.Interfaces.IProjectionOrchestrator>(),
                    provider.GetRequiredService<IAppLogger>()));
            services.AddSingleton<ICategoryStore, CategoryStore>();
            
            // Register UI services
            services.AddTransient<TodoListViewModel>();
            services.AddTransient<CategoryTreeViewModel>(provider => 
                new CategoryTreeViewModel(
                    provider.GetRequiredService<ICategoryStore>(),
                    provider.GetRequiredService<ITodoStore>(),
                    null, // ITodoTagRepository no longer used in event-sourced version
                    provider.GetRequiredService<IMediator>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<IAppLogger>()));
            services.AddTransient<TodoPanelViewModel>(); // Composite ViewModel
            services.AddTransient<TodoPanelView>();
            
            return services;
        }
    }
}
