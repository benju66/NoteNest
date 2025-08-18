using Microsoft.Extensions.DependencyInjection;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Implementation;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.ViewModels;

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
            services.AddSingleton<ConfigurationService>();
            
            // State Management (Lightweight Singletons)
            services.AddSingleton<IStateManager, StateManager>();
            services.AddSingleton<IServiceErrorHandler, ServiceErrorHandler>();
            
            // Essential Services Only
            services.AddSingleton<NoteService>(); // Core functionality
            services.AddSingleton<IDialogService, DialogService>(); // UI interaction

            // Workspace Services (Singleton for performance)
            services.AddSingleton<ContentCache>();
            services.AddSingleton<INoteOperationsService, NoteOperationsService>();
            services.AddSingleton<IWorkspaceService, WorkspaceService>();
            services.AddSingleton<ITabCloseService, TabCloseService>();

            // ViewModels (Singleton for MainViewModel, Transient for others)
            services.AddSingleton<MainViewModel>();
            services.AddTransient<SettingsViewModel>();

            // NOTE: WorkspaceViewModel is created lazily in MainViewModel
            // This maintains the performance optimization while ensuring proper DI

            return services;
        }
    }
}