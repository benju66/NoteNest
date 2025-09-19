using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Transaction
{
    /// <summary>
    /// Transaction step that migrates data from old storage location to new location
    /// Handles copying/moving all NoteNest data folders with progress tracking
    /// </summary>
    public class MigrateDataStep : TransactionStepBase
    {
        private readonly string _sourcePath;
        private readonly string _destinationPath;
        private readonly bool _keepOriginal;
        private readonly IProgress<MigrationProgress> _progressReporter;
        private readonly List<string> _copiedFiles = new();
        private readonly List<string> _createdDirectories = new();

        public override string Description => $"Migrate data from {_sourcePath} to {_destinationPath} (keep original: {_keepOriginal})";
        public override bool CanRollback => true;

        public MigrateDataStep(
            string sourcePath, 
            string destinationPath, 
            bool keepOriginal,
            IAppLogger logger,
            IProgress<MigrationProgress> progressReporter = null) : base(logger)
        {
            _sourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
            _destinationPath = destinationPath ?? throw new ArgumentNullException(nameof(destinationPath));
            _keepOriginal = keepOriginal;
            _progressReporter = progressReporter;
        }

        protected override async Task<TransactionStepResult> ExecuteStepAsync()
        {
            try
            {
                if (!Directory.Exists(_sourcePath))
                {
                    return TransactionStepResult.Failed($"Source directory does not exist: {_sourcePath}");
                }

                if (string.Equals(Path.GetFullPath(_sourcePath), Path.GetFullPath(_destinationPath), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Info("Source and destination paths are the same - skipping migration");
                    return TransactionStepResult.Succeeded(new { SkipReason = "Same source and destination" });
                }

                _logger.Info($"Starting data migration from {_sourcePath} to {_destinationPath}");

                // Calculate total work for progress reporting
                var totalSize = await CalculateTotalSizeAsync(_sourcePath);
                var processedSize = 0L;

                // Define the folders to migrate
                var foldersToMigrate = new[]
                {
                    ".metadata",
                    "Notes", 
                    "Attachments",
                    "Plugins",
                    "Templates"
                };

                var migratedFolderCount = 0;
                var migratedFileCount = 0;

                foreach (var folder in foldersToMigrate)
                {
                    var sourceFolderPath = Path.Combine(_sourcePath, folder);
                    var destFolderPath = Path.Combine(_destinationPath, folder);

                    if (Directory.Exists(sourceFolderPath))
                    {
                        _logger.Debug($"Migrating folder: {folder}");
                        
                        var result = await MigrateFolderAsync(sourceFolderPath, destFolderPath, 
                            new Progress<MigrationProgress>(p =>
                            {
                                processedSize += p.BytesProcessed;
                                _progressReporter?.Report(new MigrationProgress
                                {
                                    CurrentOperation = $"Migrating {folder}: {p.CurrentFile}",
                                    FilesProcessed = p.FilesProcessed,
                                    TotalFiles = p.TotalFiles,
                                    BytesProcessed = processedSize,
                                    TotalBytes = totalSize,
                                    CurrentFolder = folder
                                });
                            }));

                        migratedFolderCount++;
                        migratedFileCount += result.FilesCopied;
                    }
                    else
                    {
                        _logger.Debug($"Folder {folder} does not exist in source - skipping");
                    }
                }

                // Migrate any loose files in the root (shouldn't be many)
                await MigrateRootFilesAsync(_sourcePath, _destinationPath);

                _logger.Info($"Data migration completed: {migratedFolderCount} folders, {migratedFileCount} files");
                
                return TransactionStepResult.Succeeded(new 
                { 
                    MigratedFolders = migratedFolderCount,
                    MigratedFiles = migratedFileCount,
                    TotalBytes = totalSize
                });
            }
            catch (Exception ex)
            {
                return TransactionStepResult.Failed($"Exception during data migration: {ex.Message}", ex);
            }
        }

        protected override async Task<TransactionStepResult> RollbackStepAsync()
        {
            try
            {
                _logger.Info($"Rolling back data migration - cleaning up {_copiedFiles.Count} files and {_createdDirectories.Count} directories");

                var cleanupErrors = new List<string>();

                // Delete copied files
                foreach (var filePath in _copiedFiles.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        cleanupErrors.Add($"Failed to delete file {filePath}: {ex.Message}");
                    }
                }

                // Delete created directories (in reverse order)
                foreach (var dirPath in _createdDirectories.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (Directory.Exists(dirPath))
                        {
                            // Only delete if directory is empty
                            if (!Directory.EnumerateFileSystemEntries(dirPath).Any())
                            {
                                Directory.Delete(dirPath);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        cleanupErrors.Add($"Failed to delete directory {dirPath}: {ex.Message}");
                    }
                }

                if (cleanupErrors.Count > 0)
                {
                    var errorMsg = $"Rollback completed with {cleanupErrors.Count} cleanup errors:\n" + 
                                  string.Join("\n", cleanupErrors);
                    _logger.Warning(errorMsg);
                    return TransactionStepResult.Failed(errorMsg);
                }

                _logger.Info("Data migration rollback completed successfully");
                return TransactionStepResult.Succeeded(new { 
                    CleanedUpFiles = _copiedFiles.Count,
                    CleanedUpDirectories = _createdDirectories.Count 
                });
            }
            catch (Exception ex)
            {
                return TransactionStepResult.Failed($"Exception during data migration rollback: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Migrate a single folder from source to destination
        /// </summary>
        private async Task<FolderMigrationResult> MigrateFolderAsync(string sourceFolder, string destFolder, IProgress<MigrationProgress> progress)
        {
            var result = new FolderMigrationResult();
            
            // Create destination directory
            Directory.CreateDirectory(destFolder);
            _createdDirectories.Add(destFolder);

            // Get all files in the source folder (including subdirectories)
            var files = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories);
            result.TotalFiles = files.Length;

            for (int i = 0; i < files.Length; i++)
            {
                var sourceFile = files[i];
                var relativePath = Path.GetRelativePath(sourceFolder, sourceFile);
                var destFile = Path.Combine(destFolder, relativePath);

                // Create destination directory for the file
                var destDir = Path.GetDirectoryName(destFile);
                if (!Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                    _createdDirectories.Add(destDir);
                }

                // Copy the file
                File.Copy(sourceFile, destFile, overwrite: true);
                _copiedFiles.Add(destFile);
                result.FilesCopied++;

                // Report progress
                progress?.Report(new MigrationProgress
                {
                    CurrentFile = Path.GetFileName(sourceFile),
                    FilesProcessed = i + 1,
                    TotalFiles = files.Length,
                    BytesProcessed = new FileInfo(sourceFile).Length
                });

                // Small delay to allow for cancellation and prevent UI freezing
                if (i % 10 == 0)
                {
                    await Task.Delay(1);
                }
            }

            return result;
        }

        /// <summary>
        /// Migrate loose files in the root directory
        /// </summary>
        private async Task MigrateRootFilesAsync(string sourceRoot, string destRoot)
        {
            var rootFiles = Directory.GetFiles(sourceRoot, "*", SearchOption.TopDirectoryOnly);
            
            foreach (var sourceFile in rootFiles)
            {
                var fileName = Path.GetFileName(sourceFile);
                var destFile = Path.Combine(destRoot, fileName);
                
                File.Copy(sourceFile, destFile, overwrite: true);
                _copiedFiles.Add(destFile);
            }
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Calculate total size of data to migrate for progress reporting
        /// </summary>
        private async Task<long> CalculateTotalSizeAsync(string sourcePath)
        {
            try
            {
                long totalSize = 0;
                var files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
                
                foreach (var file in files)
                {
                    try
                    {
                        totalSize += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Skip files we can't access
                    }
                }
                
                await Task.CompletedTask;
                return totalSize;
            }
            catch
            {
                return 0; // If we can't calculate size, just return 0
            }
        }
    }

    /// <summary>
    /// Progress information for data migration
    /// </summary>
    public class MigrationProgress
    {
        public string CurrentOperation { get; set; } = string.Empty;
        public string CurrentFile { get; set; } = string.Empty;
        public string CurrentFolder { get; set; } = string.Empty;
        public int FilesProcessed { get; set; }
        public int TotalFiles { get; set; }
        public long BytesProcessed { get; set; }
        public long TotalBytes { get; set; }
        
        public int PercentComplete => TotalBytes > 0 ? (int)((BytesProcessed * 100) / TotalBytes) : 0;
    }

    /// <summary>
    /// Result of migrating a single folder
    /// </summary>
    internal class FolderMigrationResult
    {
        public int FilesCopied { get; set; }
        public int TotalFiles { get; set; }
    }
}
