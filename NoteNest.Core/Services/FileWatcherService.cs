using System;
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

        public event EventHandler<FileChangedEventArgs> FileChanged;
        public event EventHandler<FileChangedEventArgs> FileCreated;
        public event EventHandler<FileChangedEventArgs> FileDeleted;
        public event EventHandler<FileRenamedEventArgs> FileRenamed;

        public FileWatcherService(IAppLogger logger = null)
        {
            _watchers = new Dictionary<string, FileSystemWatcher>();
            _logger = logger ?? AppLogger.Instance;
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

                // Set buffer size to handle many changes
                watcher.InternalBufferSize = 64 * 1024; // 64KB buffer

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
                _logger.Debug($"File changed: {e.FullPath}");
                FileChanged?.Invoke(this, new FileChangedEventArgs(e.FullPath, e.ChangeType));
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
                FileCreated?.Invoke(this, new FileChangedEventArgs(e.FullPath, e.ChangeType));
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
                FileDeleted?.Invoke(this, new FileChangedEventArgs(e.FullPath, e.ChangeType));
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
                FileRenamed?.Invoke(this, new FileRenamedEventArgs(e.OldFullPath, e.FullPath));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling file rename event: {e.OldFullPath} -> {e.FullPath}");
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            var ex = e.GetException();
            _logger.Error(ex, "FileSystemWatcher error occurred");
            
            // Attempt to restart the watcher
            if (sender is FileSystemWatcher watcher)
            {
                var path = watcher.Path;
                var filter = watcher.Filter;
                var includeSub = watcher.IncludeSubdirectories;
                
                _logger.Info($"Attempting to restart watcher for: {path}");
                
                StopWatching(path);
                System.Threading.Thread.Sleep(1000); // Brief pause
                StartWatching(path, filter, includeSub);
            }
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
                    _logger.Info("Disposing FileWatcherService");
                    StopAllWatchers();
                    _watchers.Clear();
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