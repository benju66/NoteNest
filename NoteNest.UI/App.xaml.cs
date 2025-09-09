using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces.Services;
using NoteNest.UI.Services;
using NoteNest.UI.ViewModels;
using NoteNest.UI.Controls;
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

                // Create window via DI, wire services, then show
                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                var mainViewModel = ServiceProvider.GetRequiredService<MainViewModel>();
                mainWindow.DataContext = mainViewModel;
                
                try
                {
                    var dlg = ServiceProvider.GetService<IDialogService>();
                    if (dlg != null)
                    {
                        dlg.OwnerWindow = mainWindow;
                    }
                }
                catch { }
                
                MainWindow = mainWindow;
                mainWindow.Show();
                
                _startupTimer.Stop();
                _logger.Info($"App started in {_startupTimer.ElapsedMilliseconds}ms");
                
                try
                {
                    var metricsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_metrics.txt");
                    File.AppendAllText(metricsPath, $"{DateTime.Now}: {_startupTimer.ElapsedMilliseconds}ms{Environment.NewLine}");
                }
                catch { }
                
                // Load plugins
                try
                {
                    var pluginManager = ServiceProvider.GetRequiredService<IPluginManager>();
                    await pluginManager.LoadPluginAsync(new NoteNest.UI.Plugins.TestPlugin { IsEnabled = true });

                    // Load Todo plugin via DI (fail fast if not registered)
                    var todoService = ServiceProvider.GetRequiredService<NoteNest.UI.Plugins.Todo.Services.ITodoService>();
                    await pluginManager.LoadPluginAsync(new NoteNest.UI.Plugins.Todo.TodoPlugin(todoService) { IsEnabled = true });
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
                try
                {
                    _logger?.Fatal(e.ExceptionObject as Exception, "Unhandled exception");
                    var err = ServiceProvider?.GetService(typeof(NoteNest.Core.Interfaces.Services.IServiceErrorHandler)) as NoteNest.Core.Interfaces.Services.IServiceErrorHandler;
                    err?.LogError(e.ExceptionObject as Exception ?? new Exception("Unknown error"), "AppDomain");
                }
                catch { }
            };
            
            Current.DispatcherUnhandledException += (s, e) =>
            {
                try
                {
                    _logger?.Error(e.Exception, "UI thread exception");
                    var toast = ServiceProvider?.GetService(typeof(ToastNotificationService)) as ToastNotificationService;
                    var err = ServiceProvider?.GetService(typeof(NoteNest.Core.Interfaces.Services.IServiceErrorHandler)) as NoteNest.Core.Interfaces.Services.IServiceErrorHandler;
                    err?.LogError(e.Exception, "UI Thread");
                    // Throttle toasts to at most one every 5 seconds
                    var now = DateTime.UtcNow;
                    if (_lastToastAt == DateTime.MinValue || (now - _lastToastAt).TotalSeconds > 5)
                    {
                        _lastToastAt = now;
                        toast?.Error(string.IsNullOrWhiteSpace(e.Exception?.Message) ? "An unexpected error occurred" : e.Exception.Message);
                    }
                }
                catch { }
                e.Handled = true; // Keep app running
            };
        }

        private static DateTime _lastToastAt = DateTime.MinValue;

        private void ShowStartupError(Exception ex)
        {
            MessageBox.Show(
                $"Failed to start: {ex.Message}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    var child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                var saveManager = ServiceProvider?.GetService<ISaveManager>();
                if (saveManager != null)
                {
                    // Save all dirty notes with timeout
                    var saveTask = saveManager.SaveAllDirtyAsync();
                    var timeoutTask = Task.Delay(10000); // 10 second timeout
                    
                    var completedTask = await Task.WhenAny(saveTask, timeoutTask);
                    
                    BatchSaveResult result = null;
                    if (completedTask == saveTask)
                    {
                        result = await saveTask;
                    }
                    else
                    {
                        _logger?.Warning("Save timeout during shutdown");
                    }
                    
                    // If any failed or timed out, write emergency files
                    var dirtyNotes = saveManager.GetDirtyNoteIds();
                    if (dirtyNotes.Count > 0)
                    {
                        foreach (var noteId in dirtyNotes)
                        {
                            try
                            {
                                var content = saveManager.GetContent(noteId);
                                var filePath = saveManager.GetFilePath(noteId);
                                var fileName = Path.GetFileNameWithoutExtension(filePath);
                                
                                var emergencyPath = Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                                    $"NoteNest_Recovery_{fileName}_{DateTime.Now:yyyyMMddHHmmss}.txt"
                                );
                                
                                await File.WriteAllTextAsync(emergencyPath, content);
                                _logger?.Info($"Created emergency recovery file: {emergencyPath}");
                            }
                            catch (Exception ex)
                            {
                                _logger?.Error(ex, $"Failed to create emergency file for note: {noteId}");
                            }
                        }
                    }
                    
                    // Save tab state AFTER saves complete
                    var tabPersistence = ServiceProvider?.GetService<ITabPersistenceService>();
                    if (tabPersistence != null)
                    {
                        try
                        {
                            var workspace = ServiceProvider?.GetService<IWorkspaceService>();
                            if (workspace != null)
                            {
                                await tabPersistence.SaveAsync(
                                    workspace.OpenTabs,
                                    workspace.SelectedTab?.Id,
                                    null);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.Error(ex, "Failed to save tab state during shutdown");
                        }
                    }
                    
                    // Dispose save manager
                    saveManager.Dispose();
                }
                
                // Dispose other services
                if (MainWindow?.DataContext is IDisposable vm)
                {
                    vm.Dispose();
                }
                
                _host?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error during application shutdown");
            }

            base.OnExit(e);
        }
    }
}