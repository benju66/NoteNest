using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.Sqlite;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Trees;

namespace NoteNest.Infrastructure.Database
{
    /// <summary>
    /// Comprehensive backup and recovery service with multi-tier backup strategy.
    /// Provides automatic backups, integrity checking, and auto-recovery capabilities.
    /// </summary>
    public interface IDatabaseBackupService
    {
        Task<bool> CreateBackupAsync(BackupType type);
        Task<bool> RestoreFromBackupAsync(string backupPath);
        Task<bool> VerifyIntegrityAsync();
        Task<bool> AutoRecoverAsync();
        Task<BackupStatus> GetBackupStatusAsync();
        Task<List<BackupInfo>> GetAvailableBackupsAsync();
        Task<bool> ExportToJsonAsync(string exportPath = null);
    }
    
    public class DatabaseBackupService : IDatabaseBackupService, IHostedService
    {
        private readonly string _connectionString;
        private readonly ITreeDatabaseRepository _repository;
        private readonly IAppLogger _logger;
        private Timer _dailyBackupTimer;
        private Timer _integrityCheckTimer;
        private readonly string _backupPath;
        private readonly string _shadowPath;
        private readonly string _exportPath;
        
        public DatabaseBackupService(
            string connectionString,
            ITreeDatabaseRepository repository,
            IAppLogger logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Use LOCAL AppData to avoid cloud sync corruption
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var baseBackupPath = Path.Combine(localAppData, "NoteNest", "backups");
            
            _backupPath = Path.Combine(baseBackupPath, "daily");
            _shadowPath = Path.Combine(baseBackupPath, "shadow", "tree_shadow.db");
            _exportPath = Path.Combine(baseBackupPath, "exports");
            
            // Ensure backup directories exist
            Directory.CreateDirectory(_backupPath);
            Directory.CreateDirectory(Path.GetDirectoryName(_shadowPath));
            Directory.CreateDirectory(_exportPath);
            
            _logger.Info($"DatabaseBackupService initialized with backup path: {baseBackupPath}");
        }

        // =============================================================================
        // HOSTED SERVICE IMPLEMENTATION
        // =============================================================================
        
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting database backup service...");
            
            try
            {
                // Initial integrity check
                if (!await VerifyIntegrityAsync())
                {
                    _logger.Warning("Database integrity issue detected on startup");
                    await AutoRecoverAsync();
                }
                
                // Create initial shadow backup
                await CreateBackupAsync(BackupType.Shadow);
                
                // Schedule daily backups at 3 AM
                var timeUntil3AM = CalculateTimeUntilNext3AM();
                _dailyBackupTimer = new Timer(
                    async _ => await CreateBackupAsync(BackupType.Daily),
                    null,
                    timeUntil3AM,
                    TimeSpan.FromDays(1));
                
                // Schedule integrity checks every hour
                _integrityCheckTimer = new Timer(
                    async _ => await PerformScheduledIntegrityCheck(),
                    null,
                    TimeSpan.FromMinutes(5), // First check in 5 minutes
                    TimeSpan.FromHours(1));  // Then every hour
                
                _logger.Info("Database backup service started successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to start database backup service");
                throw;
            }
        }
        
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Stopping database backup service...");
            
            try
            {
                // Stop timers
                _dailyBackupTimer?.Dispose();
                _integrityCheckTimer?.Dispose();
                
                // Create final shadow backup before shutdown
                await CreateBackupAsync(BackupType.Shadow);
                
                _logger.Info("Database backup service stopped");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error during backup service shutdown: {ex.Message}");
            }
        }

        // =============================================================================
        // BACKUP OPERATIONS
        // =============================================================================
        
        public async Task<bool> CreateBackupAsync(BackupType type)
        {
            try
            {
                _logger.Debug($"Creating {type} backup...");
                
                using var sourceConnection = new SqliteConnection(_connectionString);
                await sourceConnection.OpenAsync();
                
                // Force WAL checkpoint to ensure consistency
                await sourceConnection.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE)");
                
                string backupFilePath = type switch
                {
                    BackupType.Shadow => _shadowPath,
                    BackupType.Daily => Path.Combine(_backupPath, $"tree_{DateTime.Now:yyyyMMdd_HHmmss}.db"),
                    BackupType.Manual => Path.Combine(_backupPath, "..", $"manual_{DateTime.Now:yyyyMMdd_HHmmss}.db"),
                    BackupType.Export => await ExportToJsonInternalAsync(),
                    _ => throw new ArgumentException($"Unknown backup type: {type}")
                };
                
                if (type != BackupType.Export)
                {
                // Simple file copy for backup (SQLite backup API requires additional setup)
                var dbPath = new SqliteConnectionStringBuilder(_connectionString).DataSource;
                File.Copy(dbPath, backupFilePath, overwrite: true);
                    
                    // Verify backup was created successfully
                    if (!File.Exists(backupFilePath))
                    {
                        throw new InvalidOperationException("Backup file was not created");
                    }
                    
                    // Quick integrity check on backup
                    using var verifyConnection = new SqliteConnection($"Data Source={backupFilePath}");
                    await verifyConnection.OpenAsync();
                    var integrity = await verifyConnection.ExecuteScalarAsync<string>("PRAGMA integrity_check");
                    
                    if (integrity != "ok")
                    {
                        File.Delete(backupFilePath);
                        throw new InvalidOperationException($"Backup failed integrity check: {integrity}");
                    }
                }
                
                // Clean old backups based on type
                if (type == BackupType.Daily)
                {
                    await CleanOldBackupsAsync(_backupPath, keepCount: 7);
                }
                
                var fileInfo = new FileInfo(backupFilePath);
                _logger.Info($"{type} backup created successfully: {Path.GetFileName(backupFilePath)} ({fileInfo.Length / 1024:F1} KB)");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create {type} backup");
                return false;
            }
        }
        
        public async Task<bool> RestoreFromBackupAsync(string backupPath)
        {
            try
            {
                if (!File.Exists(backupPath))
                {
                    _logger.Error($"Backup file not found: {backupPath}");
                    return false;
                }
                
                _logger.Info($"Starting restore from backup: {Path.GetFileName(backupPath)}");
                
                // Verify backup integrity before restore
                using var backupConnection = new SqliteConnection($"Data Source={backupPath}");
                await backupConnection.OpenAsync();
                var integrity = await backupConnection.ExecuteScalarAsync<string>("PRAGMA integrity_check");
                
                if (integrity != "ok")
                {
                    _logger.Error($"Backup file is corrupted: {backupPath}");
                    return false;
                }
                
                // Create safety backup of current database
                await CreateBackupAsync(BackupType.Manual);
                
                // Close all connections to current database
                SqliteConnection.ClearAllPools();
                
                // Replace current database with backup
                var dbPath = new SqliteConnectionStringBuilder(_connectionString).DataSource;
                var tempPath = dbPath + ".temp";
                
                // Copy backup to temp location first
                File.Copy(backupPath, tempPath, overwrite: true);
                
                // Atomic replace
                if (File.Exists(dbPath))
                    File.Delete(dbPath);
                File.Move(tempPath, dbPath);
                
                _logger.Info($"Database restored successfully from: {Path.GetFileName(backupPath)}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to restore from backup: {backupPath}");
                return false;
            }
        }
        
        public async Task<bool> VerifyIntegrityAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var result = await connection.ExecuteScalarAsync<string>("PRAGMA integrity_check");
                var isValid = result == "ok";
                
                if (!isValid)
                {
                    _logger.Warning($"Database integrity check failed: {result}");
                }
                else
                {
                    _logger.Debug("Database integrity check passed");
                }
                
                return isValid;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Database integrity check failed with exception");
                return false;
            }
        }
        
        public async Task<bool> AutoRecoverAsync()
        {
            try
            {
                _logger.Warning("Starting automatic database recovery...");
                
                // Step 1: Check if database is accessible
                if (await VerifyIntegrityAsync())
                {
                    _logger.Info("Database is healthy, no recovery needed");
                    return true;
                }
                
                // Step 2: Try shadow backup
                if (File.Exists(_shadowPath))
                {
                    _logger.Info("Attempting recovery from shadow backup...");
                    if (await RestoreFromBackupAsync(_shadowPath))
                    {
                        _logger.Info("Successfully recovered from shadow backup");
                        return true;
                    }
                }
                
                // Step 3: Try latest daily backup
                var latestDailyBackup = GetLatestBackupFile(_backupPath);
                if (latestDailyBackup != null)
                {
                    _logger.Info($"Attempting recovery from daily backup: {Path.GetFileName(latestDailyBackup)}");
                    if (await RestoreFromBackupAsync(latestDailyBackup))
                    {
                        _logger.Info("Successfully recovered from daily backup");
                        return true;
                    }
                }
                
                // Step 4: Last resort - rebuild from file system
                _logger.Warning("No valid backups found, rebuilding from file system...");
                
                var dbPath = new SqliteConnectionStringBuilder(_connectionString).DataSource;
                
                // Delete corrupted database
                if (File.Exists(dbPath))
                    File.Delete(dbPath);
                
                // Initialize fresh database
                var initializer = new TreeDatabaseInitializer(_connectionString, _logger);
                if (!await initializer.InitializeAsync())
                {
                    _logger.Error("Failed to initialize database during recovery");
                    return false;
                }
                
                // Rebuild from file system
                var notesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteNest");
                if (Directory.Exists(notesPath))
                {
                    await _repository.RebuildFromFileSystemAsync(notesPath);
                    _logger.Info("Database rebuilt from file system successfully");
                }
                
                // Create new shadow backup
                await CreateBackupAsync(BackupType.Shadow);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Auto-recovery failed");
                return false;
            }
        }

        // =============================================================================
        // BACKUP MANAGEMENT
        // =============================================================================
        
        public async Task<BackupStatus> GetBackupStatusAsync()
        {
            try
            {
                var status = new BackupStatus
                {
                    CheckedAt = DateTime.UtcNow,
                    IsHealthy = await VerifyIntegrityAsync()
                };
                
                // Shadow backup info
                if (File.Exists(_shadowPath))
                {
                    var shadowInfo = new FileInfo(_shadowPath);
                    status.ShadowBackup = new BackupInfo
                    {
                        FilePath = _shadowPath,
                        CreatedAt = shadowInfo.CreationTimeUtc,
                        SizeBytes = shadowInfo.Length,
                        Type = BackupType.Shadow
                    };
                }
                
                // Daily backups info
                var dailyBackups = Directory.GetFiles(_backupPath, "*.db")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .Take(7)
                    .Select(f => new BackupInfo
                    {
                        FilePath = f.FullName,
                        CreatedAt = f.CreationTimeUtc,
                        SizeBytes = f.Length,
                        Type = BackupType.Daily
                    })
                    .ToList();
                
                status.DailyBackups = dailyBackups;
                status.LatestBackup = dailyBackups.FirstOrDefault()?.CreatedAt ?? status.ShadowBackup?.CreatedAt;
                
                return status;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get backup status");
                return new BackupStatus
                {
                    CheckedAt = DateTime.UtcNow,
                    IsHealthy = false,
                    Error = ex.Message
                };
            }
        }
        
        public async Task<List<BackupInfo>> GetAvailableBackupsAsync()
        {
            try
            {
                var backups = new List<BackupInfo>();
                
                // Add shadow backup
                if (File.Exists(_shadowPath))
                {
                    var shadowInfo = new FileInfo(_shadowPath);
                    backups.Add(new BackupInfo
                    {
                        FilePath = _shadowPath,
                        CreatedAt = shadowInfo.CreationTimeUtc,
                        SizeBytes = shadowInfo.Length,
                        Type = BackupType.Shadow,
                        DisplayName = "Shadow Backup (Real-time)"
                    });
                }
                
                // Add daily backups
                var dailyBackupFiles = Directory.GetFiles(_backupPath, "*.db")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTimeUtc);
                
                foreach (var file in dailyBackupFiles)
                {
                    backups.Add(new BackupInfo
                    {
                        FilePath = file.FullName,
                        CreatedAt = file.CreationTimeUtc,
                        SizeBytes = file.Length,
                        Type = BackupType.Daily,
                        DisplayName = $"Daily Backup - {file.CreationTimeUtc:MMM dd, yyyy HH:mm}"
                    });
                }
                
                // Add manual backups
                var parentDir = Path.GetDirectoryName(_backupPath);
                var manualBackups = Directory.GetFiles(parentDir, "manual_*.db")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTimeUtc);
                
                foreach (var file in manualBackups)
                {
                    backups.Add(new BackupInfo
                    {
                        FilePath = file.FullName,
                        CreatedAt = file.CreationTimeUtc,
                        SizeBytes = file.Length,
                        Type = BackupType.Manual,
                        DisplayName = $"Manual Backup - {file.CreationTimeUtc:MMM dd, yyyy HH:mm}"
                    });
                }
                
                return backups.OrderByDescending(b => b.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get available backups");
                return new List<BackupInfo>();
            }
        }

        // =============================================================================
        // JSON EXPORT
        // =============================================================================
        
        public async Task<bool> ExportToJsonAsync(string exportPath = null)
        {
            try
            {
                exportPath ??= await ExportToJsonInternalAsync();
                return File.Exists(exportPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to export to JSON");
                return false;
            }
        }
        
        private async Task<string> ExportToJsonInternalAsync()
        {
            var exportFilePath = Path.Combine(_exportPath, $"tree_export_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            
            try
            {
                var allNodes = await _repository.GetAllNodesAsync(includeDeleted: false);
                
                var exportData = new
                {
                    ExportedAt = DateTime.UtcNow,
                    ExportVersion = 1,
                    NodeCount = allNodes.Count,
                    ExportType = "TreeDatabase",
                    SourceDatabase = "NoteNest TreeDatabase",
                    Nodes = allNodes.Select(n => new
                    {
                        Id = n.Id.ToString(),
                        ParentId = n.ParentId?.ToString(),
                        Name = n.Name,
                        CanonicalPath = n.CanonicalPath,
                        DisplayPath = n.DisplayPath,
                        AbsolutePath = n.AbsolutePath,
                        NodeType = n.NodeType.ToString(),
                        FileExtension = n.FileExtension,
                        FileSize = n.FileSize,
                        CreatedAt = n.CreatedAt,
                        ModifiedAt = n.ModifiedAt,
                        AccessedAt = n.AccessedAt,
                        QuickHash = n.QuickHash,
                        FullHash = n.FullHash,
                        IsPinned = n.IsPinned,
                        IsExpanded = n.IsExpanded,
                        SortOrder = n.SortOrder,
                        ColorTag = n.ColorTag,
                        CustomProperties = n.CustomProperties
                    }).ToList()
                };
                
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };
                
                var json = JsonSerializer.Serialize(exportData, options);
                await File.WriteAllTextAsync(exportFilePath, json);
                
                // Also create human-readable tree structure
                var treeStructurePath = Path.Combine(_exportPath, $"tree_structure_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                await ExportTreeStructureAsync(treeStructurePath, allNodes);
                
                _logger.Info($"Exported {allNodes.Count} nodes to: {Path.GetFileName(exportFilePath)}");
                return exportFilePath;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to export to JSON");
                throw;
            }
        }
        
        private async Task ExportTreeStructureAsync(string filePath, List<TreeNode> nodes)
        {
            try
            {
                var lines = new List<string>
                {
                    $"NoteNest Tree Structure Export",
                    $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
                    $"Total Nodes: {nodes.Count}",
                    "",
                    "Tree Structure:",
                    "==============="
                };
                
                // Build hierarchical tree representation
                var rootNodes = nodes.Where(n => n.ParentId == null).OrderBy(n => n.Name);
                
                foreach (var root in rootNodes)
                {
                    AppendNodeToTree(lines, root, nodes, 0);
                }
                
                await File.WriteAllLinesAsync(filePath, lines);
                _logger.Debug($"Exported tree structure to: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to export tree structure: {ex.Message}");
            }
        }
        
        private void AppendNodeToTree(List<string> lines, TreeNode node, List<TreeNode> allNodes, int depth)
        {
            var indent = new string(' ', depth * 2);
            var icon = node.NodeType == TreeNodeType.Category ? "📁" : "📄";
            var pinnedMark = node.IsPinned ? "📌" : "";
            var sizeMark = node.FileSize.HasValue ? $" ({FormatFileSize(node.FileSize.Value)})" : "";
            
            lines.Add($"{indent}{icon} {node.Name}{sizeMark} {pinnedMark}");
            
            if (node.NodeType == TreeNodeType.Category)
            {
                var children = allNodes
                    .Where(n => n.ParentId == node.Id)
                    .OrderBy(n => n.NodeType)
                    .ThenBy(n => n.Name);
                
                foreach (var child in children)
                {
                    AppendNodeToTree(lines, child, allNodes, depth + 1);
                }
            }
        }
        
        private static string FormatFileSize(long fileSize)
        {
            if (fileSize < 1024) return $"{fileSize} B";
            if (fileSize < 1024 * 1024) return $"{fileSize / 1024:F1} KB";
            if (fileSize < 1024 * 1024 * 1024) return $"{fileSize / (1024 * 1024):F1} MB";
            return $"{fileSize / (1024 * 1024 * 1024):F1} GB";
        }

        // =============================================================================
        // MAINTENANCE HELPERS
        // =============================================================================
        
        private async Task PerformScheduledIntegrityCheck()
        {
            try
            {
                if (!await VerifyIntegrityAsync())
                {
                    _logger.Warning("Scheduled integrity check failed, triggering auto-recovery");
                    await AutoRecoverAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Scheduled integrity check failed");
            }
        }
        
        private async Task CleanOldBackupsAsync(string directory, int keepCount)
        {
            try
            {
                var files = Directory.GetFiles(directory, "*.db")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .Skip(keepCount)
                    .ToList();
                
                foreach (var file in files)
                {
                    try
                    {
                        file.Delete();
                        _logger.Debug($"Deleted old backup: {file.Name}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to delete old backup {file.Name}: {ex.Message}");
                    }
                }
                
                if (files.Any())
                {
                    _logger.Info($"Cleaned up {files.Count} old backup files");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to clean old backups: {ex.Message}");
            }
        }
        
        private string GetLatestBackupFile(string directory)
        {
            try
            {
                return Directory.GetFiles(directory, "*.db")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .FirstOrDefault()?.FullName;
            }
            catch
            {
                return null;
            }
        }
        
        private TimeSpan CalculateTimeUntilNext3AM()
        {
            var now = DateTime.Now;
            var next3AM = now.Date.AddDays(1).AddHours(3);
            
            if (now.Hour < 3)
            {
                next3AM = now.Date.AddHours(3);
            }
            
            return next3AM - now;
        }
    }
    
    // =============================================================================
    // SUPPORTING TYPES
    // =============================================================================
    
    public enum BackupType
    {
        Shadow,    // Real-time shadow copy
        Daily,     // Scheduled daily backup
        Manual,    // User-triggered backup
        Export     // JSON export for portability
    }
    
    public class BackupInfo
    {
        public string FilePath { get; set; }
        public DateTime CreatedAt { get; set; }
        public long SizeBytes { get; set; }
        public BackupType Type { get; set; }
        public string DisplayName { get; set; }
        public bool IsValid { get; set; } = true;
        
        public string SizeDisplay => SizeBytes < 1024 * 1024 
            ? $"{SizeBytes / 1024:F1} KB" 
            : $"{SizeBytes / (1024.0 * 1024.0):F1} MB";
    }
    
    public class BackupStatus
    {
        public DateTime CheckedAt { get; set; }
        public bool IsHealthy { get; set; }
        public string Error { get; set; }
        public BackupInfo ShadowBackup { get; set; }
        public List<BackupInfo> DailyBackups { get; set; } = new();
        public DateTime? LatestBackup { get; set; }
    }
}

// Extension method for SqliteConnection backup
namespace Microsoft.Data.Sqlite
{
    public static class SqliteBackupExtensions
    {
        public static async Task BackupDatabaseAsync(
            this SqliteConnection source, 
            SqliteConnection destination,
            string sourceName = "main",
            string destinationName = "main",
            int pages = -1,
            Action<int, int> progressCallback = null,
            int sleepMs = 0)
        {
            // Ensure destination is open
            if (destination.State != System.Data.ConnectionState.Open)
                await destination.OpenAsync();
            
            // Simple file copy approach for now
            // Note: Full SQLite backup API would require additional implementation
            var sourcePath = new SqliteConnectionStringBuilder(source.ConnectionString).DataSource;
            var destPath = new SqliteConnectionStringBuilder(destination.ConnectionString).DataSource;
            File.Copy(sourcePath, destPath, overwrite: true);
            
            // Copy completed
            progressCallback?.Invoke(1, 1);
        }
    }
}
