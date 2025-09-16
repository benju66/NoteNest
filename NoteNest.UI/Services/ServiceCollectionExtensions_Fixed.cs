using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Core.Services;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Models;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Core service collection extensions - restores the original AddNoteNestServices method
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Add core NoteNest services (restored from original)
        /// </summary>
        public static IServiceCollection AddNoteNestServices(this IServiceCollection services)
        {
            // Core Infrastructure Services
            services.AddSingleton<IAppLogger>(AppLogger.Instance);
            services.AddSingleton<ConfigurationService>(serviceProvider =>
            {
                // CRITICAL FIX: Provide proper dependencies to ConfigurationService
                return new ConfigurationService(
                    serviceProvider.GetRequiredService<IFileSystemProvider>(),
                    serviceProvider.GetRequiredService<IEventBus>()
                );
            });
            services.AddSingleton<IStateManager, NoteNest.Core.Services.Implementation.StateManager>();
            services.AddSingleton<IServiceErrorHandler, NoteNest.Core.Services.Implementation.ServiceErrorHandler>();
            services.AddSingleton<IFileSystemProvider, DefaultFileSystemProvider>();
            services.AddSingleton<IEventBus, EventBus>();
            
            // Save Management Services
            services.AddSingleton<IWriteAheadLog, WriteAheadLog>();
            services.AddSingleton<SaveConfiguration>();
            services.AddSingleton<ISaveManager, UnifiedSaveManager>();
            
            // Note Operation Services - PREVIOUSLY MISSING
            services.AddSingleton<INoteOperationsService>(serviceProvider =>
            {
                return new NoteNest.Core.Services.Implementation.NoteOperationsService(
                    serviceProvider.GetRequiredService<NoteService>(),
                    serviceProvider.GetRequiredService<IServiceErrorHandler>(),
                    serviceProvider.GetRequiredService<IAppLogger>(),
                    serviceProvider.GetRequiredService<IFileSystemProvider>(),
                    serviceProvider.GetRequiredService<ConfigurationService>(),
                    serviceProvider.GetRequiredService<ContentCache>(),
                    serviceProvider.GetRequiredService<ISaveManager>()
                );
            });

            // Category Management Services - PREVIOUSLY MISSING  
            services.AddSingleton<ICategoryManagementService>(serviceProvider =>
            {
                return new NoteNest.Core.Services.Implementation.CategoryManagementService(
                    serviceProvider.GetRequiredService<NoteService>(),
                    serviceProvider.GetRequiredService<ConfigurationService>(),
                    serviceProvider.GetRequiredService<IServiceErrorHandler>(),
                    serviceProvider.GetRequiredService<IAppLogger>(),
                    serviceProvider.GetRequiredService<IFileSystemProvider>(),
                    serviceProvider.GetRequiredService<IEventBus>()
                );
            });

            // Tree Services - PREVIOUSLY MISSING
            services.AddSingleton<ITreeDataService, TreeDataService>();
            services.AddSingleton<ITreeOperationService>(serviceProvider =>
            {
                return new TreeOperationService(
                    serviceProvider.GetRequiredService<ICategoryManagementService>(),
                    serviceProvider.GetRequiredService<INoteOperationsService>(),
                    serviceProvider.GetRequiredService<IAppLogger>()
                );
            });
            services.AddSingleton<ITreeStateManager, TreeStateManager>();
            services.AddSingleton<ITreeController>(serviceProvider =>
            {
                return new TreeController(
                    serviceProvider.GetRequiredService<ITreeDataService>(),
                    serviceProvider.GetRequiredService<ITreeOperationService>(),
                    serviceProvider.GetRequiredService<ITreeStateManager>(),
                    serviceProvider.GetRequiredService<IAppLogger>()
                );
            });

            // UI Services
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IUserNotificationService>(serviceProvider =>
            {
                var logger = serviceProvider.GetService<IAppLogger>();
                // MainWindow may not be created yet; service tolerates null
                return new UserNotificationService(System.Windows.Application.Current?.MainWindow, logger);
            });
            services.AddSingleton<ToastNotificationService>();
            
            // CRITICAL FIX: Register ITabCloseService - was missing!
            services.AddSingleton<ITabCloseService>(serviceProvider =>
            {
                return new TabCloseService(
                    serviceProvider.GetRequiredService<INoteOperationsService>(),
                    serviceProvider.GetRequiredService<IWorkspaceService>(),
                    serviceProvider.GetRequiredService<IDialogService>(),
                    serviceProvider.GetRequiredService<IAppLogger>(),
                    serviceProvider.GetRequiredService<ISaveManager>()
                );
            });
            
            // Content and Search Services
            services.AddSingleton<ContentCache>(serviceProvider =>
            {
                var bus = serviceProvider.GetRequiredService<IEventBus>();
                return new ContentCache(bus);
            });
            
            services.AddSingleton<NoteService>(serviceProvider => 
            {
                return new NoteService(
                    serviceProvider.GetRequiredService<IFileSystemProvider>(),
                    serviceProvider.GetRequiredService<ConfigurationService>(),
                    serviceProvider.GetRequiredService<IAppLogger>(),
                    serviceProvider.GetService<IEventBus>(),
                    serviceProvider.GetService<NoteNest.Core.Services.Safety.SafeFileService>(),
                    serviceProvider.GetService<NoteNest.Core.Services.Notes.INoteStorageService>(),
                    serviceProvider.GetService<IUserNotificationService>(),
                    new NoteNest.Core.Services.NoteMetadataManager(
                        serviceProvider.GetRequiredService<IFileSystemProvider>(),
                        serviceProvider.GetRequiredService<IAppLogger>())
                );
            });

            // Additional Services - PREVIOUSLY MISSING
            services.AddSingleton<NoteNest.Core.Services.Safety.SafeFileService>();
            services.AddSingleton<NoteNest.Core.Services.Notes.INoteStorageService>(serviceProvider =>
            {
                return new NoteNest.Core.Services.Notes.NoteStorageService(
                    serviceProvider.GetRequiredService<IFileSystemProvider>(),
                    serviceProvider.GetService<NoteNest.Core.Services.Safety.SafeFileService>(),
                    serviceProvider.GetService<IAppLogger>()
                );
            });
            
            // Tab and Workspace Services - Register UITabFactory with proper constructor
            services.AddSingleton<ITabFactory>(serviceProvider =>
            {
                return new UITabFactory(
                    serviceProvider.GetRequiredService<ISaveManager>(),
                    serviceProvider.GetService<NoteNest.Core.Services.ISupervisedTaskRunner>()
                );
            });
            services.AddSingleton<ITabPersistenceService, TabPersistenceService>();
            services.AddSingleton<IWorkspaceService, NoteNest.Core.Services.Implementation.WorkspaceService>();
            
            // Window and ViewModels
            services.AddSingleton<MainWindow>();
            services.AddSingleton<NoteNest.UI.ViewModels.MainViewModel>();
            
            return services;
        }
    }

    /// <summary>
    /// Complete setup for Silent Save Failure Fix - simplified version without complex bridges
    /// </summary>
    public static class SilentSaveFailureFixExtensions
    {
        /// <summary>
        /// Add Silent Save Failure Fix to your service collection
        /// Call this AFTER your existing AddNoteNestServices() call
        /// </summary>
        public static IServiceCollection AddSilentSaveFailureFix(this IServiceCollection services)
        {
            // Step 1: Add SupervisedTaskRunner
            services.AddSingleton<NoteNest.Core.Services.ISupervisedTaskRunner>(serviceProvider =>
            {
                var logger = serviceProvider.GetRequiredService<IAppLogger>();
                var notifications = serviceProvider.GetRequiredService<IUserNotificationService>();
                return new NoteNest.Core.Services.SupervisedTaskRunner(logger, notifications);
            });

            // Step 2: Add service bridges with full namespace qualifiers
            services.AddSingleton<NoteNest.Core.Services.IStatusBarService>(serviceProvider =>
            {
                var stateManager = serviceProvider.GetRequiredService<IStateManager>();
                return new NoteNest.Core.Services.StatusBarServiceBridge(stateManager);
            });

            services.AddSingleton<NoteNest.Core.Services.IEnhancedDialogService>(serviceProvider =>
            {
                var dialogBridge = new DialogServiceBridge(serviceProvider.GetRequiredService<IDialogService>());
                return new NoteNest.Core.Services.EnhancedDialogServiceBridge(dialogBridge);
            });

            // Step 3: Add SaveHealthMonitor
            services.AddSingleton<SaveHealthMonitor>(serviceProvider =>
            {
                var taskRunner = serviceProvider.GetRequiredService<NoteNest.Core.Services.ISupervisedTaskRunner>();
                var statusBar = serviceProvider.GetRequiredService<NoteNest.Core.Services.IStatusBarService>();
                return new SaveHealthMonitor(taskRunner, statusBar);
            });

            // Step 4: Update existing services
            UpdateSaveManagerWithSupervisedRunner(services);
            UpdateTabFactoryWithSupervisedRunner(services);

            return services;
        }

        private static void UpdateSaveManagerWithSupervisedRunner(IServiceCollection services)
        {
            // Remove existing ISaveManager registration
            for (int i = services.Count - 1; i >= 0; i--)
            {
                if (services[i].ServiceType == typeof(ISaveManager))
                {
                    services.RemoveAt(i);
                }
            }

            // Re-register with SupervisedTaskRunner
            services.AddSingleton<ISaveManager>(serviceProvider => 
            {
                var logger = serviceProvider.GetRequiredService<IAppLogger>();
                var wal = serviceProvider.GetRequiredService<IWriteAheadLog>();
                var config = serviceProvider.GetService<SaveConfiguration>();
                var taskRunner = serviceProvider.GetRequiredService<NoteNest.Core.Services.ISupervisedTaskRunner>();
                
                return new UnifiedSaveManager(logger, wal, config, taskRunner);
            });
        }

        private static void UpdateTabFactoryWithSupervisedRunner(IServiceCollection services)
        {
            // Remove existing ITabFactory registration
            for (int i = services.Count - 1; i >= 0; i--)
            {
                if (services[i].ServiceType == typeof(ITabFactory))
                {
                    services.RemoveAt(i);
                }
            }

            // Re-register UITabFactory with SupervisedTaskRunner instead of using separate EnhancedTabFactory
            services.AddSingleton<ITabFactory>(serviceProvider =>
            {
                return new UITabFactory(
                    serviceProvider.GetRequiredService<ISaveManager>(),
                    serviceProvider.GetService<NoteNest.Core.Services.ISupervisedTaskRunner>()
                );
            });
        }
    }

    /// <summary>
    /// Enhanced tab factory that creates NoteTabItems with SupervisedTaskRunner support
    /// </summary>
    internal class EnhancedTabFactory : ITabFactory
    {
        private readonly ISaveManager _saveManager;
        private readonly NoteNest.Core.Services.ISupervisedTaskRunner _taskRunner;

        public EnhancedTabFactory(ISaveManager saveManager, NoteNest.Core.Services.ISupervisedTaskRunner taskRunner = null)
        {
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            _taskRunner = taskRunner; // Allow null for backward compatibility
        }

        public ITabItem CreateTab(NoteModel note, string noteId)
        {
            // Ensure note has the correct ID (same pattern as UITabFactory)
            note.Id = noteId;
            return new NoteNest.UI.ViewModels.NoteTabItem(note, _saveManager, _taskRunner);
        }
    }

    /// <summary>
    /// Bridge to existing UI IDialogService 
    /// </summary>
    internal class DialogServiceBridge : NoteNest.Core.Services.IUIDialogService
    {
        private readonly IDialogService _dialogService;

        public DialogServiceBridge(IDialogService dialogService)
        {
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        }

        public async Task<bool> ShowConfirmationDialogAsync(string message, string title)
            => await _dialogService.ShowConfirmationDialogAsync(message, title);

        public void ShowError(string message, string title = "Error")
            => _dialogService.ShowError(message, title);

        public void ShowInfo(string message, string title = "Information")
            => _dialogService.ShowInfo(message, title);
    }

    /// <summary>
    /// Save health monitor that displays save status in the status bar
    /// </summary>
    public class SaveHealthMonitor : IDisposable
    {
        private readonly NoteNest.Core.Services.ISupervisedTaskRunner _taskRunner;
        private readonly NoteNest.Core.Services.IStatusBarService _statusBar;
        private readonly System.Windows.Threading.DispatcherTimer _updateTimer;
        private bool _disposed;
        
        public SaveHealthMonitor(NoteNest.Core.Services.ISupervisedTaskRunner taskRunner, 
                                NoteNest.Core.Services.IStatusBarService statusBar)
        {
            _taskRunner = taskRunner ?? throw new ArgumentNullException(nameof(taskRunner));
            _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
            
            // Update status bar every 2 seconds
            _updateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _updateTimer.Tick += UpdateHealthIndicator;
            _updateTimer.Start();
            
            // React immediately to failures
            _taskRunner.SaveFailed += OnSaveFailure;
        }
        
        private void UpdateHealthIndicator(object? sender, EventArgs e)
        {
            if (_disposed) return;
            
            try
            {
                var health = _taskRunner.GetSaveHealth();
                
                string indicator = "";
                string tooltip = "";
                
                if (!health.WALHealthy)
                {
                    indicator = "⚠️ No crash protection";
                    tooltip = $"Crash protection failed: {health.LastFailureReason}";
                    _statusBar.SetMessage(indicator, NoteNest.Core.Services.StatusType.Warning);
                }
                else if (!health.AutoSaveHealthy)
                {
                    indicator = "❌ Auto-save failing";
                    tooltip = $"Last save failed: {health.LastFailureReason}";
                    _statusBar.SetMessage(indicator, NoteNest.Core.Services.StatusType.Error);
                }
                else if (health.FailedSaves > 0)
                {
                    indicator = $"⚠️ {health.FailedSaves} save issues";
                    tooltip = "Some background saves failed. Your work is safe but check logs.";
                    _statusBar.SetMessage(indicator, NoteNest.Core.Services.StatusType.Warning);
                }
                else if (health.SuccessfulSaves > 0)
                {
                    // Only show positive status occasionally to avoid clutter
                    if (health.SuccessfulSaves % 10 == 0)
                    {
                        indicator = "✅ All saves working";
                        tooltip = $"{health.SuccessfulSaves} successful saves";
                        _statusBar.SetMessage(indicator, NoteNest.Core.Services.StatusType.Info);
                    }
                }
                
                _statusBar.SetSaveHealth(indicator, tooltip);
            }
            catch (Exception ex)
            {
                // Don't let health monitoring itself cause problems
                System.Diagnostics.Debug.WriteLine($"SaveHealthMonitor error: {ex.Message}");
            }
        }
        
        private void OnSaveFailure(object? sender, NoteNest.Core.Services.SaveFailureEventArgs e)
        {
            if (_disposed) return;
            
            try
            {
                // Immediate status bar update for critical failures
                if (e.Type == NoteNest.Core.Services.OperationType.AutoSave || 
                    e.Type == NoteNest.Core.Services.OperationType.WALWrite)
                {
                    _statusBar.SetMessage($"❌ {e.OperationName} failed", NoteNest.Core.Services.StatusType.Error);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveHealthMonitor.OnSaveFailure error: {ex.Message}");
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _updateTimer?.Stop();
            if (_taskRunner != null)
            {
                _taskRunner.SaveFailed -= OnSaveFailure;
            }
        }
    }
}
