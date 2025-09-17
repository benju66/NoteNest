using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Models;

namespace NoteNest.Core.Services.SaveCoordination
{
    /// <summary>
    /// Core save coordinator that provides retry logic, file coordination, and status reporting
    /// Hybrid approach: Wraps existing save operations without architectural changes
    /// </summary>
    public class SaveCoordinator : IDisposable
    {
        private readonly ConcurrentHashSet<string> _savingFiles = new();
        private readonly FileWatcherService _fileWatcher;
        private readonly IUserNotificationService _notifications;
        private readonly IAppLogger _logger;
        private readonly SaveStatusManager _statusManager;
        private readonly AtomicMetadataSaver _atomicSaver;
        private bool _disposed = false;

        public SaveCoordinator(
            FileWatcherService fileWatcher,
            IUserNotificationService notifications,
            IAppLogger logger,
            SaveStatusManager statusManager,
            AtomicMetadataSaver atomicSaver)
        {
            _fileWatcher = fileWatcher ?? throw new ArgumentNullException(nameof(fileWatcher));
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _statusManager = statusManager ?? throw new ArgumentNullException(nameof(statusManager));
            _atomicSaver = atomicSaver ?? throw new ArgumentNullException(nameof(atomicSaver));
        }

        /// <summary>
        /// Main save wrapper with retry logic, file coordination, and status updates
        /// </summary>
        public async Task<bool> SafeSaveWithRetry(
            Func<Task> saveAction,
            string filePath,
            string noteTitle)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(noteTitle))
            {
                _logger.Warning("SafeSaveWithRetry called with null/empty parameters");
                return false;
            }

            var normalizedPath = filePath.ToLowerInvariant();
            
            // Prevent concurrent saves to same file
            if (!_savingFiles.Add(normalizedPath))
            {
                _logger.Debug($"Save already in progress for: {noteTitle}");
                return true; // Already saving - coalesce the request
            }

            try
            {
                // Show save starting in status bar
                _statusManager.ShowSaveInProgress(noteTitle);

                // Suspend file watcher to prevent false external change detection
                _fileWatcher?.SuspendPath(filePath);

                // Exponential backoff retry logic
                var delays = new[] { 100, 500, 1500 }; // ms
                Exception lastException = null;

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        await saveAction();

                        // Success - show in status bar and log
                        _statusManager.ShowSaveSuccess(noteTitle);
                        _logger.Debug($"Save succeeded on attempt {attempt + 1}: {noteTitle}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        _logger.Warning($"Save attempt {attempt + 1} failed for {noteTitle}: {ex.Message}");

                        if (attempt < 2) // Not the last attempt
                        {
                            // Show retry status in status bar
                            _statusManager.ShowSaveFailure(noteTitle, ex.Message, isRetrying: true);
                            await Task.Delay(delays[attempt]);
                        }
                    }
                }

                // All retries failed
                _statusManager.ShowSaveFailure(noteTitle, lastException?.Message ?? "Unknown error");
                
                // Show user dialog for critical failures (non-blocking)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _notifications.ShowErrorAsync(
                            $"Failed to save '{noteTitle}' after 3 attempts.", 
                            lastException);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to show error notification");
                    }
                });

                _logger.Error(lastException, $"All save attempts failed for: {filePath}");
                return false;
            }
            finally
            {
                // Always clean up
                _savingFiles.Remove(normalizedPath);

                // Resume file watcher with delay to avoid catching our own write
                _ = Task.Delay(750).ContinueWith(_ =>
                {
                    try
                    {
                        _fileWatcher?.ResumePath(filePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to resume file watcher for {filePath}: {ex.Message}");
                    }
                }, TaskScheduler.Default);
            }
        }

        /// <summary>
        /// Enhanced save with atomic metadata coordination
        /// Provides bulletproof content + metadata consistency
        /// </summary>
        public async Task<bool> SafeSaveWithMetadata(
            NoteModel note,
            string content,
            Func<Task> legacyContentSaveAction,
            string noteTitle)
        {
            if (note == null || string.IsNullOrEmpty(note.FilePath) || string.IsNullOrEmpty(noteTitle))
            {
                _logger.Warning("SafeSaveWithMetadata called with invalid parameters");
                return false;
            }

            var normalizedPath = note.FilePath.ToLowerInvariant();
            
            // Prevent concurrent saves to same file
            if (!_savingFiles.Add(normalizedPath))
            {
                _logger.Debug($"Atomic save already in progress for: {noteTitle}");
                return true; // Already saving - coalesce the request
            }

            try
            {
                // Show save starting in status bar
                _statusManager.ShowSaveInProgress(noteTitle);

                // Suspend file watcher to prevent false external change detection
                _fileWatcher?.SuspendPath(note.FilePath);

                // Exponential backoff retry logic for atomic saves
                var delays = new[] { 100, 500, 1500 }; // ms
                AtomicSaveResult lastResult = null;

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        // Try atomic save (content + metadata together)
                        lastResult = await _atomicSaver.SaveContentAndMetadataAtomically(
                            note, content, legacyContentSaveAction);

                        if (lastResult.Success)
                        {
                            // Success - show appropriate status
                            var statusSuffix = lastResult.WasFullyAtomic ? " (atomic)" : " (fallback)";
                            _statusManager.ShowSaveSuccess(noteTitle + statusSuffix);
                            _logger.Debug($"Atomic save succeeded on attempt {attempt + 1}: {noteTitle} (fully atomic: {lastResult.WasFullyAtomic})");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Atomic save attempt {attempt + 1} failed for {noteTitle}: {ex.Message}");
                        lastResult = new AtomicSaveResult 
                        { 
                            Success = false, 
                            ErrorMessage = ex.Message 
                        };

                        if (attempt < 2) // Not the last attempt
                        {
                            // Show retry status in status bar
                            _statusManager.ShowSaveFailure(noteTitle, ex.Message, isRetrying: true);
                            await Task.Delay(delays[attempt]);
                        }
                    }
                }

                // All atomic save attempts failed
                var errorMsg = lastResult?.ErrorMessage ?? "Unknown error";
                _statusManager.ShowSaveFailure(noteTitle, errorMsg);
                
                // Show user dialog for critical failures (non-blocking)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _notifications.ShowErrorAsync(
                            $"Failed to save '{noteTitle}' atomically after 3 attempts. Content may have been saved but metadata could be inconsistent.", 
                            new Exception(errorMsg));
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to show atomic save error notification");
                    }
                });

                _logger.Error($"All atomic save attempts failed for: {note.FilePath}");
                return false;
            }
            finally
            {
                // Always clean up
                _savingFiles.Remove(normalizedPath);

                // Resume file watcher with delay to avoid catching our own write
                _ = Task.Delay(750).ContinueWith(_ =>
                {
                    try
                    {
                        _fileWatcher?.ResumePath(note.FilePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to resume file watcher for {note.FilePath}: {ex.Message}");
                    }
                }, TaskScheduler.Default);
            }
        }

        /// <summary>
        /// Batch save with coordinated progress reporting
        /// </summary>
        public async Task<BatchSaveResult> SafeBatchSave(
            System.Collections.Generic.IEnumerable<(Func<Task> saveAction, string filePath, string noteTitle)> saveOperations)
        {
            var operations = saveOperations.ToList();
            if (!operations.Any())
            {
                return new BatchSaveResult { SuccessCount = 0, FailureCount = 0 };
            }

            _logger.Info($"Starting batch save of {operations.Count} files");
            _statusManager.ShowBatchSaveProgress(0, operations.Count);

            var result = new BatchSaveResult();
            int completed = 0;

            // Use SemaphoreSlim to limit concurrent saves (prevent overwhelming system)
            using var semaphore = new SemaphoreSlim(3, 3); // Max 3 concurrent saves

            var saveTasks = operations.Select(async (op, index) =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var success = await SafeSaveWithRetry(op.saveAction, op.filePath, op.noteTitle);
                    var newCompleted = Interlocked.Increment(ref completed);
                    
                    // Update batch progress
                    _statusManager.ShowBatchSaveProgress(newCompleted, operations.Count);

                    lock (result)
                    {
                        if (success)
                        {
                            result.SuccessCount++;
                        }
                        else
                        {
                            result.FailureCount++;
                            result.FailedItems.Add($"{op.noteTitle} ({op.filePath})");
                        }
                    }

                    return success;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(saveTasks);

            // Final batch status
            if (result.FailureCount == 0)
            {
                _statusManager.ShowSaveSuccess($"All {result.SuccessCount} files saved");
            }
            else if (result.SuccessCount == 0)
            {
                _statusManager.ShowSaveFailure("Batch save", $"All {result.FailureCount} saves failed");
            }
            else
            {
                _statusManager.ShowSaveFailure("Batch save", 
                    $"{result.SuccessCount} succeeded, {result.FailureCount} failed");
            }

            _logger.Info($"Batch save completed: {result.SuccessCount} succeeded, {result.FailureCount} failed");
            return result;
        }

        /// <summary>
        /// Get current save statistics
        /// </summary>
        public SaveCoordinatorStats GetStats()
        {
            return new SaveCoordinatorStats
            {
                CurrentlySaving = _savingFiles.Count,
                SavingFiles = _savingFiles.ToArray()
            };
        }

        /// <summary>
        /// Get atomic save metrics for monitoring data integrity improvements
        /// </summary>
        public AtomicSaveMetrics GetAtomicSaveMetrics()
        {
            return _atomicSaver.GetMetrics();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Wait for any in-progress saves to complete (with timeout)
            var timeout = DateTime.UtcNow.AddSeconds(10);
            while (_savingFiles.Any() && DateTime.UtcNow < timeout)
            {
                Thread.Sleep(100);
            }

            _statusManager?.Dispose();
            _logger?.Info("SaveCoordinator disposed");
        }
    }

    /// <summary>
    /// Result of batch save operation
    /// </summary>
    public class BatchSaveResult
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public System.Collections.Generic.List<string> FailedItems { get; set; } = new();
    }

    /// <summary>
    /// Current save coordinator statistics
    /// </summary>
    public class SaveCoordinatorStats
    {
        public int CurrentlySaving { get; set; }
        public string[] SavingFiles { get; set; } = Array.Empty<string>();
    }
}

/// <summary>
/// Thread-safe HashSet implementation for .NET Framework compatibility
/// </summary>
internal class ConcurrentHashSet<T>
{
    private readonly System.Collections.Generic.HashSet<T> _hashSet = new();
    private readonly object _lock = new object();

    public bool Add(T item)
    {
        lock (_lock)
        {
            return _hashSet.Add(item);
        }
    }

    public bool Remove(T item)
    {
        lock (_lock)
        {
            return _hashSet.Remove(item);
        }
    }

    public bool Contains(T item)
    {
        lock (_lock)
        {
            return _hashSet.Contains(item);
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _hashSet.Count;
            }
        }
    }

    public T[] ToArray()
    {
        lock (_lock)
        {
            var array = new T[_hashSet.Count];
            _hashSet.CopyTo(array);
            return array;
        }
    }

    public bool Any()
    {
        lock (_lock)
        {
            return _hashSet.Count > 0;
        }
    }
}
