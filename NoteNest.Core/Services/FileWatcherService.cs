using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoteNest.Core.Services
{
    public class FileWatcherService : IDisposable
    {
        private readonly Dictionary<string, FileSystemWatcher> _watchers;
        
        public event EventHandler<FileChangedEventArgs> FileChanged;
        public event EventHandler<FileChangedEventArgs> FileCreated;
        public event EventHandler<FileChangedEventArgs> FileDeleted;
        public event EventHandler<FileRenamedEventArgs> FileRenamed;

        public FileWatcherService()
        {
            _watchers = new Dictionary<string, FileSystemWatcher>();
        }

        public void StartWatching(string path, string filter = "*.txt")
        {
            if (_watchers.ContainsKey(path))
                return;

            var watcher = new FileSystemWatcher(path)
            {
                Filter = filter,
                NotifyFilter = NotifyFilters.LastWrite | 
                               NotifyFilters.FileName | 
                               NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileCreated;
            watcher.Deleted += OnFileDeleted;
            watcher.Renamed += OnFileRenamed;

            _watchers[path] = watcher;
        }

        public void StopWatching(string path)
        {
            if (_watchers.TryGetValue(path, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _watchers.Remove(path);
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            FileChanged?.Invoke(this, new FileChangedEventArgs(e.FullPath, e.ChangeType));
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            FileCreated?.Invoke(this, new FileChangedEventArgs(e.FullPath, e.ChangeType));
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            FileDeleted?.Invoke(this, new FileChangedEventArgs(e.FullPath, e.ChangeType));
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            FileRenamed?.Invoke(this, new FileRenamedEventArgs(e.OldFullPath, e.FullPath));
        }

        public void Dispose()
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();
        }
    }

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
