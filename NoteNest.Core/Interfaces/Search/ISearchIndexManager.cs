using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Core.Models.Search;

namespace NoteNest.Core.Interfaces.Search
{
    /// <summary>
    /// Manages search index updates and file system integration
    /// Single Responsibility: Coordinate between file system changes and search index
    /// </summary>
    public interface ISearchIndexManager
    {
        #region File System Event Handling

        /// <summary>
        /// Handle new note file creation
        /// Extracts content and adds to search index
        /// </summary>
        /// <param name="filePath">Path to newly created file</param>
        Task HandleFileCreatedAsync(string filePath);

        /// <summary>
        /// Handle note file modification
        /// Re-extracts content and updates search index
        /// </summary>
        /// <param name="filePath">Path to modified file</param>
        Task HandleFileModifiedAsync(string filePath);

        /// <summary>
        /// Handle note file deletion
        /// Removes entry from search index
        /// </summary>
        /// <param name="filePath">Path to deleted file</param>
        Task HandleFileDeletedAsync(string filePath);

        /// <summary>
        /// Handle note file rename/move operation
        /// Updates file path in search index
        /// </summary>
        /// <param name="oldPath">Previous file path</param>
        /// <param name="newPath">New file path</param>
        Task HandleFileRenamedAsync(string oldPath, string newPath);

        #endregion

        #region Bulk Operations

        /// <summary>
        /// Rebuild the entire search index from scratch
        /// Scans all note files and re-indexes them
        /// </summary>
        /// <param name="progress">Optional progress reporter</param>
        Task RebuildIndexAsync(IProgress<IndexingProgress>? progress = null);

        /// <summary>
        /// Rebuild index for specific directory
        /// </summary>
        /// <param name="directoryPath">Directory to scan and index</param>
        /// <param name="progress">Optional progress reporter</param>
        Task RebuildDirectoryAsync(string directoryPath, IProgress<IndexingProgress>? progress = null);

        /// <summary>
        /// Optimize search index for better performance
        /// Should be called after large batch operations
        /// </summary>
        Task OptimizeIndexAsync();

        /// <summary>
        /// Validate search index against file system
        /// Identifies missing, orphaned, or stale entries
        /// </summary>
        /// <returns>Validation report</returns>
        Task<IndexValidationResult> ValidateIndexAsync();

        #endregion

        #region Status and Monitoring

        /// <summary>
        /// Whether index rebuild/update operation is currently in progress
        /// </summary>
        bool IsIndexing { get; }

        /// <summary>
        /// Current indexing progress (if indexing is in progress)
        /// </summary>
        IndexingProgress? CurrentProgress { get; }

        /// <summary>
        /// Get statistics about the search index
        /// </summary>
        /// <returns>Index statistics and metrics</returns>
        Task<IndexStatistics> GetIndexStatisticsAsync();

        /// <summary>
        /// Get list of recently processed files
        /// </summary>
        /// <param name="count">Number of recent files to return</param>
        /// <returns>List of recently indexed file paths</returns>
        Task<List<string>> GetRecentlyProcessedFilesAsync(int count = 10);

        #endregion

        #region Configuration and Setup

        /// <summary>
        /// Initialize index manager with repository and settings
        /// </summary>
        /// <param name="repository">FTS5 repository to use</param>
        /// <param name="settings">Indexing configuration</param>
        Task InitializeAsync(IFts5Repository repository, IndexManagerSettings settings);

        /// <summary>
        /// Update indexing settings at runtime
        /// </summary>
        /// <param name="settings">New settings to apply</param>
        void UpdateSettings(IndexManagerSettings settings);

        /// <summary>
        /// Get current indexing settings
        /// </summary>
        IndexManagerSettings Settings { get; }

        #endregion

        #region Events

        /// <summary>
        /// Raised when indexing operation starts
        /// </summary>
        event EventHandler<IndexingStartedEventArgs>? IndexingStarted;

        /// <summary>
        /// Raised when indexing progress updates
        /// </summary>
        event EventHandler<IndexingProgressEventArgs>? IndexingProgress;

        /// <summary>
        /// Raised when indexing operation completes
        /// </summary>
        event EventHandler<IndexingCompletedEventArgs>? IndexingCompleted;

        /// <summary>
        /// Raised when indexing error occurs
        /// </summary>
        event EventHandler<IndexingErrorEventArgs>? IndexingError;

        #endregion
    }

    /// <summary>
    /// Configuration settings for search index manager
    /// </summary>
    public class IndexManagerSettings
    {
        /// <summary>
        /// File extensions to index (e.g., ".rtf", ".txt")
        /// </summary>
        public HashSet<string> IndexedExtensions { get; set; } = new() { ".rtf" };

        /// <summary>
        /// Maximum file size to index (in bytes, 0 = no limit)
        /// </summary>
        public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024; // 50MB default

        /// <summary>
        /// Number of files to process in each batch
        /// </summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>
        /// Timeout for processing individual files (milliseconds)
        /// </summary>
        public int FileProcessingTimeoutMs { get; set; } = 30000; // 30 seconds

        /// <summary>
        /// Whether to automatically optimize index after batch operations
        /// </summary>
        public bool AutoOptimizeAfterBatch { get; set; } = true;

        /// <summary>
        /// Directories to exclude from indexing
        /// </summary>
        public HashSet<string> ExcludedDirectories { get; set; } = new() { ".git", ".temp", ".wal", ".backup" };

        /// <summary>
        /// Whether to process hidden files
        /// </summary>
        public bool ProcessHiddenFiles { get; set; } = false;
    }

    /// <summary>
    /// Result of index validation operation
    /// </summary>
    public class IndexValidationResult
    {
        /// <summary>
        /// Files in index but missing from file system
        /// </summary>
        public List<string> OrphanedEntries { get; set; } = new();

        /// <summary>
        /// Files on disk but missing from index
        /// </summary>
        public List<string> MissingEntries { get; set; } = new();

        /// <summary>
        /// Files where index is older than file modification time
        /// </summary>
        public List<string> StaleEntries { get; set; } = new();

        /// <summary>
        /// Total number of valid entries
        /// </summary>
        public int ValidEntries { get; set; }

        /// <summary>
        /// Whether validation found any issues
        /// </summary>
        public bool HasIssues => OrphanedEntries.Count > 0 || MissingEntries.Count > 0 || StaleEntries.Count > 0;
    }

    /// <summary>
    /// Index statistics and health metrics
    /// </summary>
    public class IndexStatistics
    {
        /// <summary>
        /// Total documents in index
        /// </summary>
        public int TotalDocuments { get; set; }

        /// <summary>
        /// Index database size in bytes
        /// </summary>
        public long DatabaseSizeBytes { get; set; }

        /// <summary>
        /// Last time index was rebuilt
        /// </summary>
        public DateTime LastRebuild { get; set; }

        /// <summary>
        /// Last time index was optimized
        /// </summary>
        public DateTime LastOptimized { get; set; }

        /// <summary>
        /// Number of indexing errors in last operation
        /// </summary>
        public int RecentErrors { get; set; }

        /// <summary>
        /// Average indexing time per document (milliseconds)
        /// </summary>
        public double AverageIndexingTimeMs { get; set; }
    }

    #region Event Arguments

    /// <summary>
    /// Event arguments for indexing started event
    /// </summary>
    public class IndexingStartedEventArgs : EventArgs
    {
        public IndexingStartedEventArgs(int totalFiles, string operation)
        {
            TotalFiles = totalFiles;
            Operation = operation;
            StartTime = DateTime.Now;
        }

        public int TotalFiles { get; }
        public string Operation { get; }
        public DateTime StartTime { get; }
    }

    /// <summary>
    /// Event arguments for indexing progress event
    /// </summary>
    public class IndexingProgressEventArgs : EventArgs
    {
        public IndexingProgressEventArgs(IndexingProgress progress)
        {
            Progress = progress;
        }

        public IndexingProgress Progress { get; }
    }

    /// <summary>
    /// Event arguments for indexing completed event
    /// </summary>
    public class IndexingCompletedEventArgs : EventArgs
    {
        public IndexingCompletedEventArgs(int processedFiles, TimeSpan duration, int errors)
        {
            ProcessedFiles = processedFiles;
            Duration = duration;
            ErrorCount = errors;
        }

        public int ProcessedFiles { get; }
        public TimeSpan Duration { get; }
        public int ErrorCount { get; }
        public bool HasErrors => ErrorCount > 0;
    }

    /// <summary>
    /// Event arguments for indexing error event
    /// </summary>
    public class IndexingErrorEventArgs : EventArgs
    {
        public IndexingErrorEventArgs(string filePath, Exception exception)
        {
            FilePath = filePath;
            Exception = exception;
        }

        public string FilePath { get; }
        public Exception Exception { get; }
    }

    #endregion
}
