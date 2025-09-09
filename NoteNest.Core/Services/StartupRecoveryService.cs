using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    public class RecoverySummary
    {
        public List<string> RecoveredFiles { get; } = new();
        public List<string> BackupFiles { get; } = new();
        public List<string> CleanedTmpFiles { get; } = new();
        public List<string> FailedRecoveries { get; } = new();
        public List<string> EmergencyFiles { get; } = new();
    }
    
    public class StartupRecoveryService
    {
        private readonly ISaveManager _saveManager;
        private readonly IAppLogger _logger;

        public StartupRecoveryService(ISaveManager saveManager, IAppLogger logger)
        {
            _saveManager = saveManager;
            _logger = logger;
        }

        public async Task<RecoverySummary> RecoverInterruptedSavesAsync(string notesPath)
        {
            var summary = new RecoverySummary();
            
            // IMPORTANT: This must run BEFORE any notes are opened
            
            // 1. Check for .tmp files (interrupted saves)
            if (Directory.Exists(notesPath))
            {
                var tmpFiles = Directory.GetFiles(notesPath, "*.tmp", SearchOption.AllDirectories);
                foreach (var tmpFile in tmpFiles)
                {
                    try
                    {
                        var originalFile = tmpFile.Substring(0, tmpFile.Length - 4);
                        
                        // Check if tmp file is newer/valid
                        bool shouldRecover = true;
                        if (File.Exists(originalFile))
                        {
                            var tmpTime = File.GetLastWriteTimeUtc(tmpFile);
                            var originalTime = File.GetLastWriteTimeUtc(originalFile);
                            
                            // Only recover if tmp is newer
                            shouldRecover = tmpTime > originalTime;
                            
                            // Also check if tmp file has content
                            if (shouldRecover)
                            {
                                var tmpInfo = new FileInfo(tmpFile);
                                shouldRecover = tmpInfo.Length > 0;
                            }
                        }
                        
                        if (shouldRecover)
                        {
                            // Create backup of original if exists
                            if (File.Exists(originalFile))
                            {
                                var backupFile = originalFile + $".backup_{DateTime.Now:yyyyMMddHHmmss}";
                                File.Copy(originalFile, backupFile, true);
                                summary.BackupFiles.Add(backupFile);
                                _logger.Info($"Created backup: {backupFile}");
                            }
                            
                            // Recover from tmp
                            File.Move(tmpFile, originalFile, true);
                            summary.RecoveredFiles.Add(originalFile);
                            _logger.Info($"Recovered interrupted save: {originalFile}");
                        }
                        else
                        {
                            // Delete old/invalid tmp file
                            File.Delete(tmpFile);
                            summary.CleanedTmpFiles.Add(tmpFile);
                            _logger.Info($"Cleaned old tmp file: {tmpFile}");
                        }
                    }
                    catch (Exception ex)
                    {
                        summary.FailedRecoveries.Add(tmpFile);
                        _logger.Error(ex, $"Failed to recover: {tmpFile}");
                    }
                }
            }
            
            // 2. Check for emergency files on desktop
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (Directory.Exists(desktop))
            {
                var emergencyFiles = Directory.GetFiles(desktop, "NoteNest_Recovery_*.txt");
                
                foreach (var emergencyFile in emergencyFiles)
                {
                    try
                    {
                        summary.EmergencyFiles.Add(emergencyFile);
                        _logger.Info($"Found emergency recovery file: {emergencyFile}");
                        
                        // Note: Don't auto-recover these, let user decide
                        // The UI should prompt user about these files
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Failed to process emergency file: {emergencyFile}");
                    }
                }
            }
            
            // 3. Clean up old backup files (older than 7 days)
            if (Directory.Exists(notesPath))
            {
                var oldBackups = Directory.GetFiles(notesPath, "*.backup_*", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        try
                        {
                            var created = File.GetCreationTimeUtc(f);
                            return created < DateTime.UtcNow.AddDays(-7);
                        }
                        catch
                        {
                            return false;
                        }
                    });
                
                foreach (var oldBackup in oldBackups)
                {
                    try
                    {
                        File.Delete(oldBackup);
                        _logger.Info($"Deleted old backup: {oldBackup}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to delete old backup: {oldBackup} - {ex.Message}");
                    }
                }
            }
            
            return summary;
        }
        
        public async Task<bool> RecoverEmergencyFileAsync(string emergencyFile, string targetPath)
        {
            try
            {
                if (!File.Exists(emergencyFile))
                    return false;
                
                // Read emergency content
                var content = await File.ReadAllTextAsync(emergencyFile);
                
                // Create backup if target exists
                if (File.Exists(targetPath))
                {
                    var backupPath = targetPath + $".before_emergency_{DateTime.Now:yyyyMMddHHmmss}";
                    File.Copy(targetPath, backupPath, true);
                    _logger.Info($"Created backup before emergency recovery: {backupPath}");
                }
                
                // Write recovered content
                await File.WriteAllTextAsync(targetPath, content);
                
                // Delete emergency file
                File.Delete(emergencyFile);
                
                _logger.Info($"Recovered emergency file to: {targetPath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to recover emergency file: {emergencyFile}");
                return false;
            }
        }
    }
}
