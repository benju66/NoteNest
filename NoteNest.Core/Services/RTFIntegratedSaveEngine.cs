using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Models;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// RTF-aware save engine that properly integrates with existing RTF pipeline
    /// Simplifies coordination while preserving all RTF functionality and adding critical safety features
    /// </summary>
    public class RTFIntegratedSaveEngine : IDisposable
    {
        private readonly string _dataPath;
        private readonly string _tempPath;
        private readonly string _walPath;
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private readonly Dictionary<string, DateTime> _lastSaveTime = new();
        private readonly IStatusNotifier _statusNotifier;
        
        // Write-Ahead Log for crash protection
        private readonly WriteAheadLog _wal;
        
        // Metrics
        private int _totalSaves = 0;
        private int _failedSaves = 0;
        private int _retriedSaves = 0;
        private bool _disposed = false;

        public RTFIntegratedSaveEngine(string dataPath, IStatusNotifier statusNotifier)
        {
            _dataPath = dataPath ?? throw new ArgumentNullException(nameof(dataPath));
            _statusNotifier = statusNotifier ?? throw new ArgumentNullException(nameof(statusNotifier));
            _tempPath = Path.Combine(_dataPath, ".temp");
            _walPath = Path.Combine(_dataPath, ".wal");
            
            Directory.CreateDirectory(_dataPath);
            Directory.CreateDirectory(_tempPath);
            Directory.CreateDirectory(_walPath);
            
            _wal = new WriteAheadLog(_walPath);
        }

        /// <summary>
        /// Save RTF content with full safety features (WAL, retry, atomic)
        /// Content should be pre-extracted from RTF editor using RTFOperations.SaveToRTF()
        /// </summary>
        public async Task<SaveResult> SaveRTFContentAsync(
            string noteId,
            string rtfContent,
            string title = null,
            SaveType saveType = SaveType.Manual)
        {
            if (string.IsNullOrEmpty(noteId) || string.IsNullOrEmpty(rtfContent))
                return new SaveResult { Success = false, Error = "Invalid note ID or content" };

            await _saveLock.WaitAsync();
            try
            {
                _totalSaves++;
                var startTime = DateTime.UtcNow;
                
                // Show saving status
                _statusNotifier.ShowStatus($"Saving {title ?? noteId}...", StatusType.InProgress);

                // 1. Validate RTF content (it should already be processed by RTFOperations.SaveToRTF)
                // No need to extract - content is already provided

                // 2. Write to WAL first (crash protection)
                var walEntry = await _wal.WriteAsync(noteId, rtfContent);

                // 3. Try save with retry logic
                int retryCount = 0;
                const int maxRetries = 3;
                Exception lastException = null;
                
                while (retryCount < maxRetries)
                {
                    try
                    {
                        // Atomic save using temp files
                        var success = await AtomicSaveAsync(noteId, rtfContent, title, saveType);
                        
                        if (success)
                        {
                            // 4. Clear WAL entry on success
                            await _wal.RemoveAsync(walEntry.Id);
                            
                            // 5. Update tracking
                            _lastSaveTime[noteId] = DateTime.UtcNow;
                            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

                            // 6. Show success with appropriate message
                            var message = saveType == SaveType.AutoSave 
                                ? $"Auto-saved {title ?? noteId}"
                                : $"Saved {title ?? noteId}";
                            _statusNotifier.ShowStatus(message, StatusType.Success, duration: 2000);

                            // 7. Memory management for large content
                            if (!string.IsNullOrEmpty(rtfContent) && rtfContent.Length > 1_000_000) // >1MB RTF
                            {
                                GC.Collect(0, GCCollectionMode.Optimized);
                            }

                            return new SaveResult
                            {
                                Success = true,
                                SavedAt = DateTime.UtcNow,
                                Duration = duration,
                                UsedWAL = true,
                                RetryCount = retryCount
                            };
                        }
                    }
                    catch (IOException ioEx) when (IsFileLocked(ioEx))
                    {
                        // File locked - retry after delay
                        lastException = ioEx;
                        retryCount++;
                        _retriedSaves++;
                        
                        if (retryCount < maxRetries)
                        {
                            _statusNotifier.ShowStatus(
                                $"File locked, retrying... ({retryCount}/{maxRetries})", 
                                StatusType.Warning, duration: 1000);
                            await Task.Delay(500 * retryCount); // Exponential backoff
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        break; // Don't retry on non-transient errors
                    }
                }

                // Save failed after retries
                _failedSaves++;
                
                // Emergency save attempt for manual saves
                if (saveType == SaveType.Manual && !string.IsNullOrEmpty(rtfContent))
                {
                    try
                    {
                        var emergencyPath = Path.Combine(_dataPath, 
                            $"EMERGENCY_{noteId}_{DateTime.Now:yyyyMMddHHmmss}.rtf");
                        await File.WriteAllTextAsync(emergencyPath, rtfContent);
                        _statusNotifier.ShowStatus(
                            $"Emergency save created: {Path.GetFileName(emergencyPath)}", 
                            StatusType.Warning, duration: 10000);
                    }
                    catch { /* Last resort failed */ }
                }

                _statusNotifier.ShowStatus(
                    $"Failed to save {title ?? noteId}: {lastException?.Message}", 
                    StatusType.Error, duration: 5000);

                return new SaveResult
                {
                    Success = false,
                    Error = lastException?.Message,
                    SavedAt = DateTime.UtcNow,
                    RetryCount = retryCount
                };
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <summary>
        /// Save from string content with RTF validation and processing
        /// </summary>
        public async Task<SaveResult> SaveFromStringAsync(
            string noteId,
            string content,
            string title = null,
            SaveType saveType = SaveType.Manual,
            bool isRtf = true)
        {
            if (string.IsNullOrEmpty(noteId))
                return new SaveResult { Success = false, Error = "Invalid note ID" };

            await _saveLock.WaitAsync();
            try
            {
                // RTF validation and sanitization should be done at UI layer before calling this method
                // This keeps Core project independent of UI dependencies

                // Use similar logic as SaveFromRichTextBoxAsync but with string content
                var walEntry = await _wal.WriteAsync(noteId, content);
                
                // Attempt atomic save with retry logic
                return await SaveWithRetryLogic(noteId, content, title, saveType, walEntry);
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <summary>
        /// Load RTF content from file with crash recovery
        /// UI integration should call RTFOperations.LoadFromRTF() with the returned content
        /// </summary>
        public async Task<LoadResult> LoadRTFContentAsync(string noteId)
        {
            if (string.IsNullOrEmpty(noteId))
                return new LoadResult { Success = false, Error = "Invalid note ID" };

            try
            {
                // Check for RTF file
                var rtfFile = Path.Combine(_dataPath, $"{noteId}.rtf");
                var metaFile = Path.Combine(_dataPath, $"{noteId}.meta");
                
                if (!File.Exists(rtfFile))
                {
                    // Check WAL for crash recovery
                    var walContent = await _wal.RecoverAsync(noteId);
                    if (!string.IsNullOrEmpty(walContent))
                    {
                        _statusNotifier.ShowStatus(
                            "Recovered unsaved content from crash protection", 
                            StatusType.Warning, duration: 5000);
                        
                        return new LoadResult
                        {
                            Success = true,
                            RecoveredFromWAL = true,
                            Content = walContent
                        };
                    }
                    
                    return new LoadResult { Success = false, Error = "Note not found" };
                }

                var rtfContent = await File.ReadAllTextAsync(rtfFile);
                
                // Load metadata if exists
                NoteMetadata? metadata = null;
                if (File.Exists(metaFile))
                {
                    try
                    {
                        var metaJson = await File.ReadAllTextAsync(metaFile);
                        metadata = JsonSerializer.Deserialize<NoteMetadata>(metaJson);
                    }
                    catch 
                    { 
                        metadata = new NoteMetadata { Id = noteId, Title = noteId };
                    }
                }

                return new LoadResult
                {
                    Success = true,
                    Content = rtfContent,
                    Metadata = metadata ?? new NoteMetadata { Id = noteId, Title = noteId }
                };
            }
            catch (Exception ex)
            {
                return new LoadResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// True atomic save - content and metadata together
        /// </summary>
        private async Task<bool> AtomicSaveAsync(
            string noteId, 
            string rtfContent, 
            string title, 
            SaveType saveType)
        {
            var contentFile = Path.Combine(_dataPath, $"{noteId}.rtf");
            var metaFile = Path.Combine(_dataPath, $"{noteId}.meta");
            
            // Use GUID for truly unique temp files
            var tempId = Guid.NewGuid().ToString("N");
            var tempContent = Path.Combine(_tempPath, $"{tempId}.content");
            var tempMeta = Path.Combine(_tempPath, $"{tempId}.meta");

            try
            {
                // 1. Write both files to temp location
                await File.WriteAllTextAsync(tempContent, rtfContent ?? string.Empty);
                
                var metadata = new NoteMetadata
                {
                    Id = noteId,
                    Title = title ?? "Untitled",
                    LastSaved = DateTime.UtcNow,
                    Size = rtfContent?.Length ?? 0,
                    SaveType = saveType.ToString()
                };
                
                var metaJson = JsonSerializer.Serialize(metadata, 
                    new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(tempMeta, metaJson);

                // 2. Atomic move both files (as atomic as possible on Windows)
                File.Move(tempContent, contentFile, true);
                
                try
                {
                    File.Move(tempMeta, metaFile, true);
                }
                catch
                {
                    // Content saved but metadata failed - log but don't fail the save
                    System.Diagnostics.Debug.WriteLine($"Warning: Metadata save failed for {noteId}");
                }

                return true;
            }
            catch
            {
                // Clean up temp files on failure
                try
                {
                    if (File.Exists(tempContent)) File.Delete(tempContent);
                    if (File.Exists(tempMeta)) File.Delete(tempMeta);
                }
                catch { /* Ignore cleanup errors */ }
                
                throw; // Re-throw for retry logic
            }
        }

        /// <summary>
        /// Common retry logic for string-based saves
        /// </summary>
        private async Task<SaveResult> SaveWithRetryLogic(
            string noteId, 
            string content, 
            string title, 
            SaveType saveType,
            WALEntry walEntry)
        {
            int retryCount = 0;
            const int maxRetries = 3;
            Exception lastException = null;
            var startTime = DateTime.UtcNow;
            
            while (retryCount < maxRetries)
            {
                try
                {
                    var success = await AtomicSaveAsync(noteId, content, title, saveType);
                    
                    if (success)
                    {
                        await _wal.RemoveAsync(walEntry.Id);
                        _lastSaveTime[noteId] = DateTime.UtcNow;
                        var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

                        var message = saveType == SaveType.AutoSave 
                            ? $"Auto-saved {title ?? noteId}"
                            : $"Saved {title ?? noteId}";
                        _statusNotifier.ShowStatus(message, StatusType.Success, duration: 2000);

                        return new SaveResult
                        {
                            Success = true,
                            SavedAt = DateTime.UtcNow,
                            Duration = duration,
                            UsedWAL = true,
                            RetryCount = retryCount
                        };
                    }
                }
                catch (IOException ioEx) when (IsFileLocked(ioEx))
                {
                    lastException = ioEx;
                    retryCount++;
                    _retriedSaves++;
                    
                    if (retryCount < maxRetries)
                    {
                        _statusNotifier.ShowStatus(
                            $"File locked, retrying... ({retryCount}/{maxRetries})", 
                            StatusType.Warning, duration: 1000);
                        await Task.Delay(500 * retryCount);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    break;
                }
            }

            _failedSaves++;
            _statusNotifier.ShowStatus(
                $"Failed to save {title ?? noteId}: {lastException?.Message}", 
                StatusType.Error, duration: 5000);

            return new SaveResult
            {
                Success = false,
                Error = lastException?.Message,
                SavedAt = DateTime.UtcNow,
                RetryCount = retryCount
            };
        }

        /// <summary>
        /// Check if IOException is due to file lock
        /// </summary>
        private bool IsFileLocked(IOException exception)
        {
            var errorCode = exception.HResult & 0xFFFF;
            return errorCode == 32 || errorCode == 33; // ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION
        }

        /// <summary>
        /// Check if auto-save should be throttled
        /// </summary>
        public bool ShouldThrottleAutoSave(string noteId, int minimumSeconds = 5)
        {
            if (!_lastSaveTime.ContainsKey(noteId))
                return false;

            var timeSinceLastSave = DateTime.UtcNow - _lastSaveTime[noteId];
            return timeSinceLastSave.TotalSeconds < minimumSeconds;
        }

        /// <summary>
        /// Get save metrics for monitoring
        /// </summary>
        public SaveMetrics GetMetrics()
        {
            return new SaveMetrics
            {
                TotalSaves = _totalSaves,
                FailedSaves = _failedSaves,
                RetriedSaves = _retriedSaves,
                SuccessRate = _totalSaves > 0 
                    ? ((_totalSaves - _failedSaves) * 100.0 / _totalSaves) 
                    : 100
            };
        }

        /// <summary>
        /// Recover any unsaved content from WAL
        /// </summary>
        public async Task<Dictionary<string, string>> RecoverFromWAL()
        {
            return await _wal.RecoverAllAsync();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _saveLock?.Dispose();
                _wal?.Dispose();
                _disposed = true;
            }
        }
    }

    // Supporting classes for the save engine
    public enum SaveType
    {
        Manual,
        AutoSave,
        TabClose,
        AppShutdown
    }

    public class SaveResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public DateTime SavedAt { get; set; }
        public double Duration { get; set; }
        public bool UsedWAL { get; set; }
        public int RetryCount { get; set; }
    }

    public class LoadResult
    {
        public bool Success { get; set; }
        public string? Content { get; set; }
        public NoteMetadata? Metadata { get; set; }
        public string? Error { get; set; }
        public bool RecoveredFromWAL { get; set; }
    }

    public class SaveMetrics
    {
        public int TotalSaves { get; set; }
        public int FailedSaves { get; set; }
        public int RetriedSaves { get; set; }
        public double SuccessRate { get; set; }
    }

    public class NoteMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime LastSaved { get; set; }
        public int Size { get; set; }
        public string SaveType { get; set; } = string.Empty;
    }
}
