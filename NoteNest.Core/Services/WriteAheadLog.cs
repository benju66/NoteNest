using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Write-Ahead Log implementation for crash protection
    /// Ensures content is persisted before attempting file operations
    /// </summary>
    public class WriteAheadLog : IWriteAheadLog
    {
        private readonly string _walPath;
        private readonly SemaphoreSlim _walLock = new(1, 1);
        private readonly JsonSerializerOptions _jsonOptions;
        private bool _disposed = false;

        public WriteAheadLog(string walPath)
        {
            _walPath = walPath ?? throw new ArgumentNullException(nameof(walPath));
            _jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            
            // Ensure WAL directory exists
            Directory.CreateDirectory(_walPath);
        }

        /// <summary>
        /// Write content to WAL for crash protection
        /// </summary>
        public async Task<WALEntry> WriteAsync(string noteId, string content)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WriteAheadLog));

            await _walLock.WaitAsync();
            try
            {
                var entry = new WALEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    NoteId = noteId,
                    Content = content ?? string.Empty,
                    Timestamp = DateTime.UtcNow
                };

                var walFile = Path.Combine(_walPath, $"{entry.Id}.wal");
                var json = JsonSerializer.Serialize(entry, _jsonOptions);
                await File.WriteAllTextAsync(walFile, json);
                
                return entry;
            }
            finally
            {
                _walLock.Release();
            }
        }

        /// <summary>
        /// Append content to WAL (legacy API for compatibility with existing UnifiedSaveManager)
        /// </summary>
        public async Task AppendAsync(string noteId, string content)
        {
            // Delegate to new WriteAsync method and ignore the return value
            await WriteAsync(noteId, content);
        }

        /// <summary>
        /// Commit/clear WAL entry (legacy API for compatibility)
        /// </summary>
        public async Task CommitAsync(string noteId)
        {
            // For legacy compatibility, find and remove the most recent entry for this note
            if (_disposed || string.IsNullOrEmpty(noteId))
                return;

            await _walLock.WaitAsync();
            try
            {
                var walFiles = Directory.GetFiles(_walPath, "*.wal");
                WALEntry? latestEntry = null;
                string? latestFile = null;

                // Find the most recent WAL entry for this note
                foreach (var walFile in walFiles)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(walFile);
                        var entry = JsonSerializer.Deserialize<WALEntry>(json, _jsonOptions);
                        
                        if (entry?.NoteId == noteId)
                        {
                            if (latestEntry == null || entry.Timestamp > latestEntry.Timestamp)
                            {
                                latestEntry = entry;
                                latestFile = walFile;
                            }
                        }
                    }
                    catch { /* Skip corrupted files */ }
                }

                if (latestFile != null)
                {
                    await RemoveAsync(Path.GetFileNameWithoutExtension(latestFile));
                }
            }
            finally
            {
                _walLock.Release();
            }
        }

        /// <summary>
        /// Remove WAL entry after successful save
        /// </summary>
        public async Task RemoveAsync(string walId)
        {
            if (_disposed || string.IsNullOrEmpty(walId))
                return;

            var walFile = Path.Combine(_walPath, $"{walId}.wal");
            if (File.Exists(walFile))
            {
                try
                {
                    await Task.Run(() => File.Delete(walFile));
                }
                catch (Exception ex)
                {
                    // Non-critical - log but don't fail
                    System.Diagnostics.Debug.WriteLine($"[WAL] Failed to remove entry {walId}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Recover content for a specific note from WAL
        /// </summary>
        public async Task<string?> RecoverAsync(string noteId)
        {
            if (_disposed || string.IsNullOrEmpty(noteId))
                return null;

            await _walLock.WaitAsync();
            try
            {
                var walFiles = Directory.GetFiles(_walPath, "*.wal");
                WALEntry latestEntry = null;
                string latestFile = null;

                // Find the most recent WAL entry for this note
                foreach (var walFile in walFiles)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(walFile);
                        var entry = JsonSerializer.Deserialize<WALEntry>(json, _jsonOptions);
                        
                        if (entry?.NoteId == noteId)
                        {
                            if (latestEntry == null || entry.Timestamp > latestEntry.Timestamp)
                            {
                                latestEntry = entry;
                                latestFile = walFile;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Skip corrupted WAL files
                        System.Diagnostics.Debug.WriteLine($"[WAL] Skipping corrupted file {walFile}: {ex.Message}");
                    }
                }

                if (latestEntry != null && latestFile != null)
                {
                    // Delete the WAL entry after recovery
                    try
                    {
                        File.Delete(latestFile);
                    }
                    catch { /* Non-critical */ }

                    return latestEntry.Content;
                }

                return null;
            }
            finally
            {
                _walLock.Release();
            }
        }

        /// <summary>
        /// Recover all unsaved content from WAL
        /// </summary>
        public async Task<Dictionary<string, string>> RecoverAllAsync()
        {
            if (_disposed)
                return new Dictionary<string, string>();

            var recovered = new Dictionary<string, string>();
            
            await _walLock.WaitAsync();
            try
            {
                var walFiles = Directory.GetFiles(_walPath, "*.wal");
                var entriesByNote = new Dictionary<string, WALEntry>();
                var filesToDelete = new List<string>();

                // Find the latest entry for each note
                foreach (var walFile in walFiles)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(walFile);
                        var entry = JsonSerializer.Deserialize<WALEntry>(json, _jsonOptions);
                        
                        if (entry?.NoteId != null)
                        {
                            if (!entriesByNote.ContainsKey(entry.NoteId) || 
                                entry.Timestamp > entriesByNote[entry.NoteId].Timestamp)
                            {
                                entriesByNote[entry.NoteId] = entry;
                            }
                        }

                        filesToDelete.Add(walFile);
                    }
                    catch (Exception ex)
                    {
                        // Skip corrupted WAL files but still try to clean them up
                        System.Diagnostics.Debug.WriteLine($"[WAL] Skipping corrupted file {walFile}: {ex.Message}");
                        filesToDelete.Add(walFile);
                    }
                }

                // Build result dictionary
                foreach (var kvp in entriesByNote)
                {
                    recovered[kvp.Key] = kvp.Value.Content;
                }

                // Clean up all WAL files after recovery
                foreach (var file in filesToDelete)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch { /* Non-critical */ }
                }
            }
            finally
            {
                _walLock.Release();
            }
            
            return recovered;
        }

        /// <summary>
        /// Clean up old WAL entries (should be called periodically)
        /// </summary>
        public async Task CleanupOldEntriesAsync(TimeSpan maxAge)
        {
            if (_disposed)
                return;

            await _walLock.WaitAsync();
            try
            {
                var walFiles = Directory.GetFiles(_walPath, "*.wal");
                var cutoffTime = DateTime.UtcNow - maxAge;

                foreach (var walFile in walFiles)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(walFile);
                        var entry = JsonSerializer.Deserialize<WALEntry>(json, _jsonOptions);
                        
                        if (entry?.Timestamp < cutoffTime)
                        {
                            File.Delete(walFile);
                        }
                    }
                    catch
                    {
                        // Delete corrupted files
                        try { File.Delete(walFile); } catch { }
                    }
                }
            }
            finally
            {
                _walLock.Release();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _walLock?.Dispose();
                _disposed = true;
            }
        }
    }
}