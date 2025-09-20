using System.Collections.Generic;
using NoteNest.Core.Models;
using NoteNest.Core.Models.Search;

namespace NoteNest.Core.Interfaces.Search
{
    /// <summary>
    /// Maps between FTS5 results and UI ViewModels
    /// Single Responsibility: Data transformation and mapping
    /// </summary>
    public interface ISearchResultMapper
    {
        #region FTS Result to ViewModel Mapping

        /// <summary>
        /// Convert FTS5 search result to SearchResultDto for UI binding
        /// </summary>
        /// <param name="ftsResult">Raw FTS5 search result</param>
        /// <param name="originalQuery">Original user query for highlighting</param>
        /// <returns>UI-ready data transfer object</returns>
        SearchResultDto MapToDto(FtsSearchResult ftsResult, string originalQuery);

        /// <summary>
        /// Convert list of FTS5 results to DTOs
        /// </summary>
        /// <param name="ftsResults">List of FTS5 results</param>
        /// <param name="originalQuery">Original user query</param>
        /// <returns>List of UI-ready data transfer objects</returns>
        List<SearchResultDto> MapToDtos(List<FtsSearchResult> ftsResults, string originalQuery);

        #endregion

        #region NoteModel to SearchDocument Mapping

        /// <summary>
        /// Convert NoteModel to SearchDocument for indexing
        /// </summary>
        /// <param name="note">Source note model</param>
        /// <param name="plainTextContent">Extracted plain text from RTF content</param>
        /// <returns>Search document ready for FTS5 indexing</returns>
        SearchDocument MapFromNoteModel(NoteModel note, string plainTextContent);

        /// <summary>
        /// Convert multiple NoteModels to SearchDocuments
        /// </summary>
        /// <param name="notes">List of note models</param>
        /// <param name="contentExtractor">Function to extract plain text from RTF</param>
        /// <returns>List of search documents</returns>
        List<SearchDocument> MapFromNoteModels(List<NoteModel> notes, System.Func<string, string> contentExtractor);

        #endregion

        #region Score and Preview Processing

        /// <summary>
        /// Calculate composite relevance score from FTS result
        /// Combines BM25, usage count, recency, and other factors
        /// </summary>
        /// <param name="ftsResult">FTS5 result with base scoring</param>
        /// <param name="originalQuery">User query for context-specific scoring</param>
        /// <returns>Composite score (higher = better match)</returns>
        int CalculateCompositeScore(FtsSearchResult ftsResult, string originalQuery);

        /// <summary>
        /// Generate preview text with proper formatting
        /// Uses snippet if available, falls back to content excerpt
        /// </summary>
        /// <param name="ftsResult">FTS5 result with snippet and content</param>
        /// <param name="maxLength">Maximum preview length</param>
        /// <returns>Formatted preview text</returns>
        string GeneratePreview(FtsSearchResult ftsResult, int maxLength = 200);

        /// <summary>
        /// Extract and clean snippet highlighting for UI display
        /// Converts FTS5 <mark> tags to UI-appropriate format
        /// </summary>
        /// <param name="snippet">Raw FTS5 snippet with HTML-like markup</param>
        /// <param name="targetFormat">Target highlight format (e.g., for different UI frameworks)</param>
        /// <returns>Cleaned snippet with appropriate highlighting</returns>
        string ProcessSnippetHighlighting(string snippet, HighlightFormat targetFormat = HighlightFormat.Html);

        #endregion

        #region Utility Methods

        /// <summary>
        /// Determine search result type based on content and metadata
        /// </summary>
        /// <param name="ftsResult">FTS5 result to analyze</param>
        /// <returns>Appropriate result type for UI display</returns>
        SearchResultType DetermineResultType(FtsSearchResult ftsResult);

        /// <summary>
        /// Extract category name from category ID for display
        /// </summary>
        /// <param name="categoryId">Category UUID</param>
        /// <returns>Human-readable category name or default</returns>
        string GetCategoryDisplayName(string categoryId);

        /// <summary>
        /// Format file modification time for UI display
        /// </summary>
        /// <param name="lastModified">Raw modification timestamp</param>
        /// <returns>User-friendly time description</returns>
        string FormatModificationTime(System.DateTime lastModified);

        /// <summary>
        /// Clear the category name cache
        /// </summary>
        void ClearCategoryCache();

        /// <summary>
        /// Update category name in the cache
        /// </summary>
        /// <param name="categoryId">Category UUID</param>
        /// <param name="displayName">Human-readable category name</param>
        void UpdateCategoryName(string categoryId, string displayName);

        #endregion
    }

    /// <summary>
    /// Highlight format options for snippet processing
    /// </summary>
    public enum HighlightFormat
    {
        /// <summary>
        /// HTML markup: &lt;mark&gt;term&lt;/mark&gt;
        /// </summary>
        Html,

        /// <summary>
        /// Plain text with asterisks: *term*
        /// </summary>
        Asterisk,

        /// <summary>
        /// WPF TextBlock with Bold runs
        /// </summary>
        WpfRuns,

        /// <summary>
        /// No highlighting (plain text)
        /// </summary>
        None
    }
}
