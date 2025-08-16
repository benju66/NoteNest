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
            
            // ViewModels (Singleton for speed)
            services.AddSingleton<MainViewModel>();
            
            // NOTE: Heavy services are created lazily in MainViewModel
            // This keeps startup fast while maintaining testability for core components
            
            return services;
        }
    }
}