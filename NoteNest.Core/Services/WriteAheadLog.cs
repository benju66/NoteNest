using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    public interface IWriteAheadLog : IDisposable
    {
        Task AppendAsync(string noteId, string content);
        Task<string?> RecoverAsync(string noteId);
        Task CommitAsync(string noteId);
        Task<Dictionary<string, string>> RecoverAllAsync();
    }

    public class WriteAheadLog : IWriteAheadLog
    {
        private readonly string _walDirectory;
        private readonly IAppLogger _logger;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _noteLocks;
        private readonly Timer _cleanupTimer;
        private const string DELIMITER = "|||WAL|||";

        public WriteAheadLog(IAppLogger logger)
        {
            _logger = logger;
            _noteLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
            
            // Store WAL in user's local app data
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _walDirectory = Path.Combine(appData, "NoteNest", "wal");
            
            if (!Directory.Exists(_walDirectory))
            {
                Directory.CreateDirectory(_walDirectory);
            }
            
            // Cleanup old WAL files every hour
            _cleanupTimer = new Timer(CleanupOldFiles, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        }

        private string GetWalPath(string noteId)
        {
            // Sanitize noteId for filesystem
            var safeId = noteId.Replace(Path.DirectorySeparatorChar, '_')
                              .Replace(Path.AltDirectorySeparatorChar, '_');
            return Path.Combine(_walDirectory, $"{safeId}.wal");
        }

        public async Task AppendAsync(string noteId, string content)
        {
            if (string.IsNullOrEmpty(noteId)) return;
            
            var semaphore = _noteLocks.GetOrAdd(noteId, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            
            try
            {
                var walPath = GetWalPath(noteId);
                
                // Write format: timestamp|||WAL|||content
                // This allows us to find the last complete entry even if write was interrupted
                var entry = $"{DateTime.UtcNow:O}{DELIMITER}{content}{DELIMITER}\n";
                
                await File.AppendAllTextAsync(walPath, entry, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to append to WAL for note {noteId}");
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task<string?> RecoverAsync(string noteId)
        {
            if (string.IsNullOrEmpty(noteId)) return null;
            
            var walPath = GetWalPath(noteId);
            if (!File.Exists(walPath))
                return null;

            try
            {
                var content = await File.ReadAllTextAsync(walPath, Encoding.UTF8);
                if (string.IsNullOrEmpty(content))
                    return null;

                // Find last complete entry
                var lastDelimiterIndex = content.LastIndexOf(DELIMITER);
                if (lastDelimiterIndex < 0)
                    return null;

                // Work backwards to find the start of this entry
                var secondLastDelimiterIndex = content.LastIndexOf(DELIMITER, lastDelimiterIndex - 1);
                if (secondLastDelimiterIndex < 0)
                    return null;

                var recoveredContent = content.Substring(
                    secondLastDelimiterIndex + DELIMITER.Length,
                    lastDelimiterIndex - secondLastDelimiterIndex - DELIMITER.Length);

                _logger.Info($"Recovered content for note {noteId} from WAL ({recoveredContent.Length} chars)");
                return recoveredContent;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to recover from WAL for note {noteId}");
            }

            return null;
        }

        public async Task CommitAsync(string noteId)
        {
            if (string.IsNullOrEmpty(noteId)) return;
            
            var walPath = GetWalPath(noteId);
            try
            {
                if (File.Exists(walPath))
                {
                    await Task.Run(() => File.Delete(walPath));
                    _logger.Debug($"Committed WAL for note {noteId}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to commit WAL for note {noteId}");
            }
        }

        public async Task<Dictionary<string, string>> RecoverAllAsync()
        {
            var recovered = new Dictionary<string, string>();
            
            if (!Directory.Exists(_walDirectory))
                return recovered;

            foreach (var walFile in Directory.GetFiles(_walDirectory, "*.wal"))
            {
                try
                {
                    var noteId = Path.GetFileNameWithoutExtension(walFile);
                    var content = await RecoverAsync(noteId);
                    if (!string.IsNullOrEmpty(content))
                    {
                        recovered[noteId] = content;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to recover WAL file: {walFile}");
                }
            }

            if (recovered.Count > 0)
            {
                _logger.Info($"Recovered {recovered.Count} notes from WAL");
            }

            return recovered;
        }

        private void CleanupOldFiles(object? state)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-7);
                foreach (var file in Directory.GetFiles(_walDirectory, "*.wal"))
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < cutoff && info.Length == 0)
                    {
                        try
                        {
                            File.Delete(file);
                            _logger.Debug($"Cleaned up empty old WAL file: {file}");
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to cleanup old WAL files");
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            foreach (var semaphore in _noteLocks.Values)
            {
                semaphore?.Dispose();
            }
        }
    }
}
