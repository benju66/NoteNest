using System;

namespace NoteNest.Core.Models.Search
{
    /// <summary>
    /// Configuration options for FTS5 search queries
    /// </summary>
    public class SearchOptions
    {
        /// <summary>
        /// Maximum number of results to return (default: 50)
        /// </summary>
        public int MaxResults { get; set; } = 50;

        /// <summary>
        /// Filter results to specific category (null = all categories)
        /// </summary>
        public string? CategoryFilter { get; set; }

        /// <summary>
        /// Only include notes modified after this date (null = no filter)
        /// </summary>
        public DateTime? ModifiedAfter { get; set; }

        /// <summary>
        /// Only include notes modified before this date (null = no filter)
        /// </summary>
        public DateTime? ModifiedBefore { get; set; }

        /// <summary>
        /// Whether to include full content in results (default: true)
        /// Set to false for performance when only titles/snippets needed
        /// </summary>
        public bool IncludeContent { get; set; } = true;

        /// <summary>
        /// Whether to generate highlighted snippets (default: true)
        /// Uses FTS5 snippet() function to highlight search terms
        /// </summary>
        public bool HighlightSnippets { get; set; } = true;

        /// <summary>
        /// How to sort the search results (default: Relevance)
        /// </summary>
        public SearchSortOrder SortOrder { get; set; } = SearchSortOrder.Relevance;

        /// <summary>
        /// Number of words to include before/after matches in snippets (default: 8)
        /// </summary>
        public int SnippetContextWords { get; set; } = 8;

        /// <summary>
        /// Maximum number of snippets per result (default: 3)
        /// </summary>
        public int MaxSnippets { get; set; } = 3;

        /// <summary>
        /// Minimum file size to include (0 = no minimum)
        /// </summary>
        public long MinFileSize { get; set; } = 0;

        /// <summary>
        /// Maximum file size to include (0 = no maximum)
        /// </summary>
        public long MaxFileSize { get; set; } = 0;

        /// <summary>
        /// Create default search options for typical queries
        /// </summary>
        public static SearchOptions Default => new();

        /// <summary>
        /// Create options optimized for real-time search (fast results)
        /// </summary>
        public static SearchOptions RealTime => new()
        {
            MaxResults = 20,
            IncludeContent = false,
            HighlightSnippets = true,
            SortOrder = SearchSortOrder.Relevance,
            MaxSnippets = 1,
            SnippetContextWords = 6
        };

        /// <summary>
        /// Create options for comprehensive search (detailed results)
        /// </summary>
        public static SearchOptions Comprehensive => new()
        {
            MaxResults = 100,
            IncludeContent = true,
            HighlightSnippets = true,
            SortOrder = SearchSortOrder.Relevance,
            MaxSnippets = 5,
            SnippetContextWords = 12
        };

        /// <summary>
        /// Create options for category-filtered search
        /// </summary>
        public static SearchOptions ForCategory(string categoryId) => new()
        {
            CategoryFilter = categoryId,
            MaxResults = 50,
            SortOrder = SearchSortOrder.LastModified
        };

        /// <summary>
        /// Validate search options and apply constraints
        /// </summary>
        public void Validate()
        {
            MaxResults = Math.Max(1, Math.Min(MaxResults, 1000)); // 1-1000 range
            SnippetContextWords = Math.Max(1, Math.Min(SnippetContextWords, 50)); // 1-50 range  
            MaxSnippets = Math.Max(1, Math.Min(MaxSnippets, 10)); // 1-10 range

            // Date range validation
            if (ModifiedAfter.HasValue && ModifiedBefore.HasValue && ModifiedAfter > ModifiedBefore)
            {
                var temp = ModifiedAfter;
                ModifiedAfter = ModifiedBefore;
                ModifiedBefore = temp;
            }

            // File size validation
            if (MinFileSize < 0) MinFileSize = 0;
            if (MaxFileSize < 0) MaxFileSize = 0;
            if (MinFileSize > 0 && MaxFileSize > 0 && MinFileSize > MaxFileSize)
            {
                var temp = MinFileSize;
                MinFileSize = MaxFileSize;
                MaxFileSize = temp;
            }
        }
    }

    /// <summary>
    /// Sort order for search results
    /// </summary>
    public enum SearchSortOrder
    {
        /// <summary>
        /// Sort by relevance score (BM25 + usage + recency)
        /// Best matches first
        /// </summary>
        Relevance = 0,

        /// <summary>
        /// Sort by last modification date
        /// Most recently modified first
        /// </summary>
        LastModified = 1,

        /// <summary>
        /// Sort by usage count (popularity)
        /// Most frequently accessed first
        /// </summary>
        Usage = 2,

        /// <summary>
        /// Sort by title alphabetically
        /// A-Z order
        /// </summary>
        Title = 3,

        /// <summary>
        /// Sort by creation date
        /// Most recently created first
        /// </summary>
        CreatedDate = 4,

        /// <summary>
        /// Sort by file size
        /// Largest files first
        /// </summary>
        FileSize = 5
    }

    /// <summary>
    /// Search statistics and performance metrics
    /// </summary>
    public class SearchStatistics
    {
        /// <summary>
        /// Total number of documents in the search index
        /// </summary>
        public int TotalDocuments { get; set; }

        /// <summary>
        /// Number of results found for the query
        /// </summary>
        public int ResultsFound { get; set; }

        /// <summary>
        /// Time taken to execute the search query (milliseconds)
        /// </summary>
        public double SearchTimeMs { get; set; }

        /// <summary>
        /// Database file size in bytes
        /// </summary>
        public long DatabaseSizeBytes { get; set; }

        /// <summary>
        /// Last time the index was optimized
        /// </summary>
        public DateTime LastOptimized { get; set; }

        /// <summary>
        /// Number of FTS5 segments (for optimization monitoring)
        /// </summary>
        public int IndexSegments { get; set; }
    }

    /// <summary>
    /// Progress information for index rebuild operations
    /// </summary>
    public class IndexingProgress
    {
        /// <summary>
        /// Number of files processed
        /// </summary>
        public int Processed { get; set; }

        /// <summary>
        /// Total number of files to process
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// Currently processing file path
        /// </summary>
        public string? CurrentFile { get; set; }

        /// <summary>
        /// Processing stage description
        /// </summary>
        public string Stage { get; set; } = "Processing";

        /// <summary>
        /// Percentage complete (0-100)
        /// </summary>
        public double PercentComplete => Total > 0 ? (double)Processed / Total * 100 : 0;

        /// <summary>
        /// Whether the operation is complete
        /// </summary>
        public bool IsComplete => Processed >= Total;
    }
}
