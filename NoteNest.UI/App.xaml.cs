using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
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
        
        // ✅ CRITICAL: Register global exception handlers BEFORE anything can fail
        // These prevent silent crashes and capture diagnostic information
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        
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

                // ✅ CRITICAL: Synchronize projections BEFORE any UI loads
                // Fixes session persistence bug where TodoStore/CategoryStore loaded stale projection data
                // This ensures todo_view, tree_view, etc. are current before plugins query them
                // Performance: Fast when projections current (~18-30ms), only slow when behind
                _logger.Info("📊 Synchronizing projections with event store...");
                var projOrchestrator = _host.Services.GetRequiredService<NoteNest.Application.Common.Interfaces.IProjectionOrchestrator>();
                var syncStartTime = DateTime.UtcNow;
                await projOrchestrator.CatchUpAsync();
                var syncElapsed = (DateTime.UtcNow - syncStartTime).TotalMilliseconds;
                _logger.Info($"✅ Projections synchronized in {syncElapsed:F0}ms - UI ready to load");

                // 🔍 Run startup diagnostics to detect data integrity issues (circular references, etc.)
                try
                {
                    var diagnosticsService = _host.Services.GetRequiredService<NoteNest.Infrastructure.Diagnostics.StartupDiagnosticsService>();
                    await diagnosticsService.RunDiagnosticsAsync();
                }
                catch (Exception diagEx)
                {
                    _logger.Error(diagEx, "⚠️ Startup diagnostics failed - continuing with app startup");
                    // Don't fail startup if diagnostics fail - they're informational
                }

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
        
        // =============================================================================
        // GLOBAL EXCEPTION HANDLERS - Prevent Silent Crashes
        // =============================================================================
        
        /// <summary>
        /// Handles unhandled exceptions on the UI thread (Dispatcher).
        /// Critical for preventing silent crashes from UI operations.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogCrashToFile("UI_THREAD_CRASH", e.Exception);
            
            // Mark as handled to prevent application termination
            e.Handled = true;
            
            // Show user-friendly error message
            try
            {
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe application will continue running.\nCrash details saved to Logs folder.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // If MessageBox fails, at least we logged the crash
            }
        }
        
        /// <summary>
        /// Handles unobserved exceptions from async/await tasks.
        /// Critical for preventing crashes from fire-and-forget async operations.
        /// </summary>
        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogCrashToFile("ASYNC_TASK_CRASH", e.Exception);
            
            // Mark as observed to prevent application termination
            e.SetObserved();
        }
        
        /// <summary>
        /// Handles unhandled exceptions on background threads.
        /// This is the last line of defense - if this fires, crash is likely fatal.
        /// </summary>
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            LogCrashToFile("BACKGROUND_THREAD_CRASH", exception);
            
            // Note: We cannot prevent termination for background thread exceptions
            // but at least we log them
            if (e.IsTerminating)
            {
                try
                {
                    MessageBox.Show(
                        $"Fatal error - application must close:\n\n{exception?.Message}\n\nCrash details saved to Logs folder.",
                        "Fatal Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Stop);
                }
                catch { }
            }
        }
        
        /// <summary>
        /// Writes crash information to file with multiple fallback strategies.
        /// Ensures we capture crash details even if logging system is broken.
        /// </summary>
        private void LogCrashToFile(string crashType, Exception exception)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var crashFileName = $"CRASH_{crashType}_{timestamp}.txt";
            
            try
            {
                // Try to use configured logger first (goes to standard log file)
                var logger = _logger ?? AppLogger.Instance;
                logger.Fatal(exception, $"🚨 UNHANDLED EXCEPTION [{crashType}]");
            }
            catch
            {
                // Logger failed, continue to file writing
            }
            
            // Always write dedicated crash file for easy diagnosis
            try
            {
                var crashDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NoteNest",
                    "Crashes");
                
                Directory.CreateDirectory(crashDir);
                var crashFile = Path.Combine(crashDir, crashFileName);
                
                var crashReport = 
                    $"╔════════════════════════════════════════════════════════════════╗\n" +
                    $"║  NOTENEST CRASH REPORT - {crashType}\n" +
                    $"╚════════════════════════════════════════════════════════════════╝\n\n" +
                    $"Timestamp:       {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n" +
                    $"Crash Type:      {crashType}\n" +
                    $"Exception Type:  {exception?.GetType().FullName ?? "Unknown"}\n" +
                    $"Message:         {exception?.Message ?? "No message"}\n\n" +
                    $"─────────────────────────────────────────────────────────────────\n" +
                    $"STACK TRACE:\n" +
                    $"─────────────────────────────────────────────────────────────────\n" +
                    $"{exception?.StackTrace ?? "No stack trace available"}\n\n";
                
                // Add inner exception details if present
                if (exception?.InnerException != null)
                {
                    crashReport += 
                        $"─────────────────────────────────────────────────────────────────\n" +
                        $"INNER EXCEPTION:\n" +
                        $"─────────────────────────────────────────────────────────────────\n" +
                        $"Type:    {exception.InnerException.GetType().FullName}\n" +
                        $"Message: {exception.InnerException.Message}\n" +
                        $"Stack:   {exception.InnerException.StackTrace}\n\n";
                }
                
                // Add aggregated exception details if present
                if (exception is AggregateException aggEx)
                {
                    crashReport += 
                        $"─────────────────────────────────────────────────────────────────\n" +
                        $"AGGREGATE EXCEPTION - {aggEx.InnerExceptions.Count} Inner Exceptions:\n" +
                        $"─────────────────────────────────────────────────────────────────\n";
                    
                    for (int i = 0; i < aggEx.InnerExceptions.Count; i++)
                    {
                        var inner = aggEx.InnerExceptions[i];
                        crashReport += $"\n[{i + 1}] {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}\n";
                    }
                }
                
                crashReport += 
                    $"\n─────────────────────────────────────────────────────────────────\n" +
                    $"DIAGNOSTIC INFORMATION:\n" +
                    $"─────────────────────────────────────────────────────────────────\n" +
                    $"OS Version:           {Environment.OSVersion}\n" +
                    $".NET Version:         {Environment.Version}\n" +
                    $"Working Directory:    {Environment.CurrentDirectory}\n" +
                    $"Machine Name:         {Environment.MachineName}\n" +
                    $"User:                 {Environment.UserName}\n" +
                    $"Process ID:           {Environment.ProcessId}\n" +
                    $"64-bit Process:       {Environment.Is64BitProcess}\n" +
                    $"Memory (Working Set): {Environment.WorkingSet / 1024 / 1024} MB\n" +
                    $"\n═══════════════════════════════════════════════════════════════\n";
                
                File.WriteAllText(crashFile, crashReport);
                
                // Also log to debug output
                System.Diagnostics.Debug.WriteLine($"\n🚨 CRASH LOG WRITTEN: {crashFile}\n{crashReport}");
            }
            catch (Exception fileEx)
            {
                // Final fallback - write to desktop if all else fails
                try
                {
                    var desktopCrash = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        crashFileName);
                    File.WriteAllText(desktopCrash, $"CRASH: {exception}\n\nFile write error: {fileEx}");
                }
                catch
                {
                    // Absolutely nothing we can do - crash info is lost
                    System.Diagnostics.Debug.WriteLine($"CRITICAL: Could not write crash log anywhere!");
                }
            }
        }
    }
}
