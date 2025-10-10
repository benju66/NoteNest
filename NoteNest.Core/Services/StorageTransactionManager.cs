using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Services.Transaction;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Manages storage location change transactions with full rollback support
    /// Orchestrates all steps required to safely change NoteNest's storage location
    /// </summary>
    public class StorageTransactionManager : IStorageTransactionManager
    {
        private readonly ISaveManagerFactory _saveManagerFactory;
        private readonly IValidationService _validationService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IAppLogger _logger;
        private readonly List<ITransactionStep> _executedSteps = new();

        public event EventHandler<StorageTransactionEventArgs> TransactionStarted;
        public event EventHandler<StorageTransactionEventArgs> TransactionCompleted;
        public event EventHandler<StorageTransactionProgressEventArgs> ProgressChanged;

        public StorageTransactionManager(
            ISaveManagerFactory saveManagerFactory,
            IValidationService validationService,
            IServiceProvider serviceProvider,
            IAppLogger logger)
        {
            _saveManagerFactory = saveManagerFactory ?? throw new ArgumentNullException(nameof(saveManagerFactory));
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Execute a complete storage location change transaction
        /// </summary>
        public async Task<StorageTransactionResult> ChangeStorageLocationAsync(
            string newPath, 
            StorageMode mode, 
            bool keepOriginalData = true,
            IProgress<StorageTransactionProgress>? progress = null)
        {
            var transactionId = Guid.NewGuid().ToString("N")[..8];
            var startTime = DateTime.UtcNow;
            
            try
            {
                _logger.Info($"Starting storage location transaction [{transactionId}]: {newPath} (mode: {mode}, keep original: {keepOriginalData})");
                
                // Fire transaction started event
                TransactionStarted?.Invoke(this, new StorageTransactionEventArgs
                {
                    TransactionId = transactionId,
                    NewPath = newPath,
                    StorageMode = mode,
                    StartTime = startTime
                });

                var result = await ExecuteTransactionStepsAsync(transactionId, newPath, mode, keepOriginalData, progress);
                
                // Fire transaction completed event
                TransactionCompleted?.Invoke(this, new StorageTransactionEventArgs
                {
                    TransactionId = transactionId,
                    NewPath = newPath,
                    StorageMode = mode,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    Success = result.Success,
                    ErrorMessage = result.ErrorMessage
                });
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Unexpected error in storage transaction [{transactionId}]");
                
                // Attempt rollback on unexpected errors
                await RollbackExecutedStepsAsync();
                
                return new StorageTransactionResult
                {
                    Success = false,
                    ErrorMessage = $"Transaction failed with unexpected error: {ex.Message}",
                    Exception = ex,
                    TransactionId = transactionId,
                    Duration = DateTime.UtcNow - startTime
                };
            }
        }

        /// <summary>
        /// Execute all transaction steps in sequence
        /// </summary>
        private async Task<StorageTransactionResult> ExecuteTransactionStepsAsync(
            string transactionId,
            string newPath, 
            StorageMode mode, 
            bool keepOriginalData,
            IProgress<StorageTransactionProgress> progress)
        {
            var currentSaveManager = _saveManagerFactory.Current;
            var originalPath = GetCurrentStoragePath(currentSaveManager);
            var startTime = DateTime.UtcNow;

            try
            {
                // Step 1: Validate new location
                ReportProgress(progress, 1, 8, "Validating new storage location", newPath);
                var validationStep = new ValidationStep(newPath, mode, _validationService, _logger);
                await ExecuteStepAsync(validationStep);

                // Step 2: Save all pending changes
                ReportProgress(progress, 2, 8, "Saving pending changes", "");
                var saveStep = new SaveAllDirtyStep(currentSaveManager, _logger);
                await ExecuteStepAsync(saveStep);

                // Step 3: Migrate data if needed (and different paths)
                var requiresMigration = RequiresMigration(originalPath, newPath);
                if (requiresMigration)
                {
                    ReportProgress(progress, 3, 8, "Migrating data to new location", $"{originalPath} → {newPath}");
                    var migrationProgress = new Progress<MigrationProgress>(mp => 
                    {
                        ReportProgress(progress, 3, 8, mp.CurrentOperation, mp.CurrentFile);
                    });
                    
                    var migrateStep = new MigrateDataStep(originalPath, newPath, keepOriginalData, _logger, migrationProgress);
                    await ExecuteStepAsync(migrateStep);
                }
                else
                {
                    _logger.Info("No data migration required - paths are the same");
                }

                // Step 4: Create new SaveManager for new location
                ReportProgress(progress, 4, 8, "Creating new SaveManager", newPath);
                var createSaveManagerStep = new CreateSaveManagerStep(newPath, _saveManagerFactory, _logger);
                await ExecuteStepAsync(createSaveManagerStep);
                var newSaveManager = createSaveManagerStep.GetCreatedSaveManager();

                // Step 5: Update all path-dependent services
                ReportProgress(progress, 5, 8, "Updating path-dependent services", "");
                var pathUpdateStep = new PathUpdateStep(newPath, _serviceProvider, _logger);
                await ExecuteStepAsync(pathUpdateStep);

                // Step 6: Replace SaveManager atomically
                ReportProgress(progress, 6, 8, "Replacing SaveManager", "");
                var replaceSaveManagerStep = new ReplaceSaveManagerStep(_saveManagerFactory, newSaveManager, _logger);
                await ExecuteStepAsync(replaceSaveManagerStep);

                // Step 7: Reload data from new location
                ReportProgress(progress, 7, 8, "Reloading data from new location", "");
                var reloadStep = new ReloadDataStep(_serviceProvider, _logger);
                await ExecuteStepAsync(reloadStep);

                // Step 8: Final validation
                ReportProgress(progress, 8, 8, "Validating transaction completion", "");
                var finalValidationStep = new FinalValidationStep(newSaveManager, newPath, _logger);
                await ExecuteStepAsync(finalValidationStep);

                var duration = DateTime.UtcNow - startTime;
                _logger.Info($"Storage location transaction [{transactionId}] completed successfully in {duration.TotalSeconds:F2} seconds");

                return new StorageTransactionResult
                {
                    Success = true,
                    NewPath = newPath,
                    OldPath = originalPath,
                    TransactionId = transactionId,
                    Duration = duration,
                    DataMigrated = requiresMigration,
                    StepResults = _executedSteps.ToDictionary(s => s.StepId, s => s.State.ToString())
                };
            }
            catch (TransactionStepException ex)
            {
                _logger.Error(ex, $"Transaction step failed [{transactionId}]: {ex.StepDescription}");
                await RollbackExecutedStepsAsync();
                
                return new StorageTransactionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Exception = ex,
                    FailedStep = ex.StepDescription,
                    TransactionId = transactionId,
                    Duration = DateTime.UtcNow - startTime
                };
            }
        }

        /// <summary>
        /// Execute a single transaction step with error handling
        /// </summary>
        private async Task ExecuteStepAsync(ITransactionStep step)
        {
            _executedSteps.Add(step);
            
            var result = await step.ExecuteAsync();
            
            if (!result.Success)
            {
                throw new TransactionStepException(step.Description, result.ErrorMessage, result.Exception);
            }
        }

        /// <summary>
        /// Rollback all executed steps in reverse order
        /// </summary>
        private async Task RollbackExecutedStepsAsync()
        {
            _logger.Warning($"Rolling back {_executedSteps.Count} executed transaction steps");
            
            var rollbackErrors = new List<string>();
            
            // Execute rollback in reverse order
            for (int i = _executedSteps.Count - 1; i >= 0; i--)
            {
                var step = _executedSteps[i];
                
                try
                {
                    if (step.CanRollback)
                    {
                        _logger.Debug($"Rolling back step: {step.Description} [{step.StepId}]");
                        var rollbackResult = await step.RollbackAsync();
                        
                        if (!rollbackResult.Success)
                        {
                            rollbackErrors.Add($"Step {step.StepId} ({step.Description}): {rollbackResult.ErrorMessage}");
                        }
                    }
                    else
                    {
                        _logger.Debug($"Skipping rollback for non-rollbackable step: {step.Description} [{step.StepId}]");
                    }
                }
                catch (Exception ex)
                {
                    rollbackErrors.Add($"Step {step.StepId} ({step.Description}): Exception during rollback - {ex.Message}");
                    _logger.Error(ex, $"Exception during rollback of step: {step.Description}");
                }
            }
            
            if (rollbackErrors.Count > 0)
            {
                _logger.Error($"Rollback completed with {rollbackErrors.Count} errors:\n{string.Join("\n", rollbackErrors)}");
            }
            else
            {
                _logger.Info("Transaction rollback completed successfully");
            }
        }

        /// <summary>
        /// Get the current storage path from the SaveManager
        /// </summary>
        private string GetCurrentStoragePath(ISaveManager saveManager)
        {
            // Use reflection to get the path from RTFIntegratedSaveEngine
            if (saveManager is RTFIntegratedSaveEngine rtfEngine)
            {
                var dataPathField = typeof(RTFIntegratedSaveEngine).GetField("_dataPath", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (dataPathField != null)
                {
                    return dataPathField.GetValue(rtfEngine) as string ?? string.Empty;
                }
            }
            
            return string.Empty;
        }

        /// <summary>
        /// Check if migration is required between two paths
        /// </summary>
        private bool RequiresMigration(string oldPath, string newPath)
        {
            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath))
                return false;
                
            try
            {
                return !string.Equals(
                    Path.GetFullPath(oldPath), 
                    Path.GetFullPath(newPath), 
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true; // If we can't compare paths, assume migration is needed
            }
        }

        /// <summary>
        /// Report progress to the caller
        /// </summary>
        private void ReportProgress(IProgress<StorageTransactionProgress> progress, int currentStep, int totalSteps, string operation, string details)
        {
            var progressInfo = new StorageTransactionProgress
            {
                CurrentStep = currentStep,
                TotalSteps = totalSteps,
                CurrentOperation = operation,
                Details = details,
                PercentComplete = (currentStep * 100) / totalSteps
            };
            
            progress?.Report(progressInfo);
            
            ProgressChanged?.Invoke(this, new StorageTransactionProgressEventArgs
            {
                Progress = progressInfo,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Exception thrown when a transaction step fails
    /// </summary>
    public class TransactionStepException : Exception
    {
        public string StepDescription { get; }
        
        public TransactionStepException(string stepDescription, string message, Exception? innerException = null) 
            : base(message, innerException)
        {
            StepDescription = stepDescription;
        }
    }
}
