using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoteNest.Core.Services;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Models;
using NoteNest.Core.Interfaces.Search;
using NoteNest.Core.Services.Search;
using NoteNest.Core.Configuration;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Core service collection extensions - restores the original AddNoteNestServices method
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Add core NoteNest services (restored from original)
        /// </summary>
        public static IServiceCollection AddNoteNestServices(this IServiceCollection services)
        {
            // Core Infrastructure Services
            services.AddSingleton<IAppLogger>(AppLogger.Instance);
            services.AddSingleton<IStateManager, NoteNest.Core.Services.Implementation.StateManager>();
            services.AddSingleton<IServiceErrorHandler, NoteNest.Core.Services.Implementation.ServiceErrorHandler>();
            services.AddSingleton<IFileSystemProvider, DefaultFileSystemProvider>();
            services.AddSingleton<IEventBus, EventBus>();

            // Modern Configuration System (replaces ConfigurationService anti-pattern)
            services.AddSingleton<IStorageOptions>(serviceProvider =>
            {
                var notesPath = FirstTimeSetupService.ConfiguredNotesPath;
                if (string.IsNullOrEmpty(notesPath))
                {
                    notesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteNest");
                }
                
                var storageOptions = StorageOptions.FromNotesPath(notesPath);
                storageOptions.ValidatePaths(); // Ensure paths are accessible
                
                // CRITICAL: Keep legacy PathService in sync for existing components
                NoteNest.Core.Services.PathService.RootPath = storageOptions.NotesPath;
                
                return storageOptions;
            });

            services.AddSingleton<ISearchOptions>(serviceProvider =>
            {
                var storageOptions = serviceProvider.GetRequiredService<IStorageOptions>();
                var searchOptions = SearchConfigurationOptions.FromStoragePath(storageOptions.MetadataPath);
                searchOptions.ValidateConfiguration(); // Ensure configuration is valid
                return searchOptions;
            });

            // Legacy ConfigurationService (kept for backward compatibility during transition)
            services.AddSingleton<ConfigurationService>(serviceProvider =>
            {
                return new ConfigurationService(
                    serviceProvider.GetRequiredService<IFileSystemProvider>(),
                    serviceProvider.GetRequiredService<IEventBus>()
                );
            });
            
            // Save Management Services (using modern configuration)
            services.AddSingleton<IWriteAheadLog>(serviceProvider =>
            {
                var storageOptions = serviceProvider.GetRequiredService<IStorageOptions>();
                return new WriteAheadLog(storageOptions.WalPath);
            });
            services.AddSingleton<SaveConfiguration>();
            // OLD: services.AddSingleton<ISaveManager, UnifiedSaveManager>();
            // RTFIntegratedSaveEngine now implements ISaveManager - registered below
            
            // Note Operation Services - PREVIOUSLY MISSING
            services.AddSingleton<INoteOperationsService>(serviceProvider =>
            {
                return new NoteNest.Core.Services.Implementation.NoteOperationsService(
                    serviceProvider.GetRequiredService<NoteService>(),
                    serviceProvider.GetRequiredService<IServiceErrorHandler>(),
                    serviceProvider.GetRequiredService<IAppLogger>(),
                    serviceProvider.GetRequiredService<IFileSystemProvider>(),
                    serviceProvider.GetRequiredService<ConfigurationService>(),
                    serviceProvider.GetRequiredService<ContentCache>(),
                    serviceProvider.GetRequiredService<ISaveManager>()
                );
            });

            // Category Management Services - PREVIOUSLY MISSING  
            services.AddSingleton<ICategoryManagementService>(serviceProvider =>
            {
                return new NoteNest.Core.Services.Implementation.CategoryManagementService(
                    serviceProvider.GetRequiredService<NoteService>(),
                    serviceProvider.GetRequiredService<ConfigurationService>(),
                    serviceProvider.GetRequiredService<IServiceErrorHandler>(),
                    serviceProvider.GetRequiredService<IAppLogger>(),
                    serviceProvider.GetRequiredService<IFileSystemProvider>(),
                    serviceProvider.GetRequiredService<IEventBus>()
                );
            });

            // Tree Services - Enhanced with validation and caching
            // Register new tree services FIRST (before services that depend on them)
            services.AddSingleton<ITreeStructureValidationService, TreeStructureValidationService>();
            services.AddSingleton<ITreeCacheService, TreeCacheService>();
            
            // Note position management service
            services.AddSingleton<NoteNest.Core.Utils.NotePositionService>(serviceProvider =>
            {
                var metadataManager = serviceProvider.GetRequiredService<NoteMetadataManager>();
                var logger = serviceProvider.GetRequiredService<IAppLogger>();
                return new NoteNest.Core.Utils.NotePositionService(metadataManager, logger);
            });
            
            // Update TreeDataService with optional cache service
            services.AddSingleton<ITreeDataService>(serviceProvider =>
            {
                var cacheService = serviceProvider.GetService<ITreeCacheService>();
                return new TreeDataService(
                    serviceProvider.GetRequiredService<ICategoryManagementService>(),
                    serviceProvider.GetRequiredService<NoteService>(),
                    serviceProvider.GetRequiredService<IAppLogger>(),
                    cacheService  // Can be null
                );
            });
            
            // Update TreeOperationService with optional validation and cache services
            services.AddSingleton<ITreeOperationService>(serviceProvider =>
            {
                // Try to get optional services (can be null)
                var validationService = serviceProvider.GetService<ITreeStructureValidationService>();
                var cacheService = serviceProvider.GetService<ITreeCacheService>();
                
                return new TreeOperationService(
                    serviceProvider.GetRequiredService<ICategoryManagementService>(),
                    serviceProvider.GetRequiredService<INoteOperationsService>(),
                    serviceProvider.GetRequiredService<IAppLogger>(),
                    validationService,    // Can be null - safe
                    cacheService         // Can be null - safe
                );
            });
            services.AddSingleton<ITreeStateManager, TreeStateManager>();
            services.AddSingleton<ITreeController>(serviceProvider =>
            {
                return new TreeController(
                    serviceProvider.GetRequiredService<ITreeDataService>(),
                    serviceProvider.GetRequiredService<ITreeOperationService>(),
                    serviceProvider.GetRequiredService<ITreeStateManager>(),
                    serviceProvider.GetRequiredService<IAppLogger>()
                );
            });

            // UI Services
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IUserNotificationService>(serviceProvider =>
            {
                var logger = serviceProvider.GetService<IAppLogger>();
                // MainWindow may not be created yet; service tolerates null
                return new UserNotificationService(System.Windows.Application.Current?.MainWindow, logger);
            });
            services.AddSingleton<ToastNotificationService>();
            
            // CRITICAL FIX: Register ITabCloseService - was missing!
            services.AddSingleton<ITabCloseService>(serviceProvider =>
            {
                return new TabCloseService(
                    serviceProvider.GetRequiredService<INoteOperationsService>(),
                    serviceProvider.GetRequiredService<IWorkspaceService>(),
                    serviceProvider.GetRequiredService<IDialogService>(),
                    serviceProvider.GetRequiredService<IAppLogger>(),
                    serviceProvider.GetRequiredService<ISaveManager>()
                );
            });
            
            // Content and Search Services
            services.AddSingleton<ContentCache>(serviceProvider =>
            {
                var bus = serviceProvider.GetRequiredService<IEventBus>();
                return new ContentCache(bus);
            });
            
            // Register NoteMetadataManager as a proper service (was missing!)
            services.AddSingleton<NoteNest.Core.Services.NoteMetadataManager>(serviceProvider =>
            {
                return new NoteNest.Core.Services.NoteMetadataManager(
                    serviceProvider.GetRequiredService<IFileSystemProvider>(),
                    serviceProvider.GetRequiredService<IAppLogger>());
            });
            
            services.AddSingleton<NoteService>(serviceProvider => 
            {
                return new NoteService(
                    serviceProvider.GetRequiredService<IFileSystemProvider>(),
                    serviceProvider.GetRequiredService<ConfigurationService>(),
                    serviceProvider.GetRequiredService<IAppLogger>(),
                    serviceProvider.GetService<IEventBus>(),
                    serviceProvider.GetService<NoteNest.Core.Services.Safety.SafeFileService>(),
                    serviceProvider.GetService<NoteNest.Core.Services.Notes.INoteStorageService>(),
                    serviceProvider.GetService<IUserNotificationService>(),
                    serviceProvider.GetRequiredService<NoteNest.Core.Services.NoteMetadataManager>()
                );
            });

            // Additional Services - PREVIOUSLY MISSING
            services.AddSingleton<NoteNest.Core.Services.Safety.SafeFileService>();
            services.AddSingleton<NoteNest.Core.Services.Notes.INoteStorageService>(serviceProvider =>
            {
                return new NoteNest.Core.Services.Notes.NoteStorageService(
                    serviceProvider.GetRequiredService<IFileSystemProvider>(),
                    serviceProvider.GetService<NoteNest.Core.Services.Safety.SafeFileService>(),
                    serviceProvider.GetService<IAppLogger>()
                );
            });
            
            // Tab and Workspace Services - Register UITabFactory with proper constructor
            services.AddSingleton<ITabFactory>(serviceProvider =>
            {
                return new UITabFactory(
                    serviceProvider.GetRequiredService<ISaveManager>()
                );
            });
            services.AddSingleton<ITabPersistenceService, TabPersistenceService>();
            services.AddSingleton<IWorkspaceService, NoteNest.Core.Services.Implementation.WorkspaceService>();
            
            // FTS5 Search Services
            System.Diagnostics.Debug.WriteLine($"[DI] About to register FTS5 search services at {DateTime.Now:HH:mm:ss.fff}");
            services.AddFTS5SearchServices();
            System.Diagnostics.Debug.WriteLine($"[DI] FTS5 search services registered successfully");
            
            // Window and ViewModels
            services.AddSingleton<MainWindow>();
            services.AddSingleton<NoteNest.UI.ViewModels.MainViewModel>();
            
            return services;
        }
    }

    /// <summary>
    /// Complete setup for Silent Save Failure Fix - simplified version without complex bridges
    /// </summary>
    public static class SilentSaveFailureFixExtensions
    {
        // Old service registration methods removed - RTF-integrated save system is now the unified solution
    }

    // Old coordination classes removed - RTF-integrated save system is now the unified solution

    /// <summary>
    /// RTF-Integrated Save System: Clean, unified save system that replaces coordination complexity
    /// </summary>
    public static class RTFIntegratedSaveSystemExtensions
    {
    /// <summary>
        /// Add the RTF-integrated save system to services
        /// This is the new unified save system that simplifies coordination while preserving all RTF functionality
    /// </summary>
        public static IServiceCollection AddRTFIntegratedSaveSystem(this IServiceCollection services)
        {
            // Register the unified save engine as both RTFIntegratedSaveEngine and ISaveManager
            // FIXED: Use deferred initialization to avoid singleton path binding during DI container build
            services.AddSingleton<RTFIntegratedSaveEngine>(serviceProvider =>
            {
                // Use FirstTimeSetupService configured path - this should always be available
                // since FirstTimeSetupService runs before DI container initialization
                var dataPath = FirstTimeSetupService.ConfiguredNotesPath;
                
                if (string.IsNullOrEmpty(dataPath))
                {
                    throw new InvalidOperationException("FirstTimeSetupService.ConfiguredNotesPath is null - this indicates FirstTimeSetupService was not run before DI container initialization");
                }
                
                // Create status notifier using existing state manager
                var stateManager = serviceProvider.GetRequiredService<IStateManager>();
                var statusNotifier = new WPFStatusNotifier(stateManager);
                
                return new RTFIntegratedSaveEngine(dataPath, statusNotifier);
            });
            
            // Register RTFIntegratedSaveEngine as ISaveManager (same instance)
            services.AddSingleton<ISaveManager>(serviceProvider =>
            {
                return serviceProvider.GetRequiredService<RTFIntegratedSaveEngine>();
            });

            // Register the UI wrapper that handles RTF extraction
            services.AddSingleton<RTFSaveEngineWrapper>(serviceProvider =>
            {
                var coreEngine = serviceProvider.GetRequiredService<RTFIntegratedSaveEngine>();
                return new RTFSaveEngineWrapper(coreEngine);
            });

            return services;
        }

        /// <summary>
        /// Add the storage transaction system to services
        /// This enables safe storage location changes with full rollback support
        /// </summary>
        public static IServiceCollection AddStorageTransactionSystem(this IServiceCollection services)
        {
            // Core transaction components
            services.AddSingleton<ISaveManagerFactory, SaveManagerFactory>();
            services.AddSingleton<IStorageTransactionManager, StorageTransactionManager>();
            services.AddSingleton<ITransactionalSettingsService, TransactionalSettingsService>();
            
            // Validation service
            services.AddSingleton<IValidationService, ValidationService>();
            
            return services;
        }

        /// <summary>
        /// Add storage transaction system to UI services
        /// This should be called after AddNoteNestServices() to enhance existing services
        /// </summary>
        public static IServiceCollection AddStorageTransactionUI(this IServiceCollection services)
        {
            // Add the core transaction system
            services.AddStorageTransactionSystem();
            
            // UI-specific transaction components could be added here
            // For example: progress dialog services, transaction status UI, etc.
            
            return services;
        }

        /// <summary>
        /// Add modern FTS5 search services using clean IOptions<T> configuration
        /// Completely replaces legacy ConfigurationService.Settings anti-pattern
        /// </summary>
        public static IServiceCollection AddFTS5SearchServices(this IServiceCollection services)
        {
            // Core FTS5 services with clean dependencies
            services.AddSingleton<IFts5Repository>(serviceProvider =>
            {
                return new Fts5Repository(serviceProvider.GetService<IAppLogger>());
            });

            services.AddSingleton<ISearchResultMapper, SearchResultMapper>();

            services.AddSingleton<ISearchIndexManager>(serviceProvider =>
            {
                return new Fts5IndexManager(
                    serviceProvider.GetRequiredService<IFts5Repository>(),
                    serviceProvider.GetRequiredService<ISearchResultMapper>(),
                    serviceProvider.GetRequiredService<IStorageOptions>(),
                    serviceProvider.GetService<IAppLogger>()
                );
            });

            // Modern FTS5SearchService with clean IOptions<T> dependencies
            services.AddSingleton<NoteNest.UI.Interfaces.ISearchService>(serviceProvider =>
            {
                System.Diagnostics.Debug.WriteLine($"[DI] Creating FTS5SearchService instance at {DateTime.Now:HH:mm:ss.fff}");
                return new FTS5SearchService(
                    serviceProvider.GetRequiredService<IFts5Repository>(),
                    serviceProvider.GetRequiredService<ISearchResultMapper>(),
                    serviceProvider.GetRequiredService<ISearchIndexManager>(),
                    serviceProvider.GetRequiredService<ISearchOptions>(),
                    serviceProvider.GetRequiredService<IStorageOptions>(),
                    serviceProvider.GetService<IAppLogger>()
                );
            });
            
            // Clean SearchViewModel registration (no complex factory logic)
            services.AddSingleton<NoteNest.UI.ViewModels.SearchViewModel>();

            return services;
        }
    }

}
