using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Implementation;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Services.Safety;
using NoteNest.Core.Services.Notes;
using NoteNest.UI.ViewModels;
using NoteNest.UI.Services.DragDrop;
using NoteNest.Core.Plugins;
using NoteNest.UI.Plugins.Todo.Services;

namespace NoteNest.UI.Services
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddNoteNestServices(this IServiceCollection services)
        {
            // ONLY register services that improve testability or are truly shared
            
            // Core Infrastructure (Fast Singletons)
            services.AddSingleton<IAppLogger>(sp => AppLogger.Instance);
            services.AddSingleton<IFileSystemProvider, DefaultFileSystemProvider>();
            services.AddSingleton<IEventBus, EventBus>();
            services.AddSingleton<ConfigurationService>(sp => new ConfigurationService(
                sp.GetRequiredService<IFileSystemProvider>(),
                sp.GetService<IEventBus>()));
            services.AddSingleton<IWorkspaceStateService, WorkspaceStateService>();
            services.AddSingleton<ITabPersistenceService>(sp => new TabPersistenceService(
                sp.GetRequiredService<ConfigurationService>(),
                sp.GetRequiredService<IAppLogger>(),
                sp.GetRequiredService<IWorkspaceService>()));
            // UI
            services.AddSingleton<MainWindow>();
            // EventBus planned for future integration; placeholder left out to avoid large changes
            
            // State Management (Lightweight Singletons)
            services.AddSingleton<IStateManager, StateManager>();
            services.AddSingleton<IServiceErrorHandler, ServiceErrorHandler>();
            services.AddSingleton<IMarkdownService, MarkdownService>();
            services.AddSingleton<ToastNotificationService>();
            
            // Essential Services Only
            services.AddSingleton<NoteService>(sp => new NoteService(
                sp.GetRequiredService<IFileSystemProvider>(),
                sp.GetRequiredService<ConfigurationService>(),
                sp.GetRequiredService<IAppLogger>(),
                sp.GetService<IEventBus>(),
                sp.GetRequiredService<IMarkdownService>(),
                sp.GetService<SafeFileService>(),
                sp.GetService<INoteStorageService>(),
                sp.GetService<IUserNotificationService>())); // Core functionality
            services.AddSingleton<IDialogService, DialogService>(); // UI interaction
            services.AddSingleton<IUserNotificationService>(sp =>
            {
                var logger = sp.GetService<IAppLogger>();
                // MainWindow may not be created yet; service tolerates null
                return new UserNotificationService(Application.Current?.MainWindow, logger);
            });
            services.AddSingleton<SafeFileService>();
            services.AddSingleton<INoteStorageService>(sp => new NoteStorageService(
                sp.GetRequiredService<IFileSystemProvider>(),
                sp.GetService<SafeFileService>(),
                sp.GetService<IAppLogger>()));

            // Workspace Services (Singleton for performance)
            services.AddSingleton<ContentCache>(sp =>
            {
                var bus = sp.GetRequiredService<IEventBus>();
                var config = sp.GetRequiredService<ConfigurationService>();
                var s = config.Settings;
                return new ContentCache(
                    bus,
                    maxCacheSizeMB: s.ContentCacheMaxMB,
                    expirationMinutes: s.ContentCacheExpirationMinutes,
                    cleanupMinutes: s.ContentCacheCleanupMinutes);
            });

            // Note: FileWatcherService is constructed lazily in MainViewModel with ConfigurationService injection.
            services.AddSingleton<INoteOperationsService, NoteOperationsService>();
            services.AddSingleton<IWorkspaceService, WorkspaceService>();
            services.AddSingleton<ITabCloseService, TabCloseService>();

            // Drag & Drop services
            services.AddSingleton<TabDragManager>();
            services.AddSingleton<DropZoneManager>();
            services.AddSingleton<SpringLoadedPaneManager>();

            // ViewModels (Singleton for MainViewModel, Transient for others)
            services.AddSingleton<MainViewModel>();
            services.AddTransient<SettingsViewModel>();

            // NOTE: WorkspaceViewModel is created lazily in MainViewModel
            // This maintains the performance optimization while ensuring proper DI

            // Plugin system services
            services.AddSingleton<IPluginDataStore, PluginDataStore>();
            services.AddSingleton<IPluginManager, PluginManager>();
            services.AddSingleton<ITodoService, TodoService>();

            return services;
        }
    }
}