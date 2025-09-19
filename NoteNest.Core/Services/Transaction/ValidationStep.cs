using System;
using System.IO;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Transaction
{
    /// <summary>
    /// Transaction step that validates the new storage location is suitable for NoteNest
    /// Checks permissions, available space, and path validity
    /// </summary>
    public class ValidationStep : TransactionStepBase
    {
        private readonly string _newPath;
        private readonly StorageMode _storageMode;
        private readonly IValidationService _validationService;

        public override string Description => $"Validate storage location: {_newPath} (mode: {_storageMode})";
        public override bool CanRollback => false; // Validation doesn't change anything

        public ValidationStep(
            string newPath, 
            StorageMode storageMode, 
            IValidationService validationService,
            IAppLogger logger) : base(logger)
        {
            _newPath = newPath ?? throw new ArgumentNullException(nameof(newPath));
            _storageMode = storageMode;
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        }

        protected override async Task<TransactionStepResult> ExecuteStepAsync()
        {
            try
            {
                _logger.Info($"Validating storage location: {_newPath}");

                // Basic path validation
                if (string.IsNullOrWhiteSpace(_newPath))
                {
                    return TransactionStepResult.Failed("Storage path cannot be empty");
                }

                // Check if path is valid
                if (!IsValidPath(_newPath))
                {
                    return TransactionStepResult.Failed($"Invalid path format: {_newPath}");
                }

                // Use validation service if available
                if (_validationService != null)
                {
                    var validationResult = await _validationService.ValidateStorageLocationAsync(_newPath, _storageMode);
                    if (!validationResult.IsValid)
                    {
                        var errorMsg = $"Validation failed: {string.Join(", ", validationResult.Errors)}";
                        return TransactionStepResult.Failed(errorMsg);
                    }
                }
                else
                {
                    // Fallback validation when service is not available
                    var fallbackResult = await PerformFallbackValidationAsync(_newPath);
                    if (!fallbackResult.Success)
                    {
                        return fallbackResult;
                    }
                }

                // Check available disk space
                var spaceCheckResult = await CheckAvailableSpaceAsync(_newPath);
                if (!spaceCheckResult.Success)
                {
                    return spaceCheckResult;
                }

                _logger.Info($"Storage location validation successful: {_newPath}");
                return TransactionStepResult.Succeeded(new { ValidatedPath = _newPath, StorageMode = _storageMode });
            }
            catch (Exception ex)
            {
                return TransactionStepResult.Failed($"Exception during storage location validation: {ex.Message}", ex);
            }
        }

        protected override async Task<TransactionStepResult> RollbackStepAsync()
        {
            // Validation doesn't change anything, so rollback is a no-op
            await Task.CompletedTask;
            return TransactionStepResult.Succeeded();
        }

        /// <summary>
        /// Check if the path format is valid
        /// </summary>
        private bool IsValidPath(string path)
        {
            try
            {
                // Try to get full path - this will throw if path is invalid
                Path.GetFullPath(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Perform fallback validation when validation service is not available
        /// </summary>
        private async Task<TransactionStepResult> PerformFallbackValidationAsync(string path)
        {
            try
            {
                // Check if path exists or can be created
                if (!Directory.Exists(path))
                {
                    try
                    {
                        Directory.CreateDirectory(path);
                    }
                    catch (Exception ex)
                    {
                        return TransactionStepResult.Failed($"Cannot create directory {path}: {ex.Message}", ex);
                    }
                }

                // Test write permissions
                var testFile = Path.Combine(path, $"write_test_{Guid.NewGuid():N}.tmp");
                try
                {
                    await File.WriteAllTextAsync(testFile, "write test");
                    File.Delete(testFile);
                }
                catch (Exception ex)
                {
                    return TransactionStepResult.Failed($"No write permission to {path}: {ex.Message}", ex);
                }

                // Check for reserved paths (basic check)
                var fullPath = Path.GetFullPath(path).ToLowerInvariant();
                var systemPaths = new[] { "windows", "program files", "system32" };
                
                foreach (var systemPath in systemPaths)
                {
                    if (fullPath.Contains(systemPath))
                    {
                        _logger.Warning($"Storage path is in system directory: {path}");
                        // Don't fail, but warn
                    }
                }

                return TransactionStepResult.Succeeded();
            }
            catch (Exception ex)
            {
                return TransactionStepResult.Failed($"Fallback validation failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Check available disk space at the target location
        /// </summary>
        private async Task<TransactionStepResult> CheckAvailableSpaceAsync(string path)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(path));
                var availableSpace = drive.AvailableFreeSpace;
                var requiredSpace = 100 * 1024 * 1024; // 100 MB minimum

                if (availableSpace < requiredSpace)
                {
                    return TransactionStepResult.Failed(
                        $"Insufficient disk space. Available: {availableSpace / 1024 / 1024} MB, Required: {requiredSpace / 1024 / 1024} MB");
                }

                _logger.Debug($"Available disk space: {availableSpace / 1024 / 1024} MB");
                await Task.CompletedTask;
                return TransactionStepResult.Succeeded();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Could not check disk space: {ex.Message}");
                // Don't fail the transaction for disk space check failure
                return TransactionStepResult.Succeeded();
            }
        }
    }
}
