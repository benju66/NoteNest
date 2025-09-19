using System;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Transaction
{
    /// <summary>
    /// Transaction step that creates a new SaveManager for the target storage location
    /// Validates the new location and ensures the SaveManager can be created successfully
    /// </summary>
    public class CreateSaveManagerStep : TransactionStepBase
    {
        private readonly string _dataPath;
        private readonly ISaveManagerFactory _saveManagerFactory;
        private ISaveManager _createdSaveManager;

        public override string Description => $"Create SaveManager for new location: {_dataPath}";
        public override bool CanRollback => true;

        public CreateSaveManagerStep(string dataPath, ISaveManagerFactory saveManagerFactory, IAppLogger logger) : base(logger)
        {
            _dataPath = dataPath ?? throw new ArgumentNullException(nameof(dataPath));
            _saveManagerFactory = saveManagerFactory ?? throw new ArgumentNullException(nameof(saveManagerFactory));
        }

        protected override async Task<TransactionStepResult> ExecuteStepAsync()
        {
            try
            {
                _logger.Info($"Creating new SaveManager for location: {_dataPath}");

                // Validate the path exists and is accessible
                if (!System.IO.Directory.Exists(_dataPath))
                {
                    return TransactionStepResult.Failed($"Target directory does not exist: {_dataPath}");
                }

                // Test write access to the directory
                await TestDirectoryAccessAsync(_dataPath);

                // Create the new SaveManager
                _createdSaveManager = await _saveManagerFactory.CreateSaveManagerAsync(_dataPath);

                if (_createdSaveManager == null)
                {
                    return TransactionStepResult.Failed("SaveManagerFactory returned null SaveManager");
                }

                // Test that the SaveManager is functional by performing a basic operation
                await TestSaveManagerFunctionalityAsync(_createdSaveManager);

                _logger.Info($"Successfully created and validated SaveManager for: {_dataPath}");
                return TransactionStepResult.Succeeded(_createdSaveManager);
            }
            catch (UnauthorizedAccessException ex)
            {
                return TransactionStepResult.Failed($"Access denied to directory {_dataPath}: {ex.Message}", ex);
            }
            catch (System.IO.DirectoryNotFoundException ex)
            {
                return TransactionStepResult.Failed($"Directory not found {_dataPath}: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                return TransactionStepResult.Failed($"Exception while creating SaveManager: {ex.Message}", ex);
            }
        }

        protected override async Task<TransactionStepResult> RollbackStepAsync()
        {
            try
            {
                if (_createdSaveManager != null)
                {
                    _logger.Debug($"Disposing created SaveManager during rollback");
                    _createdSaveManager.Dispose();
                    _createdSaveManager = null;
                }

                // Clean up any directory structures created during SaveManager creation
                await CleanupCreatedDirectoriesAsync(_dataPath);

                _logger.Debug("Successfully rolled back SaveManager creation");
                return TransactionStepResult.Succeeded();
            }
            catch (Exception ex)
            {
                return TransactionStepResult.Failed($"Exception during SaveManager creation rollback: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Test write access to the target directory
        /// </summary>
        private async Task TestDirectoryAccessAsync(string path)
        {
            var testFile = System.IO.Path.Combine(path, $"write_test_{Guid.NewGuid():N}.tmp");
            
            try
            {
                await System.IO.File.WriteAllTextAsync(testFile, "access test");
                System.IO.File.Delete(testFile);
            }
            catch (Exception ex)
            {
                throw new UnauthorizedAccessException($"Cannot write to directory {path}", ex);
            }
        }

        /// <summary>
        /// Test basic functionality of the created SaveManager
        /// </summary>
        private async Task TestSaveManagerFunctionalityAsync(ISaveManager saveManager)
        {
            try
            {
                // Test basic methods don't throw exceptions
                var dirtyNotes = saveManager.GetDirtyNoteIds();
                
                // Test that the SaveManager responds correctly to basic queries
                if (dirtyNotes == null)
                {
                    throw new InvalidOperationException("SaveManager returned null for GetDirtyNoteIds()");
                }

                _logger.Debug($"SaveManager functionality test passed - {dirtyNotes.Count} dirty notes found");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"SaveManager failed functionality test: {ex.Message}", ex);
            }
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Clean up any directories created during SaveManager instantiation
        /// </summary>
        private async Task CleanupCreatedDirectoriesAsync(string basePath)
        {
            try
            {
                // Clean up standard SaveManager subdirectories if they're empty
                var subDirs = new[] { ".temp", ".wal" };
                
                foreach (var subDir in subDirs)
                {
                    var fullPath = System.IO.Path.Combine(basePath, subDir);
                    if (System.IO.Directory.Exists(fullPath))
                    {
                        try
                        {
                            // Only delete if directory is empty
                            var files = System.IO.Directory.GetFiles(fullPath);
                            var dirs = System.IO.Directory.GetDirectories(fullPath);
                            
                            if (files.Length == 0 && dirs.Length == 0)
                            {
                                System.IO.Directory.Delete(fullPath);
                                _logger.Debug($"Cleaned up empty directory: {fullPath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning($"Failed to cleanup directory {fullPath}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error during directory cleanup: {ex.Message}");
            }
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Get the created SaveManager (for use by subsequent transaction steps)
        /// </summary>
        public ISaveManager GetCreatedSaveManager()
        {
            return _createdSaveManager;
        }
    }
}
