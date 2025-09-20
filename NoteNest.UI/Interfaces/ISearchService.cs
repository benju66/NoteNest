using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Services
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
    }
}
