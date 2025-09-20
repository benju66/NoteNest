using System;

namespace NoteNest.Core.Models.Search
{
    /// <summary>
    /// Data Transfer Object for search results
    /// Used to pass search result data from Core to UI without creating circular dependencies
    /// </summary>
    public class SearchResultDto
    {
        /// <summary>
        /// Note unique identifier
        /// </summary>
        public string NoteId { get; set; } = string.Empty;

        /// <summary>
        /// Note title
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// File path for opening the note
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Category identifier
        /// </summary>
        public string CategoryId { get; set; } = string.Empty;

        /// <summary>
        /// Preview text or snippet
        /// </summary>
        public string Preview { get; set; } = string.Empty;

        /// <summary>
        /// Search relevance score (higher = better match)
        /// </summary>
        public int Score { get; set; }

        /// <summary>
        /// Raw relevance from FTS5 (for debugging)
        /// </summary>
        public float Relevance { get; set; }

        /// <summary>
        /// Result type (Note, Category, etc.)
        /// </summary>
        public SearchResultType ResultType { get; set; } = SearchResultType.Note;

        /// <summary>
        /// Original search query that produced this result
        /// </summary>
        public string SearchQuery { get; set; } = string.Empty;

        /// <summary>
        /// Last modification time
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// Highlighted snippet with search terms
        /// </summary>
        public string HighlightedSnippet { get; set; } = string.Empty;
    }

    /// <summary>
    /// Search result type enumeration
    /// </summary>
    public enum SearchResultType
    {
        /// <summary>
        /// Note/document result
        /// </summary>
        Note = 0,

        /// <summary>
        /// Category result
        /// </summary>
        Category = 1,

        /// <summary>
        /// Tag result
        /// </summary>
        Tag = 2
    }
}
