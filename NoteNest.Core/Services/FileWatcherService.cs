using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    public class FileWatcherService : IDisposable
    {
        private readonly Dictionary<string, FileSystemWatcher> _watchers;
        private readonly IAppLogger _logger;
        private bool _disposed;
        private readonly Dictionary<string, DateTime> _lastEventTimes = new();
        private readonly TimeSpan _debounceInterval;
        private readonly ConfigurationService? _config;
        
        // HYBRID ENHANCEMENT: File path suspension for save coordination
        private readonly ConcurrentDictionary<string, DateTime> _suspendedUntil = new();
        private readonly TimeSpan _suspendDuration = TimeSpan.FromSeconds(2);

        public event EventHandler<FileChangedEventArgs>? FileChanged;
        public event EventHandler<FileChangedEventArgs>? FileCreated;
        public event EventHandler<FileChangedEventArgs>? FileDeleted;
        public event EventHandler<FileRenamedEventArgs>? FileRenamed;

        public FileWatcherService(IAppLogger? logger = null, ConfigurationService? config = null)
        {
            _watchers = new Dictionary<string, FileSystemWatcher>();
            _logger = logger ?? AppLogger.Instance;
            _config = config;
            try
            {
                var s = config?.Settings;
                var ms = s?.FileWatcherDebounceMs > 0 ? s.FileWatcherDebounceMs : 500;
                _debounceInterval = TimeSpan.FromMilliseconds(ms);
            }
            catch
            {
                _debounceInterval = TimeSpan.FromMilliseconds(500);
            }
        }

        public void StartWatching(string path, string filter = "*.txt", bool includeSubdirectories = false)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FileWatcherService));

            try
            {
                // Validate path
                if (!Directory.Exists(path))
                {
                    _logger.Warning($"Cannot watch non-existent directory: {path}");
                    return;
                }

                // Check if already watching this path
                if (_watchers.ContainsKey(path))
                {
                    _logger.Debug($"Already watching path: {path}");
                    return;
                }

                var watcher = new FileSystemWatcher(path)
                {
                    Filter = filter,
                    NotifyFilter = NotifyFilters.LastWrite | 
                                   NotifyFilters.FileName | 
                                   NotifyFilters.DirectoryName,
                    IncludeSubdirectories = includeSubdirectories,
                    EnableRaisingEvents = true
                };

                // Set buffer size to handle many changes (configurable)
                try
                {
                    var bufferKB = _config?.Settings?.FileWatcherBufferKB ?? 64;
                    if (bufferKB < 4) bufferKB = 4; // minimum supported
                    watcher.InternalBufferSize = bufferKB * 1024;
                }
                catch
                {
                    watcher.InternalBufferSize = 64 * 1024;
                }

                watcher.Changed += OnFileChanged;
                watcher.Created += OnFileCreated;
                watcher.Deleted += OnFileDeleted;
                watcher.Renamed += OnFileRenamed;
                watcher.Error += OnWatcherError;

                _watchers[path] = watcher;
                _logger.Info($"Started watching: {path} (recursive: {includeSubdirectories})");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to start watching path: {path}");
                throw;
            }
        }

        public void StopWatching(string path)
        {
            if (_disposed) return;

            try
            {
                if (_watchers.TryGetValue(path, out var watcher))
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Changed -= OnFileChanged;
                    watcher.Created -= OnFileCreated;
                    watcher.Deleted -= OnFileDeleted;
                    watcher.Renamed -= OnFileRenamed;
                    watcher.Error -= OnWatcherError;
                    watcher.Dispose();
                    _watchers.Remove(path);
                    
                    _logger.Info($"Stopped watching: {path}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error stopping watcher for path: {path}");
            }
        }

        public void StopAllWatchers()
        {
            var paths = new List<string>(_watchers.Keys);
            foreach (var path in paths)
            {
                StopWatching(path);
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                // HYBRID ENHANCEMENT: Check if file path is suspended (during saves)
                if (IsSuspended(e.FullPath))
                {
                    _logger.Debug($"Skipping file change event for suspended path: {e.FullPath}");
                    return;
                }

                // Debounce rapid successive events for the same path
                lock (_lastEventTimes)
                {
                    if (_lastEventTimes.TryGetValue(e.FullPath, out var lastTime))
                    {
                        if (DateTime.Now - lastTime < _debounceInterval)
                        {
                            return;
                        }
                    }
                    _lastEventTimes[e.FullPath] = DateTime.Now;
                }

                _logger.Debug($"File changed: {e.FullPath}");
                // Marshal handler to thread pool to avoid any potential UI thread dependency in subscribers
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    try { FileChanged?.Invoke(this, new FileChangedEventArgs(e.FullPath, e.ChangeType)); }
                    catch (Exception cbEx) { _logger.Warning($"FileChanged handler error: {cbEx.Message}"); }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling file change event for: {e.FullPath}");
            }
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                _logger.Debug($"File created: {e.FullPath}");
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    try { FileCreated?.Invoke(this, new FileChangedEventArgs(e.FullPath, e.ChangeType)); }
                    catch (Exception cbEx) { _logger.Warning($"FileCreated handler error: {cbEx.Message}"); }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling file creation event for: {e.FullPath}");
            }
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            try
            {
                _logger.Debug($"File deleted: {e.FullPath}");
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    try { FileDeleted?.Invoke(this, new FileChangedEventArgs(e.FullPath, e.ChangeType)); }
                    catch (Exception cbEx) { _logger.Warning($"FileDeleted handler error: {cbEx.Message}"); }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling file deletion event for: {e.FullPath}");
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            try
            {
                _logger.Debug($"File renamed: {e.OldFullPath} -> {e.FullPath}");
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    try { FileRenamed?.Invoke(this, new FileRenamedEventArgs(e.OldFullPath, e.FullPath)); }
                    catch (Exception cbEx) { _logger.Warning($"FileRenamed handler error: {cbEx.Message}"); }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling file rename event: {e.OldFullPath} -> {e.FullPath}");
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            var ex = e.GetException();
            _logger?.Error(ex, "FileSystemWatcher error occurred");
            
            // Don't attempt restart if we're disposing
            if (_disposed)
                return;
            
            // Attempt to restart the watcher
            if (sender is FileSystemWatcher watcher)
            {
                var path = watcher.Path;
                var filter = watcher.Filter;
                var includeSub = watcher.IncludeSubdirectories;
                
                _logger?.Info($"Attempting to restart watcher for: {path}");
                
                // Use async delay instead of blocking thread sleep
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        if (!_disposed)
                        {
                            StopWatching(path);
                            await System.Threading.Tasks.Task.Delay(1000); // Brief pause
                            if (!_disposed)
                            {
                                StartWatching(path, filter, includeSub);
                            }
                        }
                    }
                    catch (Exception restartEx)
                    {
                        _logger?.Warning($"Failed to restart watcher for {path}: {restartEx.Message}");
                    }
                });
            }
        }

        /// <summary>
        /// HYBRID ENHANCEMENT: Suspend file watching for a specific path
        /// Used during save operations to prevent false external change detection
        /// </summary>
        public void SuspendPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || _disposed)
                return;

            var normalizedPath = filePath.ToLowerInvariant();
            _suspendedUntil[normalizedPath] = DateTime.UtcNow.Add(_suspendDuration);
            _logger.Debug($"Suspended file watching: {filePath} for {_suspendDuration.TotalSeconds}s");
        }

        /// <summary>
        /// HYBRID ENHANCEMENT: Resume file watching for a specific path
        /// </summary>
        public void ResumePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || _disposed)
                return;

            var normalizedPath = filePath.ToLowerInvariant();
            _suspendedUntil.TryRemove(normalizedPath, out _);
            _logger.Debug($"Resumed file watching: {filePath}");
        }

        /// <summary>
        /// HYBRID ENHANCEMENT: Check if a file path is currently suspended
        /// </summary>
        private bool IsSuspended(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            var normalizedPath = filePath.ToLowerInvariant();
            if (_suspendedUntil.TryGetValue(normalizedPath, out var suspendUntil))
            {
                if (DateTime.UtcNow < suspendUntil)
                {
                    return true; // Still suspended
                }
                else
                {
                    // Expired suspension - clean up
                    _suspendedUntil.TryRemove(normalizedPath, out _);
                }
            }
            return false;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _logger?.Info("Disposing FileWatcherService");
                    try
                    {
                        // Use timeout to prevent hanging during shutdown
                        var timeoutTask = System.Threading.Tasks.Task.Delay(3000);
                        var disposeTask = System.Threading.Tasks.Task.Run(() => {
                            StopAllWatchers();
                            _watchers.Clear();
                        });
                        
                        var completedTask = System.Threading.Tasks.Task.WaitAny(disposeTask, timeoutTask);
                        if (completedTask == 1) // timeout occurred
                        {
                            _logger?.Warning("FileWatcherService disposal timed out - forcing cleanup");
                            // Force clear watchers without proper disposal
                            try
                            {
                                foreach (var kvp in _watchers.ToList())
                                {
                                    try
                                    {
                                        kvp.Value?.Dispose();
                                    }
                                    catch
                                    {
                                        // Ignore disposal errors during forced cleanup
                                    }
                                }
                                _watchers.Clear();
                            }
                            catch
                            {
                                // Final safety net
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"Error during FileWatcherService disposal: {ex.Message}");
                        // Try to clear watchers anyway
                        try
                        {
                            _watchers.Clear();
                        }
                        catch
                        {
                            // Ignore final cleanup errors
                        }
                    }
                }
                _disposed = true;
            }
        }
    }

    // Keep existing event args classes
    public class FileChangedEventArgs : EventArgs
    {
        public string FilePath { get; }
        public WatcherChangeTypes ChangeType { get; }

        public FileChangedEventArgs(string filePath, WatcherChangeTypes changeType)
        {
            FilePath = filePath;
            ChangeType = changeType;
        }
    }

    public class FileRenamedEventArgs : EventArgs
    {
        public string OldPath { get; }
        public string NewPath { get; }

        public FileRenamedEventArgs(string oldPath, string newPath)
        {
            OldPath = oldPath;
            NewPath = newPath;
        }
    }
}