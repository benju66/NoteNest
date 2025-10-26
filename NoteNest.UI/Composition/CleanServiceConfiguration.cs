using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Data.Sqlite;
using MediatR;
using FluentValidation;
using NoteNest.Application.Common.Behaviors;
using NoteNest.Application.Notes.Commands.CreateNote;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Infrastructure.EventBus;
using NoteNest.Infrastructure.Database;
using NoteNest.Infrastructure.Database.Adapters;
using NoteNest.Infrastructure.Database.Services;
using NoteNest.Infrastructure.Services;
using NoteNest.UI.ViewModels.Shell;
using NoteNest.UI.ViewModels.Categories;
using NoteNest.UI.ViewModels.Notes;
using NoteNest.UI.ViewModels.Workspace;
using NoteNest.UI.ViewModels;
using NoteNest.UI.Services;
using NoteNest.UI.Interfaces;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Configuration;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Implementation;
using NoteNest.Core.Interfaces.Search;
using NoteNest.Core.Services.Search;

namespace NoteNest.UI.Composition
{
    /// <summary>
    /// 🎯 CLEAN ARCHITECTURE SERVICE CONFIGURATION
    /// Single Responsibility Principle (SRP) - Each extension method has one job
    /// Scorched Earth Rebuild - Only proven working components
    /// </summary>
    public static class CleanServiceConfiguration
    {
        /// <summary>
        /// Main entry point for clean architecture DI configuration
        /// </summary>
        public static IServiceCollection ConfigureCleanArchitecture(
            this IServiceCollection services, IConfiguration configuration)
        {
            // 1. FOUNDATION (SRP: Core Infrastructure)
            services.AddFoundationServices();
            
            // 2. DATABASE ONLY (SRP: Data Layer)
            services.AddDatabaseServices(configuration);
            
            // 2.5. EVENT SOURCING (SRP: CQRS Read/Write Separation)
            services.AddEventSourcingServices(configuration);
            
            // 3. CORE SYSTEMS (SRP: Business Logic)
            services.AddRTFEditorSystem(configuration);
            services.AddSaveSystem(configuration);
            services.AddWorkspaceServices();
            services.AddSearchSystem();
            
            // 4. UI LAYER (SRP: Presentation)
            services.AddCleanViewModels();
            
            // 5. CLEAN ARCHITECTURE (SRP: CQRS)
            services.AddCleanArchitecture();
            
            // 6. PLUGIN SYSTEM (SRP: Extensibility)
            services.AddPluginSystem();
            
            return services;
        }
        
        /// <summary>
        /// SRP: Foundation services - logging, caching, configuration
        /// </summary>
        private static IServiceCollection AddFoundationServices(this IServiceCollection services)
        {
            // 🚨 CRITICAL FIX: Add Microsoft.Extensions.Logging for LoggingBehavior compatibility
            services.AddLogging();
            
            // Custom app logging
            // Use same AppLogger instance as legacy components for unified logging
            services.AddSingleton<IAppLogger>(NoteNest.Core.Services.Logging.AppLogger.Instance);
            
            // Memory caching for performance
            services.AddMemoryCache();
            
            // File system provider
            services.AddSingleton<IFileSystemProvider, NoteNest.Core.Services.DefaultFileSystemProvider>();
            
            // Event bus for application events (CQRS domain events)
            services.AddSingleton<NoteNest.Application.Common.Interfaces.IEventBus>(provider =>
                new InMemoryEventBus(
                    provider.GetRequiredService<IMediator>(),
                    provider.GetRequiredService<IAppLogger>()));
            
            // Plugin event bus for cross-cutting concerns and subscriptions
            services.AddSingleton<NoteNest.Core.Services.IEventBus, NoteNest.Core.Services.EventBus>();
            
            // Legacy configuration service (for compatibility)
            services.AddSingleton<ConfigurationService>(provider =>
                new ConfigurationService(
                    provider.GetService<IFileSystemProvider>(),
                    provider.GetService<NoteNest.Core.Services.IEventBus>()));
            
            return services;
        }
        
        /// <summary>
        /// SRP: Database-only services - no file system repositories
        /// </summary>
        private static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Database path configuration
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var databasePath = Path.Combine(localAppData, "NoteNest");
            Directory.CreateDirectory(databasePath);
            var treeDbPath = Path.Combine(databasePath, "tree.db");
            
            // Optimized connection string
            var treeConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = treeDbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true,
                DefaultTimeout = 30
            }.ToString();
            
            // Use actual notes path for this user
            var notesRootPath = configuration.GetValue<string>("NotesPath") 
                ?? @"C:\Users\Burness\MyNotes\Notes";
            
            // Core database services
            services.AddSingleton<ITreeDatabaseInitializer>(provider => 
                new TreeDatabaseInitializer(treeConnectionString, provider.GetRequiredService<IAppLogger>()));
            
            services.AddSingleton<ITreeDatabaseRepository>(provider => 
                new TreeDatabaseRepository(treeConnectionString, provider.GetRequiredService<IAppLogger>(), notesRootPath));
            
            // Legacy tag services removed - replaced by TagQueryService
            // Legacy repositories removed - replaced by EventStore + Query Services
            
            services.AddSingleton<ITreePopulationService, TreePopulationService>();
            
            // ICategoryRepository and INoteRepository removed - using event sourcing now
            // Queries use ITreeQueryService instead
            
            // Tree repository for category operations (descendants, bulk updates - reads from projections)
            services.AddScoped<ITreeRepository>(provider =>
                new NoteNest.Infrastructure.Queries.TreeQueryRepositoryAdapter(
                    provider.GetRequiredService<NoteNest.Application.Queries.ITreeQueryService>(),
                    provider.GetRequiredService<IAppLogger>()));
            
            // File service for category and note file operations (CRITICAL for CQRS handlers)
            services.AddScoped<IFileService>(provider =>
                new NoteNest.Infrastructure.Services.FileService(provider.GetRequiredService<IAppLogger>()));
            
            // Folder tag repository for TodoPlugin tag inheritance (uses tree.db)
            services.AddSingleton<NoteNest.Application.FolderTags.Repositories.IFolderTagRepository>(provider =>
                new NoteNest.Infrastructure.Repositories.FolderTagRepository(
                    treeConnectionString,
                    provider.GetRequiredService<IAppLogger>()));
            
            // Auto-start services for database initialization
            services.AddHostedService<TreeNodeInitializationService>();
            
            // File system watcher for backup sync (catches external file changes)
            services.AddSingleton<NoteNest.Infrastructure.Database.Services.DatabaseFileWatcherService>(provider =>
                new NoteNest.Infrastructure.Database.Services.DatabaseFileWatcherService(
                    provider.GetRequiredService<ITreeDatabaseRepository>(),
                    provider.GetRequiredService<IMemoryCache>(),
                    provider.GetRequiredService<IAppLogger>(),
                    notesRootPath));
            
            services.AddHostedService(provider => 
                provider.GetRequiredService<NoteNest.Infrastructure.Database.Services.DatabaseFileWatcherService>());
            
            return services;
        }
        
        /// <summary>
        /// SRP: RTF Editor & Tab System
        /// </summary>
        private static IServiceCollection AddRTFEditorSystem(this IServiceCollection services, IConfiguration configuration)
        {
            // Use actual notes path for this user
            var notesRootPath = configuration.GetValue<string>("NotesPath") 
                ?? @"C:\Users\Burness\MyNotes\Notes";
            
            // RTF Save Manager (proven working system)
            // Uses its own BasicStatusNotifier to avoid DI order issues
            services.AddSingleton<ISaveManager>(provider =>
            {
                var statusNotifier = new NoteNest.Core.Services.BasicStatusNotifier(provider.GetRequiredService<IAppLogger>());
                return new NoteNest.Core.Services.RTFIntegratedSaveEngine(notesRootPath, statusNotifier);
            });
            
            // 🧪 PROTOTYPE: Database metadata sync service (MUST be registered AFTER ISaveManager)
            services.AddHostedService<NoteNest.Infrastructure.Database.Services.DatabaseMetadataUpdateService>();
            
            // 🔍 Search index sync service - keeps search index updated when notes are saved
            services.AddHostedService<NoteNest.UI.Services.SearchIndexSyncService>();
            
            // Workspace Persistence Service (Milestone 2A - Tab Persistence)
            services.AddSingleton<IWorkspacePersistenceService, WorkspacePersistenceService>();
            
            // NEW: Tear-Out Services (Multi-Window Tab Management)
            services.AddSingleton<NoteNest.UI.Services.IWindowManager, NoteNest.UI.Services.WindowManager>();
            services.AddSingleton<NoteNest.UI.Services.IMultiWindowThemeCoordinator, NoteNest.UI.Services.MultiWindowThemeCoordinator>();
            services.AddSingleton<NoteNest.Core.Services.IMultiMonitorManager, NoteNest.Core.Services.MultiMonitorManager>();
            
            // NEW: Clean Workspace ViewModel (Enhanced with Tear-Out Support)
            // Replaces ModernWorkspaceViewModel with better MVVM separation + multi-window support
            services.AddTransient<NoteNest.UI.ViewModels.Workspace.WorkspaceViewModel>(provider =>
                new NoteNest.UI.ViewModels.Workspace.WorkspaceViewModel(
                    provider.GetRequiredService<ISaveManager>(),
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<IWorkspacePersistenceService>(),
                    provider.GetRequiredService<NoteNest.UI.Services.IWindowManager>(),
                    provider.GetRequiredService<NoteNest.UI.Services.IMultiWindowThemeCoordinator>(),
                    provider.GetRequiredService<NoteNest.Core.Services.IMultiMonitorManager>()));
            
            return services;
        }
        
        /// <summary>
        /// SRP: Search System (Already database-backed)
        /// </summary>
        private static IServiceCollection AddSearchSystem(this IServiceCollection services)
        {
            // Storage & Search Options
            services.AddSingleton<NoteNest.Core.Configuration.IStorageOptions>(provider =>
            {
                var notesPath = NoteNest.Core.Services.FirstTimeSetupService.ConfiguredNotesPath;
                if (string.IsNullOrWhiteSpace(notesPath))
                {
                    var config = provider.GetRequiredService<IConfiguration>();
                    notesPath = config.GetValue<string>("NotesPath") 
                        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteNest");
                }

                var storageOptions = NoteNest.Core.Configuration.StorageOptions.FromNotesPath(notesPath);
                storageOptions.ValidatePaths();
                return storageOptions;
            });

            services.AddSingleton<NoteNest.Core.Configuration.ISearchOptions>(provider =>
            {
                var storageOptions = provider.GetRequiredService<NoteNest.Core.Configuration.IStorageOptions>();
                var searchOptions = NoteNest.Core.Configuration.SearchConfigurationOptions.FromStoragePath(storageOptions.MetadataPath);
                searchOptions.ValidateConfiguration();
                return searchOptions;
            });
            
            // FTS5 Search Services (proven working)
            services.AddSingleton<IFts5Repository>(provider =>
                new Fts5Repository(provider.GetService<IAppLogger>()));

            services.AddSingleton<ISearchResultMapper, SearchResultMapper>();

            services.AddSingleton<ISearchIndexManager>(provider =>
                new Fts5IndexManager(
                    provider.GetRequiredService<IFts5Repository>(),
                    provider.GetRequiredService<ISearchResultMapper>(),
                    provider.GetRequiredService<NoteNest.Core.Configuration.IStorageOptions>(),
                    provider.GetService<IAppLogger>()));

            services.AddSingleton<NoteNest.UI.Interfaces.ISearchService>(provider =>
                new FTS5SearchService(
                    provider.GetRequiredService<IFts5Repository>(),
                    provider.GetRequiredService<ISearchResultMapper>(),
                    provider.GetRequiredService<ISearchIndexManager>(),
                    provider.GetRequiredService<NoteNest.Core.Configuration.ISearchOptions>(),
                    provider.GetRequiredService<NoteNest.Core.Configuration.IStorageOptions>(),
                    provider.GetService<IAppLogger>()));
            
            return services;
        }
        
        /// <summary>
        /// SRP: Save System & Supporting Services
        /// </summary>
        private static IServiceCollection AddSaveSystem(this IServiceCollection services, IConfiguration configuration)
        {
            // Note Service (core note management)
            services.AddSingleton<NoteService>(provider =>
                new NoteService(
                    provider.GetRequiredService<IFileSystemProvider>(),
                    provider.GetRequiredService<ConfigurationService>(),
                    provider.GetRequiredService<IAppLogger>(),
                    null, // IEventBus is optional
                    provider.GetService<NoteNest.Core.Services.Safety.SafeFileService>(),
                    provider.GetService<NoteNest.Core.Services.Notes.INoteStorageService>(),
                    provider.GetService<IUserNotificationService>(),
                    provider.GetRequiredService<NoteNest.Core.Services.NoteMetadataManager>()));
            
            // Supporting services
            services.AddSingleton<NoteNest.Core.Services.NoteMetadataManager>(provider =>
                new NoteNest.Core.Services.NoteMetadataManager(
                    provider.GetRequiredService<IFileSystemProvider>(),
                    provider.GetRequiredService<IAppLogger>()));
            
            services.AddSingleton<IServiceErrorHandler, NoteNest.Core.Services.Implementation.ServiceErrorHandler>();
            services.AddSingleton<NoteNest.Core.Services.Safety.SafeFileService>();
            
            services.AddSingleton<NoteNest.Core.Services.Notes.INoteStorageService>(provider =>
                new NoteNest.Core.Services.Notes.NoteStorageService(
                    provider.GetRequiredService<IFileSystemProvider>(),
                    provider.GetService<NoteNest.Core.Services.Safety.SafeFileService>(),
                    provider.GetService<IAppLogger>()));
            
            return services;
        }
        
        /// <summary>
        /// SRP: Clean ViewModels with direct dependencies only
        /// </summary>
        private static IServiceCollection AddCleanViewModels(this IServiceCollection services)
        {
            // Tree view (event-sourced) - queries projections via ITreeQueryService
            services.AddTransient<CategoryTreeViewModel>(provider =>
                new CategoryTreeViewModel(
                    provider.GetRequiredService<NoteNest.Application.Queries.ITreeQueryService>(),
                    provider.GetRequiredService<NoteNest.Application.Common.Interfaces.INoteRepository>(),
                    provider.GetRequiredService<IAppLogger>()));
            
            // Search view (database-backed)
            services.AddTransient<SearchViewModel>(provider =>
                new SearchViewModel(
                    provider.GetRequiredService<NoteNest.UI.Interfaces.ISearchService>(),
                    provider.GetRequiredService<NoteService>(),
                    provider.GetRequiredService<IAppLogger>()));
            
            // Operations ViewModels
            services.AddTransient<NoteOperationsViewModel>();
            services.AddTransient<CategoryOperationsViewModel>();
            
            // Main Shell ViewModel (ties everything together)
            services.AddTransient<MainShellViewModel>();
            
            // Status notifier for background services and UI feedback
            // IMPORTANT: Registered AFTER MainShellViewModel so it can access StatusMessage property
            services.AddSingleton<NoteNest.Core.Interfaces.IStatusNotifier>(provider =>
            {
                var mainShell = provider.GetRequiredService<MainShellViewModel>();
                return new NoteNest.UI.Services.WPFStatusNotifier(msg => mainShell.StatusMessage = msg);
            });
            
            // UI Services
            services.AddScoped<IDialogService, DialogService>();
            
            services.AddSingleton<IUserNotificationService>(provider =>
            {
                var logger = provider.GetService<IAppLogger>();
                return new UserNotificationService(System.Windows.Application.Current?.MainWindow, logger);
            });
            
            // Theme Service - NEW for full Solarized theming
            services.AddSingleton<IThemeService, ThemeService>();
            
            return services;
        }
        
        /// <summary>
        /// SRP: CQRS/MediatR Clean Architecture
        /// </summary>
        private static IServiceCollection AddCleanArchitecture(this IServiceCollection services)
        {
            // MediatR for CQRS
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(typeof(CreateNoteCommand).Assembly);
                cfg.RegisterServicesFromAssembly(typeof(NoteNest.UI.Plugins.TodoPlugin.TodoPlugin).Assembly);
                cfg.RegisterServicesFromAssembly(typeof(DomainEventBridge).Assembly);  // Infrastructure - for DomainEventBridge
            });
            
            // Pipeline behaviors (now compatible with Microsoft.Extensions.Logging)
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(NoteNest.Infrastructure.Behaviors.ProjectionSyncBehavior<,>));
            
            // FluentValidation
            services.AddValidatorsFromAssembly(typeof(CreateNoteCommand).Assembly);
            services.AddValidatorsFromAssembly(typeof(NoteNest.UI.Plugins.TodoPlugin.TodoPlugin).Assembly);
            
            // Domain event bridge for plugin system
            services.AddTransient<INotificationHandler<DomainEventNotification>, DomainEventBridge>();
            
            return services;
        }
        
        /// <summary>
        /// SRP: Workspace Service for SearchViewModel compatibility
        /// </summary>
        private static IServiceCollection AddWorkspaceServices(this IServiceCollection services)
        {
            // Content Cache
            services.AddSingleton<ContentCache>();
            
            // Note Operations Service
            services.AddSingleton<INoteOperationsService>(provider =>
                new NoteNest.Core.Services.Implementation.NoteOperationsService(
                    provider.GetRequiredService<NoteService>(),
                    provider.GetRequiredService<IServiceErrorHandler>(),
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<IFileSystemProvider>(),
                    provider.GetRequiredService<ConfigurationService>(),
                    provider.GetRequiredService<ContentCache>(),
                    provider.GetRequiredService<ISaveManager>()));
            
            // OLD: IWorkspaceService removed - functionality now in WorkspaceViewModel
            
            // Add plugin system services
            services.AddPluginSystem();
            
            return services;
        }
        
        /// <summary>
        /// SRP: Event Sourcing Services - Event Store, Projections, Query Services
        /// </summary>
        private static IServiceCollection AddEventSourcingServices(this IServiceCollection services, IConfiguration configuration)
        {
            var logger = services.BuildServiceProvider().GetRequiredService<IAppLogger>();
            
            // Notes root path for file operations
            var notesRootPath = configuration.GetValue<string>("NotesPath") 
                ?? @"C:\Users\Burness\MyNotes\Notes";
            
            // Database paths
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var databasePath = Path.Combine(localAppData, "NoteNest");
            Directory.CreateDirectory(databasePath);
            
            var eventsDbPath = Path.Combine(databasePath, "events.db");
            var projectionsDbPath = Path.Combine(databasePath, "projections.db");
            
            var eventsConnectionString = $"Data Source={eventsDbPath};Cache=Shared;";
            var projectionsConnectionString = $"Data Source={projectionsDbPath};Cache=Shared;";
            
            // Event Store Core
            services.AddSingleton<NoteNest.Infrastructure.EventStore.IEventSerializer>(provider =>
                new NoteNest.Infrastructure.EventStore.JsonEventSerializer(
                    provider.GetRequiredService<IAppLogger>()));
            
            services.AddSingleton<NoteNest.Infrastructure.EventStore.EventStoreInitializer>(provider =>
                new NoteNest.Infrastructure.EventStore.EventStoreInitializer(
                    eventsConnectionString,
                    provider.GetRequiredService<IAppLogger>()));
            
            services.AddSingleton<NoteNest.Infrastructure.Projections.ProjectionsInitializer>(provider =>
                new NoteNest.Infrastructure.Projections.ProjectionsInitializer(
                    projectionsConnectionString,
                    provider.GetRequiredService<IAppLogger>()));
            
            services.AddSingleton<IEventStore>(provider =>
                new NoteNest.Infrastructure.EventStore.SqliteEventStore(
                    eventsConnectionString,
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<NoteNest.Infrastructure.EventStore.IEventSerializer>()));
            
            // Query Repository for Notes (reads from projections)
            services.AddSingleton<NoteNest.Application.Common.Interfaces.INoteRepository>(provider =>
                new NoteNest.Infrastructure.Queries.NoteQueryRepository(
                    provider.GetRequiredService<NoteNest.Application.Queries.ITreeQueryService>(),
                    notesRootPath,
                    provider.GetRequiredService<IAppLogger>()));
            
            // Repository for Categories (reads from projections for data source consistency)
            services.AddSingleton<NoteNest.Application.Common.Interfaces.ICategoryRepository>(provider =>
                new NoteNest.Infrastructure.Queries.CategoryQueryRepository(
                    provider.GetRequiredService<NoteNest.Application.Queries.ITreeQueryService>(),
                    provider.GetRequiredService<IAppLogger>()));
            
            // Projections
            services.AddSingleton<NoteNest.Application.Projections.IProjection>(provider =>
                new NoteNest.Infrastructure.Projections.TreeViewProjection(
                    projectionsConnectionString,
                    provider.GetRequiredService<IAppLogger>()));
            
            services.AddSingleton<NoteNest.Application.Projections.IProjection>(provider =>
                new NoteNest.Infrastructure.Projections.TagProjection(
                    projectionsConnectionString,
                    provider.GetRequiredService<IAppLogger>()));
            
            // TodoProjection - reads todo events and builds todo_view in projections.db
            services.AddSingleton<NoteNest.Application.Projections.IProjection>(provider =>
                new NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Projections.TodoProjection(
                    projectionsConnectionString,
                    provider.GetRequiredService<IAppLogger>()));
            
            services.AddSingleton<NoteNest.Infrastructure.Projections.ProjectionOrchestrator>(provider =>
                new NoteNest.Infrastructure.Projections.ProjectionOrchestrator(
                    provider.GetRequiredService<IEventStore>(),
                    provider.GetServices<NoteNest.Application.Projections.IProjection>(),
                    provider.GetRequiredService<NoteNest.Infrastructure.EventStore.IEventSerializer>(),
                    provider.GetRequiredService<IAppLogger>()));
                    
            // Register interface that maps to concrete implementation (for Clean Architecture)
            services.AddSingleton<NoteNest.Application.Common.Interfaces.IProjectionOrchestrator>(provider =>
                provider.GetRequiredService<NoteNest.Infrastructure.Projections.ProjectionOrchestrator>());
            
            // Background service for continuous projection updates (safety net)
            services.AddHostedService<NoteNest.Infrastructure.Projections.ProjectionHostedService>();
            
            // Background service for tag propagation to child items
            services.AddHostedService(provider =>
                new NoteNest.Infrastructure.Services.TagPropagationService(
                    provider.GetRequiredService<NoteNest.Core.Services.IEventBus>(),
                    provider.GetRequiredService<IEventStore>(),
                    provider.GetRequiredService<NoteNest.Application.Queries.ITagQueryService>(),
                    provider.GetRequiredService<NoteNest.Application.Common.Interfaces.IProjectionOrchestrator>(),
                    provider.GetRequiredService<NoteNest.Application.Tags.Services.ITagPropagationService>(),
                    provider.GetRequiredService<IStatusNotifier>(),
                    projectionsConnectionString,
                    provider.GetRequiredService<IAppLogger>()));
            
            // Query Services
            services.AddSingleton<NoteNest.Application.Queries.ITreeQueryService>(provider =>
                new NoteNest.Infrastructure.Queries.TreeQueryService(
                    projectionsConnectionString,
                    provider.GetRequiredService<IMemoryCache>(),
                    provider.GetRequiredService<IAppLogger>()));
            
            services.AddSingleton<NoteNest.Application.Queries.ITagQueryService>(provider =>
                new NoteNest.Infrastructure.Queries.TagQueryService(
                    projectionsConnectionString,
                    provider.GetRequiredService<IAppLogger>()));
            
            // TodoQueryService - reads from todo_view in projections.db
            services.AddSingleton<NoteNest.UI.Plugins.TodoPlugin.Application.Queries.ITodoQueryService>(provider =>
                new NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Queries.TodoQueryService(
                    projectionsConnectionString,
                    provider.GetRequiredService<IAppLogger>()));
            
            // Initialize databases on startup
            // Note: Initialization happens during first service resolution
            // EventStoreInitializer and ProjectionsInitializer registered as singletons above
            
            return services;
        }
    }
}
