using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Services;
using NoteNest.UI.ViewModels.Shell;
namespace NoteNest.UI
{
    /// <summary>
    /// Minimal, clean application startup - Scorched Earth Rebuild
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private IHost _host;
        private IAppLogger _logger;

        // TEMPORARY: Compatibility with legacy components accessing ServiceProvider
        public IServiceProvider ServiceProvider => _host?.Services;

        protected override async void OnStartup(StartupEventArgs e)
        {
            // GUARANTEED to run - no competing startup paths
            System.Diagnostics.Debug.WriteLine($"🚀 MINIMAL APP STARTUP: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            
            // ALSO write to a file for absolute confirmation
            try 
            {
                System.IO.File.WriteAllText(@"C:\Users\Burness\AppData\Local\NoteNest\CONTROLLED_STARTUP_PROOF.txt", 
                    $"🚀 CONTROLLED STARTUP WORKING: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            } 
            catch { /* ignore errors */ }
            
            try
            {
                // Simple, predictable service configuration
                _host = Host.CreateDefaultBuilder()
                    .ConfigureServices((context, services) =>
                    {
                        ConfigureMinimalServices(services, context.Configuration);
                    })
                    .Build();

                await _host.StartAsync();

                _logger = _host.Services.GetRequiredService<IAppLogger>();
                _logger.Info("🎉 Full NoteNest app started successfully!");

                // DIAGNOSTIC: Test CategoryTreeViewModel creation manually
                try
                {
                    var categoryTreeVm = _host.Services.GetRequiredService<NoteNest.UI.ViewModels.Categories.CategoryTreeViewModel>();
                    _logger.Info($"✅ CategoryTreeViewModel created - Categories count: {categoryTreeVm.Categories.Count}");
                }
                catch (Exception vmEx)
                {
                    _logger.Error(vmEx, "❌ Failed to create CategoryTreeViewModel");
                }

                // Create the REAL NoteNest MainWindow with tree view (using NEW window)
                var mainWindow = _host.Services.GetRequiredService<NoteNest.UI.ViewModels.Shell.MainShellViewModel>();
                _logger.Info($"✅ MainShellViewModel created - CategoryTree.Categories count: {mainWindow.CategoryTree.Categories.Count}");
                
                // Restore workspace state BEFORE showing window
                await mainWindow.Workspace.RestoreStateAsync();
                _logger.Info("✅ Workspace state restored");
                
                var realMainWindow = new NoteNest.UI.NewMainWindow();
                realMainWindow.DataContext = mainWindow;
                realMainWindow.Show();
                
                MainWindow = realMainWindow;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ STARTUP FAILED: {ex.Message}");
                
                // Write detailed error to file for diagnosis
                try 
                {
                    var errorDetails = $"🚨 STARTUP EXCEPTION: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n" +
                                     $"Message: {ex.Message}\n" +
                                     $"StackTrace: {ex.StackTrace}\n" +
                                     $"InnerException: {ex.InnerException?.Message}\n";
                    System.IO.File.WriteAllText(@"C:\Users\Burness\AppData\Local\NoteNest\STARTUP_ERROR.txt", errorDetails);
                } 
                catch { /* ignore errors */ }
                
                MessageBox.Show($"Startup failed: {ex.Message}\n\nFull details written to STARTUP_ERROR.txt", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }

            base.OnStartup(e);
        }

        private void ConfigureMinimalServices(IServiceCollection services, IConfiguration configuration)
        {
            // FULL SERVICES - Database, Tree View, Search, RTF Editor, etc.
            System.Diagnostics.Debug.WriteLine("📦 Configuring FULL services with database...");
            
            // Use our proven Clean Architecture service configuration
            NoteNest.UI.Composition.CleanServiceConfiguration.ConfigureCleanArchitecture(services, configuration);
            
            System.Diagnostics.Debug.WriteLine("✅ Full services configured with database");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _logger?.Info("Minimal app shutting down...");
            
            // Save workspace state before exit
            try
            {
                if (MainWindow?.DataContext is NoteNest.UI.ViewModels.Shell.MainShellViewModel mainShell)
                {
                    // Synchronous save (OnExit can't be async)
                    mainShell.Workspace.SaveStateAsync().GetAwaiter().GetResult();
                    _logger?.Info("✅ Workspace state saved on exit");
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to save workspace state on exit");
            }
            
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}
