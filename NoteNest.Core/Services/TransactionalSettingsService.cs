using System;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Settings service that uses transactions for storage location changes
    /// Ensures safe storage location changes with full rollback capability
    /// </summary>
    public class TransactionalSettingsService : ITransactionalSettingsService
    {
        private readonly ConfigurationService _configService;
        private readonly IStorageTransactionManager _transactionManager;
        private readonly IAppLogger _logger;
        private readonly IServiceProvider _serviceProvider;

        public TransactionalSettingsService(
            ConfigurationService configService,
            IStorageTransactionManager transactionManager,
            IAppLogger logger,
            IServiceProvider serviceProvider)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>
        /// Apply settings changes with storage location transaction support
        /// </summary>
        public async Task<SettingsChangeResult> ApplySettingsAsync(
            AppSettings newSettings, 
            AppSettings originalSettings,
            IProgress<StorageTransactionProgress> progress = null)
        {
            try
            {
                _logger.Info("Applying settings changes with transaction support");

                // Check if storage location is changing
                var storageLocationChanged = IsStorageLocationChanged(originalSettings, newSettings);
                
                if (storageLocationChanged)
                {
                    _logger.Info("Storage location change detected - using transaction");
                    return await ApplyStorageLocationChangeAsync(newSettings, originalSettings, progress);
                }
                else
                {
                    _logger.Info("No storage location change - applying settings normally");
                    return await ApplyNormalSettingsChangeAsync(newSettings, originalSettings);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception while applying settings changes");
                return new SettingsChangeResult
                {
                    Success = false,
                    ErrorMessage = $"Settings change failed: {ex.Message}",
                    Exception = ex
                };
            }
        }

        /// <summary>
        /// Apply storage location change using the transaction manager
        /// </summary>
        private async Task<SettingsChangeResult> ApplyStorageLocationChangeAsync(
            AppSettings newSettings, 
            AppSettings originalSettings,
            IProgress<StorageTransactionProgress> progress)
        {
            try
            {
                // Calculate the new storage path
                var storageLocationService = new StorageLocationService();
                var newPath = CalculatePathFromSettings(newSettings, storageLocationService);
                
                if (string.IsNullOrEmpty(newPath))
                {
                    return new SettingsChangeResult
                    {
                        Success = false,
                        ErrorMessage = "Could not determine new storage path from settings"
                    };
                }

                // Update settings first (this will be part of the transaction)
                await _configService.UpdateSettingsAsync(newSettings);

                // Execute the storage location transaction
                var transactionResult = await _transactionManager.ChangeStorageLocationAsync(
                    newPath, 
                    newSettings.StorageMode, 
                    keepOriginalData: true, 
                    progress);

                if (transactionResult.Success)
                {
                    _logger.Info($"Storage location change transaction completed successfully: {newPath}");
                    
                    // Broadcast settings changed event
                    await BroadcastSettingsChangedAsync();
                    
                    return new SettingsChangeResult
                    {
                        Success = true,
                        NewStoragePath = newPath,
                        OldStoragePath = transactionResult.OldPath,
                        TransactionId = transactionResult.TransactionId,
                        DataMigrated = transactionResult.DataMigrated,
                        Duration = transactionResult.Duration
                    };
                }
                else
                {
                    _logger.Error($"Storage location change transaction failed: {transactionResult.ErrorMessage}");
                    
                    // Rollback settings changes
                    try
                    {
                        await _configService.UpdateSettingsAsync(originalSettings);
                        _logger.Info("Settings rolled back after failed storage transaction");
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger.Error(rollbackEx, "Failed to rollback settings after transaction failure");
                    }
                    
                    return new SettingsChangeResult
                    {
                        Success = false,
                        ErrorMessage = transactionResult.ErrorMessage,
                        Exception = transactionResult.Exception,
                        TransactionId = transactionResult.TransactionId,
                        FailedStep = transactionResult.FailedStep
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception during storage location change transaction");
                
                // Attempt to rollback settings
                try
                {
                    await _configService.UpdateSettingsAsync(originalSettings);
                }
                catch (Exception rollbackEx)
                {
                    _logger.Error(rollbackEx, "Failed to rollback settings after exception");
                }
                
                return new SettingsChangeResult
                {
                    Success = false,
                    ErrorMessage = $"Storage location change failed with exception: {ex.Message}",
                    Exception = ex
                };
            }
        }

        /// <summary>
        /// Apply normal settings changes (no storage location change)
        /// </summary>
        private async Task<SettingsChangeResult> ApplyNormalSettingsChangeAsync(
            AppSettings newSettings, 
            AppSettings originalSettings)
        {
            try
            {
                // Update settings normally
                await _configService.UpdateSettingsAsync(newSettings);
                
                // Update PathService (in case other paths changed)
                PathService.RootPath = newSettings.DefaultNotePath;
                
                // Broadcast settings changed event
                await BroadcastSettingsChangedAsync();
                
                _logger.Info("Normal settings changes applied successfully");
                
                return new SettingsChangeResult
                {
                    Success = true,
                    SettingsOnly = true
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to apply normal settings changes");
                
                // Attempt rollback
                try
                {
                    await _configService.UpdateSettingsAsync(originalSettings);
                }
                catch (Exception rollbackEx)
                {
                    _logger.Error(rollbackEx, "Failed to rollback settings after normal change failure");
                }
                
                return new SettingsChangeResult
                {
                    Success = false,
                    ErrorMessage = $"Settings change failed: {ex.Message}",
                    Exception = ex
                };
            }
        }

        /// <summary>
        /// Check if storage location is changing between old and new settings
        /// </summary>
        private bool IsStorageLocationChanged(AppSettings originalSettings, AppSettings newSettings)
        {
            if (originalSettings.StorageMode != newSettings.StorageMode)
                return true;

            var storageLocationService = new StorageLocationService();
            var oldPath = CalculatePathFromSettings(originalSettings, storageLocationService);
            var newPath = CalculatePathFromSettings(newSettings, storageLocationService);

            return !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Calculate storage path from settings
        /// </summary>
        private string CalculatePathFromSettings(AppSettings settings, StorageLocationService storageService)
        {
            switch (settings.StorageMode)
            {
                case StorageMode.OneDrive:
                    var oneDrive = storageService.GetOneDrivePath();
                    return !string.IsNullOrEmpty(oneDrive)
                        ? System.IO.Path.Combine(oneDrive, "NoteNest")
                        : storageService.ResolveNotesPath(StorageMode.Local);
                        
                case StorageMode.Custom:
                    return !string.IsNullOrEmpty(settings.CustomNotesPath)
                        ? settings.CustomNotesPath
                        : storageService.ResolveNotesPath(StorageMode.Local);
                        
                case StorageMode.Local:
                default:
                    return !string.IsNullOrEmpty(settings.DefaultNotePath)
                        ? settings.DefaultNotePath
                        : storageService.ResolveNotesPath(StorageMode.Local);
            }
        }

        /// <summary>
        /// Broadcast settings changed event to other services
        /// </summary>
        private async Task BroadcastSettingsChangedAsync()
        {
            try
            {
                // Use reflection to get the event bus from the service provider
                var eventBusType = Type.GetType("NoteNest.Core.Services.EventBus, NoteNest.Core");
                if (eventBusType != null)
                {
                    var getServiceMethod = _serviceProvider.GetType().GetMethod("GetService", new[] { typeof(Type) });
                    if (getServiceMethod != null)
                    {
                        var eventBus = getServiceMethod.Invoke(_serviceProvider, new object[] { eventBusType });
                        if (eventBus != null)
                        {
                            var eventType = Type.GetType("NoteNest.Core.Events.AppSettingsChangedEvent, NoteNest.Core");
                            if (eventType != null)
                            {
                                var eventInstance = Activator.CreateInstance(eventType);
                                var publishMethod = eventBusType.GetMethod("PublishAsync");
                                if (publishMethod != null)
                                {
                                    await (Task)publishMethod.Invoke(eventBus, new[] { eventInstance });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to broadcast settings changed event: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Interface for transactional settings service
    /// </summary>
    public interface ITransactionalSettingsService
    {
        Task<SettingsChangeResult> ApplySettingsAsync(
            AppSettings newSettings, 
            AppSettings originalSettings,
            IProgress<StorageTransactionProgress> progress = null);
    }

    /// <summary>
    /// Result of applying settings changes
    /// </summary>
    public class SettingsChangeResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public Exception Exception { get; set; }
        public string NewStoragePath { get; set; } = string.Empty;
        public string OldStoragePath { get; set; } = string.Empty;
        public string TransactionId { get; set; } = string.Empty;
        public string FailedStep { get; set; } = string.Empty;
        public bool DataMigrated { get; set; }
        public bool SettingsOnly { get; set; }
        public TimeSpan Duration { get; set; }
    }
}
