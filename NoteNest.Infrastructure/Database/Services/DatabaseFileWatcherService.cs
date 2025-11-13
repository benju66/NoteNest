using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Memory;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.Database.Services
{
    /// <summary>
    /// File system watcher that automatically syncs database with file changes.
    /// Maintains database-file system consistency for lightning-fast tree view.
    /// </summary>
    public interface IDatabaseFileWatcherService
    {
        void Start();
        void Stop();
        void InvalidateCache();
    }

    public class DatabaseFileWatcherService : IDatabaseFileWatcherService, IHostedService, IDisposable
    {
        private readonly ITreeDatabaseRepository _repository;
        private readonly IMemoryCache _cache;
        private readonly IAppLogger _logger;
        private readonly string _watchPath;
        private FileSystemWatcher _watcher;
        private Timer _debounceTimer;
        private DateTime _lastChange = DateTime.MinValue;
        private bool _isProcessing = false;

        public DatabaseFileWatcherService(
            ITreeDatabaseRepository repository,
            IMemoryCache cache,
            IAppLogger logger,
            string watchPath)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _watchPath = watchPath ?? throw new ArgumentNullException(nameof(watchPath));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Start();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stop();
            return Task.CompletedTask;
        }

        public void Start()
        {
            try
            {
                if (!Directory.Exists(_watchPath))
                {
                    _logger.Warning($"Watch path does not exist: {_watchPath}");
                    return;
                }

                _watcher = new FileSystemWatcher(_watchPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | 
                                  NotifyFilters.DirectoryName | 
                                  NotifyFilters.LastWrite | 
                                  NotifyFilters.Size
                };

                // Subscribe to events
                _watcher.Created += OnFileSystemChanged;
                _watcher.Deleted += OnFileSystemChanged;
                _watcher.Changed += OnFileSystemChanged;
                _watcher.Renamed += OnFileSystemRenamed;

                // Initialize debounce timer
                _debounceTimer = new Timer(ProcessPendingChanges, null, 
                    Timeout.Infinite, Timeout.Infinite);

                _watcher.EnableRaisingEvents = true;
                _logger.Info($"🔍 Database file watcher started for: {_watchPath}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to start file watcher for: {_watchPath}");
            }
        }

        public void Stop()
        {
            try
            {
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Dispose();
                    _watcher = null;
                }

                if (_debounceTimer != null)
                {
                    _debounceTimer.Dispose();
                    _debounceTimer = null;
                }

                _logger.Info("🔍 Database file watcher stopped");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping file watcher");
            }
        }

        public void InvalidateCache()
        {
            try
            {
                if (_cache is MemoryCache memoryCache)
                {
                    // Clear all tree-related cache entries
                    memoryCache.Remove("category_tree_hierarchy");
                    memoryCache.Remove("root_categories");
                    
                    _logger.Debug("🗑️ Tree cache invalidated due to file system changes");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to invalidate cache");
            }
        }

        // =============================================================================
        // PRIVATE EVENT HANDLERS
        // =============================================================================

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Filter for note files and directories only
                if (IsRelevantChange(e.FullPath))
                {
                    _lastChange = DateTime.Now;
                    _logger.Debug($"📝 File system change detected: {e.ChangeType} - {e.FullPath}");
                    
                    // Debounce - wait 2 seconds after last change to coalesce rapid saves
                    // Reduced from 1s to 2s to prevent excessive database refreshes
                    _debounceTimer?.Change(2000, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling file system change: {e.FullPath}");
            }
        }

        private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            try
            {
                if (IsRelevantChange(e.FullPath) || IsRelevantChange(e.OldFullPath))
                {
                    _lastChange = DateTime.Now;
                    _logger.Debug($"📝 File renamed: {e.OldFullPath} → {e.FullPath}");
                    
                    // Debounce - wait 2 seconds after last change to coalesce rapid saves
                    // Reduced from 1s to 2s to prevent excessive database refreshes
                    _debounceTimer?.Change(2000, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling file rename: {e.OldFullPath} → {e.FullPath}");
            }
        }

        private async void ProcessPendingChanges(object state)
        {
            if (_isProcessing)
            {
                _logger.Debug("File change processing already in progress, skipping");
                return;
            }

            try
            {
                _isProcessing = true;
                _logger.Info("🔄 Processing file system changes...");

                // Step 1: Invalidate all cached data immediately
                InvalidateCache();

                // Step 2: Refresh database metadata from file system
                await _repository.RefreshAllNodeMetadataAsync();

                _logger.Info("✅ Database synchronized with file system changes");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Failed to process file system changes");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private bool IsRelevantChange(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                    return false;

                var fileName = Path.GetFileName(filePath);
                var extension = Path.GetExtension(filePath);

                // Skip hidden files and system files
                if (fileName.StartsWith(".") || fileName.StartsWith("~"))
                    return false;

                // Include directories (for category changes)
                if (Directory.Exists(filePath))
                    return true;

                // Include note files
                var relevantExtensions = new[] { ".txt", ".rtf", ".md" };
                return relevantExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
