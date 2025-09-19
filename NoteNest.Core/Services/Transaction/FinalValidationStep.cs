using System;
using System.IO;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Transaction
{
    /// <summary>
    /// Final validation step to ensure the storage location change was successful
    /// Verifies the new SaveManager is working and data is accessible
    /// </summary>
    public class FinalValidationStep : TransactionStepBase
    {
        private readonly ISaveManager _newSaveManager;
        private readonly string _newPath;

        public override string Description => "Final validation of storage location change";
        public override bool CanRollback => false; // Final validation doesn't change anything

        public FinalValidationStep(
            ISaveManager newSaveManager,
            string newPath,
            IAppLogger logger) : base(logger)
        {
            _newSaveManager = newSaveManager ?? throw new ArgumentNullException(nameof(newSaveManager));
            _newPath = newPath ?? throw new ArgumentNullException(nameof(newPath));
        }

        protected override async Task<TransactionStepResult> ExecuteStepAsync()
        {
            try
            {
                _logger.Info($"Performing final validation of storage location change to: {_newPath}");

                var validationResults = new FinalValidationResults();

                // Validate SaveManager is functioning
                await ValidateSaveManagerAsync(validationResults);

                // Validate directory structure exists
                await ValidateDirectoryStructureAsync(validationResults);

                // Validate data files are accessible
                await ValidateDataAccessibilityAsync(validationResults);

                // Validate path services are updated correctly
                await ValidatePathServicesAsync(validationResults);

                // Check for any critical issues
                if (validationResults.HasCriticalIssues)
                {
                    var errorMsg = $"Final validation failed with {validationResults.CriticalIssues.Count} critical issues: " +
                                  string.Join(", ", validationResults.CriticalIssues);
                    return TransactionStepResult.Failed(errorMsg);
                }

                if (validationResults.HasWarnings)
                {
                    _logger.Warning($"Final validation completed with {validationResults.Warnings.Count} warnings: " +
                                   string.Join(", ", validationResults.Warnings));
                }

                _logger.Info("Final validation completed successfully - storage location change verified");
                return TransactionStepResult.Succeeded(validationResults);
            }
            catch (Exception ex)
            {
                return TransactionStepResult.Failed($"Exception during final validation: {ex.Message}", ex);
            }
        }

        protected override async Task<TransactionStepResult> RollbackStepAsync()
        {
            // Final validation doesn't change anything, so rollback is a no-op
            await Task.CompletedTask;
            return TransactionStepResult.Succeeded();
        }

        /// <summary>
        /// Validate the new SaveManager is functioning correctly
        /// </summary>
        private async Task ValidateSaveManagerAsync(FinalValidationResults results)
        {
            try
            {
                _logger.Debug("Validating SaveManager functionality");

                // Test basic SaveManager operations
                var dirtyNotes = _newSaveManager.GetDirtyNoteIds();
                if (dirtyNotes == null)
                {
                    results.CriticalIssues.Add("SaveManager.GetDirtyNoteIds() returned null");
                    return;
                }

                // Test that SaveManager can handle basic content operations
                var testNoteId = "validation_test_note";
                var testContent = "This is a validation test";
                
                try
                {
                    _newSaveManager.UpdateContent(testNoteId, testContent);
                    var retrievedContent = _newSaveManager.GetContent(testNoteId);
                    
                    if (retrievedContent != testContent)
                    {
                        results.CriticalIssues.Add("SaveManager content update/retrieval test failed");
                    }
                    
                    // Clean up test content
                    await _newSaveManager.CloseNoteAsync(testNoteId);
                }
                catch (Exception ex)
                {
                    results.CriticalIssues.Add($"SaveManager content operations failed: {ex.Message}");
                }

                results.SaveManagerValidated = true;
                _logger.Debug("SaveManager validation completed");
            }
            catch (Exception ex)
            {
                results.CriticalIssues.Add($"SaveManager validation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Validate the NoteNest directory structure exists at new location
        /// </summary>
        private async Task ValidateDirectoryStructureAsync(FinalValidationResults results)
        {
            try
            {
                _logger.Debug("Validating directory structure at new location");

                var requiredDirectories = new[]
                {
                    _newPath,
                    Path.Combine(_newPath, ".metadata"),
                    Path.Combine(_newPath, "Notes"),
                    Path.Combine(_newPath, ".temp"),
                    Path.Combine(_newPath, ".wal")
                };

                foreach (var directory in requiredDirectories)
                {
                    if (!Directory.Exists(directory))
                    {
                        results.CriticalIssues.Add($"Required directory missing: {directory}");
                    }
                    else
                    {
                        results.ValidatedDirectories++;
                    }
                }

                _logger.Debug($"Directory structure validation completed - {results.ValidatedDirectories} directories validated");
            }
            catch (Exception ex)
            {
                results.CriticalIssues.Add($"Directory structure validation failed: {ex.Message}");
            }
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Validate data files are accessible at new location
        /// </summary>
        private async Task ValidateDataAccessibilityAsync(FinalValidationResults results)
        {
            try
            {
                _logger.Debug("Validating data accessibility at new location");

                // Check for categories.json
                var categoriesPath = Path.Combine(_newPath, ".metadata", "categories.json");
                if (File.Exists(categoriesPath))
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(categoriesPath);
                        if (string.IsNullOrEmpty(content))
                        {
                            results.Warnings.Add("categories.json exists but is empty");
                        }
                        else
                        {
                            results.AccessibleDataFiles++;
                        }
                    }
                    catch (Exception ex)
                    {
                        results.Warnings.Add($"Cannot read categories.json: {ex.Message}");
                    }
                }
                else
                {
                    results.Warnings.Add("categories.json not found - this is normal for new installations");
                }

                // Check Notes directory accessibility
                var notesPath = Path.Combine(_newPath, "Notes");
                if (Directory.Exists(notesPath))
                {
                    try
                    {
                        var files = Directory.GetFiles(notesPath, "*.rtf", SearchOption.AllDirectories);
                        results.AccessibleNoteFiles = files.Length;
                        _logger.Debug($"Found {files.Length} RTF files in Notes directory");
                    }
                    catch (Exception ex)
                    {
                        results.Warnings.Add($"Cannot enumerate Notes directory: {ex.Message}");
                    }
                }

                _logger.Debug($"Data accessibility validation completed - {results.AccessibleDataFiles} data files, {results.AccessibleNoteFiles} note files");
            }
            catch (Exception ex)
            {
                results.Warnings.Add($"Data accessibility validation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Validate path services are pointing to the new location
        /// </summary>
        private async Task ValidatePathServicesAsync(FinalValidationResults results)
        {
            try
            {
                _logger.Debug("Validating path services configuration");

                // Check PathService static state
                var pathServiceRoot = PathService.RootPath;
                var expectedPath = Path.GetFullPath(_newPath);
                var actualPath = Path.GetFullPath(pathServiceRoot);

                if (!string.Equals(expectedPath, actualPath, StringComparison.OrdinalIgnoreCase))
                {
                    results.CriticalIssues.Add($"PathService.RootPath mismatch - Expected: {expectedPath}, Actual: {actualPath}");
                }
                else
                {
                    results.PathServicesValidated = true;
                }

                _logger.Debug("Path services validation completed");
            }
            catch (Exception ex)
            {
                results.CriticalIssues.Add($"Path services validation failed: {ex.Message}");
            }
            
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Results of final validation
    /// </summary>
    public class FinalValidationResults
    {
        public bool SaveManagerValidated { get; set; }
        public bool PathServicesValidated { get; set; }
        public int ValidatedDirectories { get; set; }
        public int AccessibleDataFiles { get; set; }
        public int AccessibleNoteFiles { get; set; }
        
        public System.Collections.Generic.List<string> CriticalIssues { get; set; } = new();
        public System.Collections.Generic.List<string> Warnings { get; set; } = new();
        
        public bool HasCriticalIssues => CriticalIssues.Count > 0;
        public bool HasWarnings => Warnings.Count > 0;
        public bool IsFullyValid => !HasCriticalIssues;
    }
}
