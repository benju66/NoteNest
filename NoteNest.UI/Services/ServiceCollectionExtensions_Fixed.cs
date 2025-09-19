using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Core.Services;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Models;

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
            services.AddSingleton<ConfigurationService>(serviceProvider =>
            {
                // CRITICAL FIX: Provide proper dependencies to ConfigurationService
                return new ConfigurationService(
                    serviceProvider.GetRequiredService<IFileSystemProvider>(),
                    serviceProvider.GetRequiredService<IEventBus>()
                );
            });
            services.AddSingleton<IStateManager, NoteNest.Core.Services.Implementation.StateManager>();
            services.AddSingleton<IServiceErrorHandler, NoteNest.Core.Services.Implementation.ServiceErrorHandler>();
            services.AddSingleton<IFileSystemProvider, DefaultFileSystemProvider>();
            services.AddSingleton<IEventBus, EventBus>();
            
            // Save Management Services
            services.AddSingleton<IWriteAheadLog>(serviceProvider =>
            {
                // Get configuration for WAL path with safe fallback
                var configService = serviceProvider.GetRequiredService<ConfigurationService>();
                var basePath = configService.Settings.DefaultNotePath;
                
                // Fallback if DefaultNotePath is null (during startup)
                if (string.IsNullOrEmpty(basePath))
                {
                    basePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "NoteNest");
                }
                
                var walPath = Path.Combine(basePath, ".wal");
                return new WriteAheadLog(walPath);
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

            // Tree Services - PREVIOUSLY MISSING
            services.AddSingleton<ITreeDataService, TreeDataService>();
            services.AddSingleton<ITreeOperationService>(serviceProvider =>
            {
                return new TreeOperationService(
                    serviceProvider.GetRequiredService<ICategoryManagementService>(),
                    serviceProvider.GetRequiredService<INoteOperationsService>(),
                    serviceProvider.GetRequiredService<IAppLogger>()
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
                    new NoteNest.Core.Services.NoteMetadataManager(
                        serviceProvider.GetRequiredService<IFileSystemProvider>(),
                        serviceProvider.GetRequiredService<IAppLogger>())
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
                // CRITICAL FIX: Use FirstTimeSetupService configured path to avoid timing issues
                var dataPath = FirstTimeSetupService.ConfiguredNotesPath;
                
                // If FirstTimeSetupService didn't run (shouldn't happen), fall back to ConfigurationService
                if (string.IsNullOrEmpty(dataPath))
                {
                    var configService = serviceProvider.GetRequiredService<ConfigurationService>();
                    dataPath = configService.Settings.DefaultNotePath;
                    
                    // Final fallback if settings also empty
                    if (string.IsNullOrEmpty(dataPath))
                    {
                        dataPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                            "NoteNest");
                    }
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
    }

}
