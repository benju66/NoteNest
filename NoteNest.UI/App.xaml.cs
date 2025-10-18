using System;
using System.IO;
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

                // Initialize databases (events.db + projections.db) BEFORE any queries run
                _logger.Info("🔧 Initializing event store and projections...");
                var eventStoreInit = _host.Services.GetRequiredService<NoteNest.Infrastructure.EventStore.EventStoreInitializer>();
                var projectionsInit = _host.Services.GetRequiredService<NoteNest.Infrastructure.Projections.ProjectionsInitializer>();
                
                await eventStoreInit.InitializeAsync();
                await projectionsInit.InitializeAsync();
                
                _logger.Info("✅ Databases initialized successfully");

                // Auto-rebuild from RTF files if event store is empty (database loss recovery)
                var eventStore = _host.Services.GetRequiredService<NoteNest.Application.Common.Interfaces.IEventStore>();
                var currentPosition = await eventStore.GetCurrentStreamPositionAsync();
                
                if (currentPosition == 0)
                {
                    _logger.Info("📂 Empty event store detected - rebuilding from RTF files...");
                    
                    var notesRootPath = _host.Services.GetRequiredService<IConfiguration>().GetValue<string>("NotesPath") 
                                        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteNest");
                    
                    var projectionOrchestrator = _host.Services.GetRequiredService<NoteNest.Infrastructure.Projections.ProjectionOrchestrator>();
                    
                    var fileSystemMigrator = new NoteNest.Infrastructure.Migrations.FileSystemMigrator(
                        notesRootPath,
                        eventStore,
                        projectionOrchestrator,
                        _logger);
                    
                    var migrationResult = await fileSystemMigrator.MigrateAsync();
                    
                    if (migrationResult.Success)
                    {
                        _logger.Info($"✅ Rebuilt from RTF files: {migrationResult.NotesFound} notes, {migrationResult.CategoriesFound} categories");
                    }
                    else
                    {
                        _logger.Error($"❌ File system migration failed: {migrationResult.Error}");
                    }
                }
                else
                {
                    _logger.Info($"📊 Event store has data (position {currentPosition}) - skipping file system migration");
                }

                // Initialize theme system FIRST (before creating UI)
                var themeService = _host.Services.GetRequiredService<NoteNest.UI.Services.IThemeService>();
                await themeService.InitializeAsync();
                _logger.Info($"✅ Theme system initialized: {themeService.CurrentTheme}");

                // Initialize multi-window theme coordinator for tear-out functionality
                var multiWindowThemeCoordinator = _host.Services.GetRequiredService<NoteNest.UI.Services.IMultiWindowThemeCoordinator>();
                multiWindowThemeCoordinator.Initialize();
                _logger.Info("✅ Multi-window theme coordinator initialized");

                // 🔍 Initialize search service at startup
                try
                {
                    var searchService = _host.Services.GetRequiredService<NoteNest.UI.Interfaces.ISearchService>();
                    
                    // Initialize the search service and database
                    await searchService.InitializeAsync();
                    
                    // Get indexed document count for diagnostics
                    var docCount = await searchService.GetIndexedDocumentCountAsync();
                    _logger.Info($"🔍 Search service initialized - Indexed documents: {docCount}");
                    
                    // Check if index is empty and log warning
                    if (docCount == 0)
                    {
                        _logger.Warning("⚠️ Search index is empty - background indexing started. First search may take a moment.");
                    }
                    else
                    {
                        _logger.Info($"✅ Search ready with {docCount} documents");
                    }
                }
                catch (Exception searchEx)
                {
                    _logger.Error(searchEx, "❌ Failed to initialize search service - search functionality may not work");
                    // Don't fail startup if search initialization fails - degrade gracefully
                }

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
                    var appDataPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "NoteNest");
                    System.IO.Directory.CreateDirectory(appDataPath);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(appDataPath, "STARTUP_ERROR.txt"), errorDetails);
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
            
            // Close all detached windows first
            try
            {
                var windowManager = _host?.Services?.GetService<NoteNest.UI.Services.IWindowManager>();
                if (windowManager is IDisposable disposableWindowManager)
                {
                    disposableWindowManager.Dispose();
                    _logger?.Info("✅ All detached windows closed");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to close detached windows on exit: {ex.Message}");
            }
            
            // Save workspace state before exit
            try
            {
                if (MainWindow?.DataContext is NoteNest.UI.ViewModels.Shell.MainShellViewModel mainShell)
                {
                    // Synchronous save (OnExit can't be async)
                    mainShell.Workspace.SaveStateAsync().GetAwaiter().GetResult();
                    _logger?.Info("✅ Workspace state saved on exit");
                    
                    // Dispose resources
                    mainShell.Dispose();
                    _logger?.Info("✅ Resources cleaned up on exit");
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
