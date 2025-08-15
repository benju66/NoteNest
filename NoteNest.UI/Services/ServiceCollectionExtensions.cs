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
            // Core Services (Singleton - shared across app lifetime)
            services.AddSingleton<IAppLogger, AppLogger>(sp => AppLogger.Instance);
            services.AddSingleton<IFileSystemProvider, DefaultFileSystemProvider>();
            services.AddSingleton<ConfigurationService>();
            services.AddSingleton<NoteService>();
            services.AddSingleton<FileWatcherService>();
            services.AddSingleton<SearchIndexService>();
            services.AddSingleton<ContentCache>();
            
            // New Services (Singleton)
            services.AddSingleton<IServiceErrorHandler, ServiceErrorHandler>();
            services.AddSingleton<IStateManager, StateManager>();
            services.AddSingleton<INoteOperationsService, NoteOperationsService>();
            services.AddSingleton<ICategoryManagementService, CategoryManagementService>();
            services.AddSingleton<IWorkspaceService, WorkspaceService>();
            
            // ViewModels (Transient - new instance each time)
            services.AddTransient<MainViewModel>();
            services.AddTransient<SettingsViewModel>();
            
            return services;
        }
    }
}