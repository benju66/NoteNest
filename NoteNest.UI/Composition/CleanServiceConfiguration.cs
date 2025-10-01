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
using NoteNest.Infrastructure.Database.Services;
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
            
            // 3. CORE SYSTEMS (SRP: Business Logic)
            services.AddRTFEditorSystem(configuration);
            services.AddSaveSystem(configuration);
            services.AddWorkspaceServices();
            services.AddSearchSystem();
            
            // 4. UI LAYER (SRP: Presentation)
            services.AddCleanViewModels();
            
            // 5. CLEAN ARCHITECTURE (SRP: CQRS)
            services.AddCleanArchitecture();
            
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
            
            // Event bus for application events
            services.AddSingleton<NoteNest.Application.Common.Interfaces.IEventBus, InMemoryEventBus>();
            
            // Legacy configuration service (for compatibility)
            services.AddSingleton<ConfigurationService>();
            
            // 🎨 LUCIDE ICON SERVICE - Modern SVG icon management
            services.AddSingleton<NoteNest.UI.Interfaces.IIconService, NoteNest.UI.Services.LucideIconService>();
            
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
            
            var notesRootPath = configuration.GetValue<string>("NotesPath") 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteNest");
            
            // Core database services
            services.AddSingleton<ITreeDatabaseInitializer>(provider => 
                new TreeDatabaseInitializer(treeConnectionString, provider.GetRequiredService<IAppLogger>()));
            
            services.AddSingleton<ITreeDatabaseRepository>(provider => 
                new TreeDatabaseRepository(treeConnectionString, provider.GetRequiredService<IAppLogger>(), notesRootPath));
            
            services.AddSingleton<ITreePopulationService, TreePopulationService>();
            
            // 🎯 DATABASE-ONLY REPOSITORIES (No duplicate registrations)
            services.AddScoped<ICategoryRepository>(provider => 
                new CategoryTreeDatabaseService(
                    provider.GetRequiredService<ITreeDatabaseRepository>(),
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<IMemoryCache>(),
                    notesRootPath));
            
            services.AddScoped<INoteRepository>(provider => 
                new NoteTreeDatabaseService(
                    provider.GetRequiredService<ITreeDatabaseRepository>(),
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<IMemoryCache>()));
            
            // Auto-start services for database initialization
            services.AddHostedService<TreeNodeInitializationService>();
            
            return services;
        }
        
        /// <summary>
        /// SRP: RTF Editor & Tab System
        /// </summary>
        private static IServiceCollection AddRTFEditorSystem(this IServiceCollection services, IConfiguration configuration)
        {
            var notesRootPath = configuration.GetValue<string>("NotesPath") 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteNest");
            
            // RTF Save Manager (proven working system)
            services.AddSingleton<ISaveManager>(provider =>
            {
                var statusNotifier = new NoteNest.Core.Services.BasicStatusNotifier(provider.GetRequiredService<IAppLogger>());
                return new NoteNest.Core.Services.RTFIntegratedSaveEngine(notesRootPath, statusNotifier);
            });
            
            // Tab Factory for workspace
            services.AddSingleton<ITabFactory>(provider =>
                new NoteNest.UI.Services.UITabFactory(provider.GetRequiredService<ISaveManager>()));
            
            // Modern Workspace ViewModel (proven RTF integration)
            services.AddTransient<ModernWorkspaceViewModel>();
            
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
            // Tree view (database-backed) 
            services.AddTransient<CategoryTreeViewModel>(provider =>
                new CategoryTreeViewModel(
                    provider.GetRequiredService<ICategoryRepository>(),
                    provider.GetRequiredService<INoteRepository>(),
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<NoteNest.UI.Interfaces.IIconService>()));
            
            // Search view (database-backed)
            services.AddTransient<SearchViewModel>(provider =>
                new SearchViewModel(
                    provider.GetRequiredService<NoteNest.UI.Interfaces.ISearchService>(),
                    provider.GetRequiredService<IWorkspaceService>(),
                    provider.GetRequiredService<NoteService>(),
                    provider.GetRequiredService<IAppLogger>()));
            
            // Operations ViewModels
            services.AddTransient<NoteOperationsViewModel>();
            services.AddTransient<CategoryOperationsViewModel>();
            
            // Main Shell ViewModel (ties everything together)
            services.AddTransient<MainShellViewModel>();
            
            // UI Services
            services.AddScoped<IDialogService, DialogService>();
            
            services.AddSingleton<IUserNotificationService>(provider =>
            {
                var logger = provider.GetService<IAppLogger>();
                return new UserNotificationService(System.Windows.Application.Current?.MainWindow, logger);
            });
            
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
            });
            
            // Pipeline behaviors (now compatible with Microsoft.Extensions.Logging)
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            
            // FluentValidation
            services.AddValidatorsFromAssembly(typeof(CreateNoteCommand).Assembly);
            
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
            
            // Workspace Service
            services.AddSingleton<IWorkspaceService>(provider =>
                new NoteNest.Core.Services.Implementation.WorkspaceService(
                    provider.GetRequiredService<ContentCache>(),
                    provider.GetRequiredService<NoteService>(),
                    provider.GetRequiredService<IServiceErrorHandler>(),
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<INoteOperationsService>(),
                    provider.GetRequiredService<ISaveManager>(),
                    provider.GetRequiredService<ITabFactory>()));
            
            return services;
        }
    }
}
