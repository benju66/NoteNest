using System;

namespace NoteNest.Core.Configuration
{
    /// <summary>
    /// Clean, focused interface for search-related configuration
    /// Follows Single Responsibility Principle
    /// </summary>
    public interface ISearchOptions
    {
        /// <summary>
        /// Path to the SQLite FTS5 database file
        /// </summary>
        string DatabasePath { get; }

        /// <summary>
        /// Maximum number of search results to return
        /// </summary>
        int MaxResults { get; }

        /// <summary>
        /// Whether to enable real-time index updates
        /// </summary>
        bool RealTimeIndexing { get; }

        /// <summary>
        /// Search debounce delay in milliseconds
        /// </summary>
        int DebounceDelayMs { get; }

        /// <summary>
        /// Maximum file size to index (in bytes)
        /// </summary>
        long MaxFileSizeBytes { get; }

        /// <summary>
        /// Whether to auto-optimize the FTS5 index
        /// </summary>
        bool AutoOptimizeIndex { get; }
    }
}
