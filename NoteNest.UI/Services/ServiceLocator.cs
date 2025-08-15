using System;
using Microsoft.Extensions.DependencyInjection;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Temporary service locator for gradual migration.
    /// Will be removed after full DI implementation.
    /// </summary>
    public static class ServiceLocator
    {
        private static IServiceProvider _serviceProvider;
        
        public static void SetServiceProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }
        
        public static T GetService<T>() where T : class
        {
            if (_serviceProvider == null)
            {
                // Fallback for Phase 1 - services not yet implemented
                return null;
            }
            
            return _serviceProvider.GetService<T>();
        }
        
        public static T GetRequiredService<T>() where T : class
        {
            if (_serviceProvider == null)
            {
                throw new InvalidOperationException(
                    "ServiceProvider not initialized. Call SetServiceProvider first.");
            }
            
            return _serviceProvider.GetRequiredService<T>();
        }
    }
}