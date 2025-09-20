using System;
using System.IO;

namespace NoteNest.Core.Configuration
{
    /// <summary>
    /// Clean, focused search configuration implementation
    /// Immutable with sensible defaults
    /// </summary>
    public class SearchConfigurationOptions : ISearchOptions
    {
        public string DatabasePath { get; init; } = string.Empty;
        public int MaxResults { get; init; } = 50;
        public bool RealTimeIndexing { get; init; } = true;
        public int DebounceDelayMs { get; init; } = 300;
        public long MaxFileSizeBytes { get; init; } = 50 * 1024 * 1024; // 50MB
        public bool AutoOptimizeIndex { get; init; } = true;

        /// <summary>
        /// Create search options from storage metadata path
        /// </summary>
        public static SearchConfigurationOptions FromStoragePath(string metadataPath)
        {
            if (string.IsNullOrWhiteSpace(metadataPath))
            {
                throw new ArgumentException("Metadata path cannot be null or empty", nameof(metadataPath));
            }

            var databasePath = Path.Combine(metadataPath, "search.db");

            return new SearchConfigurationOptions
            {
                DatabasePath = Path.GetFullPath(databasePath)
            };
        }

        /// <summary>
        /// Create search options with custom configuration
        /// </summary>
        public static SearchConfigurationOptions WithSettings(
            string databasePath,
            int maxResults = 50,
            bool realTimeIndexing = true,
            int debounceDelayMs = 300,
            long maxFileSizeBytes = 50 * 1024 * 1024,
            bool autoOptimizeIndex = true)
        {
            return new SearchConfigurationOptions
            {
                DatabasePath = Path.GetFullPath(databasePath),
                MaxResults = Math.Max(1, maxResults),
                RealTimeIndexing = realTimeIndexing,
                DebounceDelayMs = Math.Max(50, debounceDelayMs),
                MaxFileSizeBytes = Math.Max(1024, maxFileSizeBytes),
                AutoOptimizeIndex = autoOptimizeIndex
            };
        }

        /// <summary>
        /// Validate search configuration
        /// </summary>
        public void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(DatabasePath))
            {
                throw new InvalidOperationException("Database path cannot be null or empty");
            }

            if (MaxResults < 1 || MaxResults > 1000)
            {
                throw new InvalidOperationException($"MaxResults must be between 1 and 1000, got: {MaxResults}");
            }

            if (DebounceDelayMs < 50 || DebounceDelayMs > 5000)
            {
                throw new InvalidOperationException($"DebounceDelayMs must be between 50 and 5000, got: {DebounceDelayMs}");
            }

            if (MaxFileSizeBytes < 1024)
            {
                throw new InvalidOperationException($"MaxFileSizeBytes must be at least 1024 bytes, got: {MaxFileSizeBytes}");
            }

            try
            {
                var directory = Path.GetDirectoryName(DatabasePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Cannot access database path '{DatabasePath}': {ex.Message}", ex);
            }
        }
    }
}
