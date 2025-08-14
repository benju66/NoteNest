using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace NoteNest.Core.Services
{
    public class MigrationService
    {
        public event EventHandler<MigrationProgressEventArgs> ProgressChanged;
        public event EventHandler<string> LogMessage;
        
        private string _backupPath;
        private static readonly string[] IgnoredFileNames = new[] { "desktop.ini", "Thumbs.db", ".DS_Store" };

        public async Task<MigrationPlan> AnalyzeAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
            {
                return new MigrationPlan();
            }

            return await Task.Run(() =>
            {
                var plan = new MigrationPlan();
                try
                {
                    var allFiles = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
                    foreach (var file in allFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var name = Path.GetFileName(file);
                        if (IgnoredFileNames.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                        {
                            plan.ExcludedFiles++;
                            continue;
                        }
                        plan.TotalFiles++;
                        try
                        {
                            var info = new FileInfo(file);
                            plan.TotalBytes += info.Length;
                        }
                        catch { }
                    }
                    plan.TotalDirectories = Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories).Length;
                }
                catch { }
                return plan;
            }, cancellationToken);
        }

        public async Task<bool> MigrateNotesAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Validating source directory...");
                
                if (!Directory.Exists(sourcePath))
                {
                    LogMessage?.Invoke(this, "ERROR: Source directory does not exist!");
                    return false;
                }

                LogMessage?.Invoke(this, "Preparing destination directory...");
                if (!Directory.Exists(destinationPath))
                {
                    Directory.CreateDirectory(destinationPath);
                    LogMessage?.Invoke(this, $"Created directory: {destinationPath}");
                }

                _backupPath = sourcePath + "_backup_" + DateTime.Now.ToString("yyyyMMddHHmmss");

                LogMessage?.Invoke(this, "Scanning for files and folders to migrate...");
                // Create all directories first (to preserve empty folders)
                var allDirectories = Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories).ToList();
                foreach (var dir in allDirectories)
                {
                    var relativeDir = Path.GetRelativePath(sourcePath, dir);
                    var destDirFull = Path.Combine(destinationPath, relativeDir);
                    if (!Directory.Exists(destDirFull))
                    {
                        Directory.CreateDirectory(destDirFull);
                    }
                }

                // Collect all files (copy everything except ignored noise files)
                var files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories)
                    .Where(f => !IgnoredFileNames.Any(x => string.Equals(Path.GetFileName(f), x, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (files.Count == 0)
                {
                    LogMessage?.Invoke(this, "No files found to migrate.");
                    return false;
                }

                LogMessage?.Invoke(this, $"Found {files.Count} files to migrate");

                for (int i = 0; i < files.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        LogMessage?.Invoke(this, "Migration cancelled by user");
                        await CleanupPartialMigration(destinationPath);
                        return false;
                    }
                    
                    var file = files[i];
                    var relativePath = Path.GetRelativePath(sourcePath, file);
                    var destFile = Path.Combine(destinationPath, relativePath);
                    
                    var destDir = Path.GetDirectoryName(destFile);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    
                    await Task.Run(() => File.Copy(file, destFile, overwrite: true), cancellationToken);
                    
                    var fileName = Path.GetFileName(file);
                    LogMessage?.Invoke(this, $"Copied: {fileName}");
                    
                    var progress = (int)((i + 1) * 100.0 / files.Count);
                    ProgressChanged?.Invoke(this, new MigrationProgressEventArgs 
                    { 
                        Progress = progress,
                        CurrentFile = fileName,
                        ProcessedFiles = i + 1,
                        TotalFiles = files.Count
                    });
                    
                    await Task.Delay(50, cancellationToken);
                }

                var categoriesFile = Path.Combine(destinationPath, ".metadata", "categories.json");
                if (File.Exists(categoriesFile))
                {
                    LogMessage?.Invoke(this, "Updating categories.json paths...");
                    await UpdateCategoriesJson(categoriesFile, sourcePath, destinationPath);
                }

                LogMessage?.Invoke(this, "✓ Migration completed successfully!");
                return true;
            }
            catch (OperationCanceledException)
            {
                LogMessage?.Invoke(this, "Migration cancelled by user");
                return false;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"ERROR: {ex.Message}");
                
                if (!string.IsNullOrEmpty(_backupPath) && Directory.Exists(_backupPath))
                {
                    await RollbackMigration(_backupPath, sourcePath);
                }
                
                return false;
            }
        }

        private async Task UpdateCategoriesJson(string categoriesFile, string oldPath, string newPath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(categoriesFile);
                json = json.Replace(oldPath.Replace("\\", "\\\\"), newPath.Replace("\\", "\\\\"));
                await File.WriteAllTextAsync(categoriesFile, json);
                LogMessage?.Invoke(this, "Updated category paths");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Warning: Could not update categories.json: {ex.Message}");
            }
        }

        private async Task CleanupPartialMigration(string path)
        {
            try
            {
                await Task.Run(() =>
                {
                    if (Directory.Exists(path))
                    {
                        var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                        if (files.Length == 0)
                        {
                            Directory.Delete(path, recursive: true);
                        }
                    }
                });
            }
            catch { }
        }

        private async Task RollbackMigration(string backupPath, string originalPath)
        {
            try
            {
                LogMessage?.Invoke(this, "Starting rollback...");
                
                if (Directory.Exists(backupPath))
                {
                    await Task.Run(() =>
                    {
                        if (Directory.Exists(originalPath))
                            Directory.Delete(originalPath, recursive: true);
                        
                        Directory.Move(backupPath, originalPath);
                    });
                    
                    LogMessage?.Invoke(this, "Rollback completed");
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Rollback failed: {ex.Message}");
            }
        }
    }

    public class MigrationProgressEventArgs : EventArgs
    {
        public int Progress { get; set; }
        public string CurrentFile { get; set; }
        public int ProcessedFiles { get; set; }
        public int TotalFiles { get; set; }
    }

    public class MigrationPlan
    {
        public int TotalFiles { get; set; }
        public int TotalDirectories { get; set; }
        public long TotalBytes { get; set; }
        public int ExcludedFiles { get; set; }
    }
}


