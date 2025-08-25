using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Services;
using NoteNest.UI.ViewModels;
using NoteNest.Core.Plugins;

namespace NoteNest.UI
{
    public partial class App : Application
    {
        private IHost _host;
        private IAppLogger _logger;
        private Stopwatch _startupTimer;

        public IServiceProvider ServiceProvider { get; private set; }

        protected override async void OnStartup(StartupEventArgs e)
        {
            _startupTimer = Stopwatch.StartNew();
            
            try
            {
                try
                {
                    var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteNest");
                    Directory.CreateDirectory(logDir);
                    var logFile = Path.Combine(logDir, "debug.log");
                    System.Diagnostics.Trace.Listeners.Add(new TextWriterTraceListener(logFile));
                    System.Diagnostics.Trace.AutoFlush = true;
                    System.Diagnostics.Debug.WriteLine($"[App] Debug listener configured at {logFile} {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                }
                catch { }
                // Ultra-fast startup sequence
                ShutdownMode = ShutdownMode.OnMainWindowClose;

                // Minimal DI container (only essential services)
                _host = Host.CreateDefaultBuilder()
                    .ConfigureServices((context, services) =>
                    {
                        services.AddNoteNestServices(); // Only fast services
                    })
                    .Build();

                ServiceProvider = _host.Services;
                _logger = ServiceProvider.GetRequiredService<IAppLogger>();

                // Skip validation in production for speed
                #if DEBUG
                ValidateCriticalServices();
                #endif

                // Initialize theme (fast)
                try
                {
                    ThemeService.Initialize();
                }
                catch (Exception themeEx)
                {
                    _logger?.Warning($"Theme init failed: {themeEx.Message}");
                }

                // Create and show window immediately
                var mainWindow = new MainWindow();
                var mainViewModel = ServiceProvider.GetRequiredService<MainViewModel>();
                mainWindow.DataContext = mainViewModel;
                
                mainWindow.Show();
                MainWindow = mainWindow;
                
                _startupTimer.Stop();
                _logger.Info($"App started in {_startupTimer.ElapsedMilliseconds}ms");
                
                try
                {
                    var metricsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_metrics.txt");
                    File.AppendAllText(metricsPath, $"{DateTime.Now}: {_startupTimer.ElapsedMilliseconds}ms{Environment.NewLine}");
                }
                catch { }
                
                // Load test plugin (Phase 2 validation)
                try
                {
                    var pluginManager = ServiceProvider.GetService<IPluginManager>();
                    if (pluginManager != null)
                    {
                        await pluginManager.LoadPluginAsync(new NoteNest.UI.Plugins.TestPlugin { IsEnabled = true });
                    }
                }
                catch { }

                // Setup exception handling after startup
                SetupExceptionHandling();
            }
            catch (Exception ex)
            {
                ShowStartupError(ex);
                Shutdown(1);
            }

            base.OnStartup(e);
        }

        #if DEBUG
        private void ValidateCriticalServices()
        {
            // Only validate essential services in debug mode
            try
            {
                ServiceProvider.GetRequiredService<IAppLogger>();
                ServiceProvider.GetRequiredService<NoteNest.Core.Services.ConfigurationService>();
                ServiceProvider.GetRequiredService<NoteNest.Core.Services.NoteService>();
                ServiceProvider.GetRequiredService<MainViewModel>();
                _logger.Debug("Critical services validated");
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Critical service validation failed");
                throw;
            }
        }
        #endif

        private void SetupExceptionHandling()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                _logger?.Fatal(e.ExceptionObject as Exception, "Unhandled exception");
            };
            
            Current.DispatcherUnhandledException += (s, e) =>
            {
                _logger?.Error(e.Exception, "UI thread exception");
                e.Handled = true; // Keep app running
            };
        }

        private void ShowStartupError(Exception ex)
        {
            MessageBox.Show(
                $"Failed to start: {ex.Message}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        

        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                // Flush pending config saves
                try
                {
                    var config = ServiceProvider?.GetService<NoteNest.Core.Services.ConfigurationService>();
                    if (config != null)
                    {
                        await config.FlushPendingAsync();
                    }
                }
                catch { }
                
                // Fast shutdown
                if (MainWindow?.DataContext is IDisposable vm)
                {
                    vm.Dispose();
                }
                
                _host?.Dispose();
                (_logger as IDisposable)?.Dispose();
            }
            catch
            {
                // Ignore shutdown errors
            }

            base.OnExit(e);
        }
    }
}