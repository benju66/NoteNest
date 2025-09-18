using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Diagnostics;
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
                        services.AddNoteNestServices(); // Core services
                        services.AddRTFIntegratedSaveSystem(); // Unified save engine with RTF integration
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
                
                // RTF-integrated save system is now active and doesn't need timer coordination
                
                // HIGH-IMPACT MEMORY FIX: Set memory baseline after startup
                SimpleMemoryTracker.SetBaseline();
                
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
                // RTF-INTEGRATED: Use unified save system (now the only system)
                var saveManager = ServiceProvider?.GetService<ISaveManager>();
                
                bool saveSucceeded = false;
                
                // Primary: Use RTF-integrated save system
                _logger?.Info("Using RTF-integrated save system for shutdown");
                try
                {
                    var rtfSaveResult = await RTFIntegratedShutdownSaveAsync();
                    saveSucceeded = rtfSaveResult.SuccessCount > 0 || rtfSaveResult.FailureCount == 0;
                    _logger?.Info($"RTF-integrated shutdown save completed: {rtfSaveResult.SuccessCount} succeeded, {rtfSaveResult.FailureCount} failed");
                }
                catch (Exception rtfEx)
                {
                    _logger?.Error(rtfEx, "RTF-integrated save system failed during shutdown - falling back to ISaveManager");
                }
                
                // Fallback: Use ISaveManager interface if RTF-integrated save had issues
                if (!saveSucceeded && saveManager != null)
                {
                    _logger?.Info("Using legacy save system for shutdown");
                    // Save all dirty notes with timeout
                    var saveTask = saveManager.SaveAllDirtyAsync();
                    var timeoutTask = Task.Delay(10000); // 10 second timeout
                    
                    var completedTask = await Task.WhenAny(saveTask, timeoutTask);
                    
                    if (completedTask == saveTask)
                    {
                        var legacyResult = await saveTask;
                        _logger?.Info($"Legacy save completed: {legacyResult.SuccessCount} succeeded, {legacyResult.FailureCount} failed");
                        saveSucceeded = legacyResult.SuccessCount > 0 || legacyResult.FailureCount == 0;
                    }
                    else
                    {
                        _logger?.Warning("Legacy save timeout during shutdown");
                    }
                }
                
                // If any saves failed or timed out, write emergency files
                if (saveManager != null)
                {
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
                
                // HIGH-IMPACT MEMORY FIX: Log final memory status
                DebugLogger.LogMemory("Before app shutdown");
                
                // Dispose other services
                if (MainWindow?.DataContext is IDisposable vm)
                {
                    vm.Dispose();
                }
                
                _host?.Dispose();
                
                // Final memory report
                DebugLogger.LogMemory("After app shutdown");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error during application shutdown");
            }

            base.OnExit(e);
        }

        /// <summary>
        /// RTF-integrated shutdown save implementation
        /// Uses the new unified save system for all dirty tabs
        /// </summary>
        private async Task<BatchSaveResult> RTFIntegratedShutdownSaveAsync()
        {
            var result = new BatchSaveResult();
            int successCount = 0;
            int failureCount = 0;
            
            try
            {
                // Get RTF save wrapper service
                var rtfSaveWrapper = ServiceProvider?.GetService<RTFSaveEngineWrapper>();
                var workspaceService = ServiceProvider?.GetService<IWorkspaceService>();
                
                if (rtfSaveWrapper == null || workspaceService == null)
                {
                    _logger?.Warning("RTF-integrated save services not available during shutdown");
                    return result; // Return empty result, will trigger fallback
                }

                // Get all dirty tabs using workspace service
                var dirtyTabs = workspaceService.OpenTabs.Where(t => t.IsDirty).ToList();
                
                if (!dirtyTabs.Any())
                {
                    _logger?.Info("No dirty tabs to save during shutdown");
                    return result;
                }

                _logger?.Info($"RTF-integrated shutdown save starting for {dirtyTabs.Count} dirty tabs");

                // Use semaphore to limit concurrent saves (prevent overwhelming system during shutdown)
                using var semaphore = new SemaphoreSlim(3, 3); // Max 3 concurrent saves
                
                var saveTasks = dirtyTabs.Select(async tab =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        // Try RTF-specific save first (preferred path)
                        if (tab is NoteTabItem noteTabItem && noteTabItem.Editor != null)
                        {
                            var saveResult = await rtfSaveWrapper.SaveFromRichTextBoxAsync(
                                tab.NoteId,
                                noteTabItem.Editor,
                                tab.Title ?? "Untitled",
                                NoteNest.Core.Services.SaveType.AppShutdown
                            );
                            
                            if (saveResult.Success)
                            {
                                Interlocked.Increment(ref successCount);
                                _logger?.Debug($"RTF shutdown save succeeded: {tab.Title}");
                                return true;
                            }
                            else
                            {
                                Interlocked.Increment(ref failureCount);
                                lock (result.FailedNoteIds) { result.FailedNoteIds.Add(tab.NoteId); }
                                _logger?.Warning($"RTF shutdown save failed: {tab.Title} - {saveResult.Error}");
                                return false;
                            }
                        }
                        else
                        {
                            // ENHANCED: Fallback to ISaveManager for any non-RTF tabs (should be rare in RTF-only architecture)
                            _logger?.Info($"Tab {tab.Title} using ISaveManager fallback for shutdown save");
                            
                            var saveManager = ServiceProvider?.GetService<ISaveManager>();
                            if (saveManager != null)
                            {
                                try
                                {
                                    var success = await saveManager.SaveNoteAsync(tab.NoteId);
                                    if (success)
                                    {
                                        Interlocked.Increment(ref successCount);
                                        _logger?.Debug($"ISaveManager shutdown save succeeded: {tab.Title}");
                                        return true;
                                    }
                                    else
                                    {
                                        Interlocked.Increment(ref failureCount);
                                        lock (result.FailedNoteIds) { result.FailedNoteIds.Add(tab.NoteId); }
                                        _logger?.Warning($"ISaveManager shutdown save failed: {tab.Title}");
                                        return false;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Interlocked.Increment(ref failureCount);
                                    lock (result.FailedNoteIds) { result.FailedNoteIds.Add(tab.NoteId); }
                                    _logger?.Error(ex, $"ISaveManager shutdown save error: {tab.Title}");
                                    return false;
                                }
                            }
                            else
                            {
                                _logger?.Error($"No save method available for tab: {tab.Title}");
                                Interlocked.Increment(ref failureCount);
                                lock (result.FailedNoteIds) { result.FailedNoteIds.Add(tab.NoteId); }
                                return false;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failureCount);
                        lock (result.FailedNoteIds) { result.FailedNoteIds.Add(tab.NoteId); }
                        _logger?.Error(ex, $"RTF shutdown save error for tab: {tab.Title}");
                        return false;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                // Wait for all saves to complete with timeout
                var saveTask = Task.WhenAll(saveTasks);
                var timeoutTask = Task.Delay(12000); // 12 second timeout for RTF saves
                
                var completedTask = await Task.WhenAny(saveTask, timeoutTask);
                
                if (completedTask == saveTask)
                {
                    await saveTask; // Ensure all tasks completed
                    result.SuccessCount = successCount;
                    result.FailureCount = failureCount;
                    _logger?.Info($"RTF-integrated shutdown save completed: {successCount} succeeded, {failureCount} failed");
                }
                else
                {
                    _logger?.Warning("RTF-integrated shutdown save timeout - some saves may not have completed");
                    result.SuccessCount = successCount;
                    result.FailureCount = dirtyTabs.Count - successCount; // Mark remaining as failed due to timeout
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error in RTF-integrated shutdown save");
                result.FailureCount = 1;
                return result;
            }
        }
    }
}