using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using NoteNest.UI.Composition;
using NoteNest.UI.ViewModels.Shell;

namespace NoteNest.UI
{
    public partial class NewApp : System.Windows.Application
    {
        private ServiceProvider _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .Build();
            
            // Configure services
            var serviceCollection = new ServiceCollection();
            serviceCollection.ConfigureServices(configuration);
            _serviceProvider = serviceCollection.BuildServiceProvider();
            
            // Create and show main window with new ViewModel
            var mainShellViewModel = _serviceProvider.GetRequiredService<MainShellViewModel>();
            var newMainWindow = new NewMainWindow
            {
                DataContext = mainShellViewModel
            };
            
            newMainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }
    }
}
