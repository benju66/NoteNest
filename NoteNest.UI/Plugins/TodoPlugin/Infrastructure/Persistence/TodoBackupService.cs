using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence
{
    /// <summary>
    /// Backup service for the todo database.
    /// Provides automatic and manual backup capabilities.
    /// </summary>
    public interface ITodoBackupService
    {
        Task<bool> BackupAsync();
        Task<bool> RestoreAsync(string backupPath);
        Task<int> CleanOldBackupsAsync(int daysToKeep = 7);
        Task<string[]> GetAvailableBackupsAsync();
    }
    
    public class TodoBackupService : ITodoBackupService
    {
        private readonly string _databasePath;
        private readonly string _backupDirectory;
        private readonly IAppLogger _logger;
        
        public TodoBackupService(string connectionString, IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Extract database path from connection string
            _databasePath = new SqliteConnectionStringBuilder(connectionString).DataSource;
            
            // Backup directory is sibling to database file
            _backupDirectory = Path.Combine(
                Path.GetDirectoryName(_databasePath) ?? "",
                "backups");
                
            // Ensure backup directory exists
            Directory.CreateDirectory(_backupDirectory);
        }
        
        public async Task<bool> BackupAsync()
        {
            try
            {
                if (!File.Exists(_databasePath))
                {
                    _logger.Warning("[TodoBackup] Database file does not exist, skipping backup");
                    return false;
                }
                
                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var backupPath = Path.Combine(_backupDirectory, $"todos-{timestamp}.db");
                
                _logger.Info($"[TodoBackup] Creating backup: {backupPath}");
                
                // SQLite online backup
                await Task.Run(() =>
                {
                    using var sourceConnection = new SqliteConnection($"Data Source={_databasePath}");
                    using var destConnection = new SqliteConnection($"Data Source={backupPath}");
                    
                    sourceConnection.Open();
                    destConnection.Open();
                    
                    sourceConnection.BackupDatabase(destConnection);
                });
                
                _logger.Info($"[TodoBackup] Backup completed successfully: {new FileInfo(backupPath).Length} bytes");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoBackup] Failed to create backup");
                return false;
            }
        }
        
        public async Task<bool> RestoreAsync(string backupPath)
        {
            try
            {
                if (!File.Exists(backupPath))
                {
                    _logger.Error($"[TodoBackup] Backup file not found: {backupPath}");
                    return false;
                }
                
                _logger.Info($"[TodoBackup] Restoring from backup: {backupPath}");
                
                // Create a safety backup of current database first
                if (File.Exists(_databasePath))
                {
                    var safetyBackup = _databasePath + ".before-restore";
                    File.Copy(_databasePath, safetyBackup, overwrite: true);
                    _logger.Info($"[TodoBackup] Created safety backup: {safetyBackup}");
                }
                
                // Restore the backup
                await Task.Run(() =>
                {
                    File.Copy(backupPath, _databasePath, overwrite: true);
                });
                
                _logger.Info("[TodoBackup] Database restored successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoBackup] Failed to restore from backup: {backupPath}");
                return false;
            }
        }
        
        public async Task<int> CleanOldBackupsAsync(int daysToKeep = 7)
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                var backups = Directory.GetFiles(_backupDirectory, "todos-*.db");
                var deletedCount = 0;
                
                foreach (var backup in backups)
                {
                    var fileInfo = new FileInfo(backup);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        await Task.Run(() => File.Delete(backup));
                        deletedCount++;
                        _logger.Debug($"[TodoBackup] Deleted old backup: {Path.GetFileName(backup)}");
                    }
                }
                
                if (deletedCount > 0)
                {
                    _logger.Info($"[TodoBackup] Cleaned {deletedCount} old backup(s)");
                }
                
                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoBackup] Failed to clean old backups");
                return 0;
            }
        }
        
        public Task<string[]> GetAvailableBackupsAsync()
        {
            try
            {
                var backups = Directory.GetFiles(_backupDirectory, "todos-*.db")
                    .OrderByDescending(f => new FileInfo(f).CreationTime)
                    .ToArray();
                
                return Task.FromResult(backups);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoBackup] Failed to get available backups");
                return Task.FromResult(Array.Empty<string>());
            }
        }
    }
}

