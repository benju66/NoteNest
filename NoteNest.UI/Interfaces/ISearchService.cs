using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Interfaces
{
    /// <summary>
    /// UI-layer search service interface that SearchViewModel expects
    /// Provides search functionality with UI-specific return types
    /// </summary>
    public interface ISearchService
    {
        /// <summary>
        /// Indicates whether the search index is ready for queries
        /// </summary>
        bool IsIndexReady { get; }

        /// <summary>
        /// Perform search and return UI-ready ViewModels
        /// </summary>
        /// <param name="query">Search query</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>List of search result view models for UI binding</returns>
        Task<List<SearchResultViewModel>> SearchAsync(string query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Search within a specific category
        /// </summary>
        /// <param name="query">Search query</param>
        /// <param name="categoryId">Category to search within</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Filtered search results</returns>
        Task<List<SearchResultViewModel>> SearchInCategoryAsync(string query, string categoryId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get search suggestions for autocomplete
        /// </summary>
        /// <param name="partialQuery">Partial search query</param>
        /// <param name="maxResults">Maximum suggestions to return</param>
        /// <returns>List of search suggestions</returns>
        Task<List<string>> GetSearchSuggestionsAsync(string partialQuery, int maxResults = 10);

        /// <summary>
        /// Initialize the search service
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Rebuild the search index
        /// </summary>
        Task RebuildIndexAsync();

        /// <summary>
        /// Handle note file creation event
        /// </summary>
        /// <param name="filePath">Path to the created note file</param>
        Task HandleNoteCreatedAsync(string filePath);

        /// <summary>
        /// Handle note file update event
        /// </summary>
        /// <param name="filePath">Path to the updated note file</param>
        Task HandleNoteUpdatedAsync(string filePath);

        /// <summary>
        /// Handle note file deletion event
        /// </summary>
        /// <param name="filePath">Path to the deleted note file</param>
        Task HandleNoteDeletedAsync(string filePath);

        /// <summary>
        /// Get the number of indexed documents
        /// </summary>
        /// <returns>Number of documents in the search index</returns>
        Task<int> GetIndexedDocumentCountAsync();

        /// <summary>
        /// Check if the index is currently being built
        /// </summary>
        /// <returns>True if indexing is in progress</returns>
        bool IsIndexing();

        /// <summary>
        /// Get the current indexing progress
        /// </summary>
        /// <returns>Progress information or null if not indexing</returns>
        NoteNest.Core.Models.Search.IndexingProgress GetIndexingProgress();
    }
}
