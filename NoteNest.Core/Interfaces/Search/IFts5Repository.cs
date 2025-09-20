using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Core.Models.Search;

namespace NoteNest.Core.Interfaces.Search
{
    /// <summary>
    /// Repository interface for SQLite FTS5 database operations
    /// Single Responsibility: Database CRUD operations for search documents
    /// </summary>
    public interface IFts5Repository : IDisposable
    {
        #region Database Lifecycle

        /// <summary>
        /// Initialize the FTS5 database at the specified path
        /// Creates database and schema if they don't exist
        /// </summary>
        /// <param name="databasePath">Full path to the SQLite database file</param>
        Task InitializeAsync(string databasePath);

        /// <summary>
        /// Check if database exists and has correct schema
        /// </summary>
        /// <param name="databasePath">Database path to validate</param>
        /// <returns>True if database is valid and ready</returns>
        Task<bool> DatabaseExistsAsync(string databasePath);

        /// <summary>
        /// Create new database with FTS5 schema
        /// Overwrites existing database if present
        /// </summary>
        /// <param name="databasePath">Path where database should be created</param>
        Task CreateDatabaseAsync(string databasePath);

        /// <summary>
        /// Get current database file path
        /// </summary>
        string? DatabasePath { get; }

        /// <summary>
        /// Check if repository is properly initialized
        /// </summary>
        bool IsInitialized { get; }

        #endregion

        #region Document Management

        /// <summary>
        /// Index a single document in the FTS5 table
        /// Creates new entry or updates existing one based on note_id
        /// </summary>
        /// <param name="document">Document to index</param>
        Task IndexDocumentAsync(SearchDocument document);

        /// <summary>
        /// Update an existing document in the search index
        /// No-op if document doesn't exist
        /// </summary>
        /// <param name="document">Updated document</param>
        Task UpdateDocumentAsync(SearchDocument document);

        /// <summary>
        /// Remove document from search index by note ID
        /// </summary>
        /// <param name="noteId">Note ID to remove</param>
        Task RemoveDocumentAsync(string noteId);

        /// <summary>
        /// Remove document from search index by file path
        /// Useful when file is deleted but note ID is unknown
        /// </summary>
        /// <param name="filePath">File path to remove</param>
        Task RemoveByFilePathAsync(string filePath);

        /// <summary>
        /// Check if document exists in index
        /// </summary>
        /// <param name="noteId">Note ID to check</param>
        /// <returns>True if document is indexed</returns>
        Task<bool> DocumentExistsAsync(string noteId);

        #endregion

        #region Search Operations

        /// <summary>
        /// Perform FTS5 search with specified query and options
        /// </summary>
        /// <param name="query">FTS5 search query (user input will be processed)</param>
        /// <param name="options">Search configuration options</param>
        /// <returns>List of search results with relevance scoring</returns>
        Task<List<FtsSearchResult>> SearchAsync(string query, SearchOptions? options = null);

        /// <summary>
        /// Get search suggestions for partial queries (autocomplete)
        /// </summary>
        /// <param name="partialQuery">Incomplete search term</param>
        /// <param name="maxResults">Maximum suggestions to return</param>
        /// <returns>List of suggested search terms</returns>
        Task<List<string>> GetSuggestionsAsync(string partialQuery, int maxResults = 10);

        /// <summary>
        /// Search within specific category
        /// </summary>
        /// <param name="query">Search query</param>
        /// <param name="categoryId">Category to limit search to</param>
        /// <param name="maxResults">Maximum results to return</param>
        /// <returns>Filtered search results</returns>
        Task<List<FtsSearchResult>> SearchInCategoryAsync(string query, string categoryId, int maxResults = 50);

        #endregion

        #region Batch Operations

        /// <summary>
        /// Index multiple documents in a single transaction for performance
        /// </summary>
        /// <param name="documents">Documents to index</param>
        Task IndexDocumentsBatchAsync(IEnumerable<SearchDocument> documents);

        /// <summary>
        /// Clear all documents from the search index
        /// </summary>
        Task ClearIndexAsync();

        /// <summary>
        /// Optimize the FTS5 index for better performance
        /// Should be called after large batch operations
        /// </summary>
        Task OptimizeIndexAsync();

        #endregion

        #region Metadata Operations

        /// <summary>
        /// Update usage statistics for a document (for popularity ranking)
        /// </summary>
        /// <param name="noteId">Note ID that was accessed</param>
        Task UpdateUsageStatsAsync(string noteId);

        /// <summary>
        /// Get search statistics and performance metrics
        /// </summary>
        /// <returns>Database and index statistics</returns>
        Task<SearchStatistics> GetStatisticsAsync();

        /// <summary>
        /// Get total number of indexed documents
        /// </summary>
        /// <returns>Document count</returns>
        Task<int> GetDocumentCountAsync();

        /// <summary>
        /// Get database file size in bytes
        /// </summary>
        /// <returns>File size or -1 if error</returns>
        Task<long> GetDatabaseSizeAsync();

        #endregion

        #region Maintenance Operations

        /// <summary>
        /// Rebuild the entire FTS5 index from scratch
        /// Clears existing index and requires re-indexing all documents
        /// </summary>
        Task RebuildIndexAsync();

        /// <summary>
        /// Perform database maintenance (vacuum, analyze, optimize)
        /// Should be called periodically for optimal performance
        /// </summary>
        Task PerformMaintenanceAsync();

        /// <summary>
        /// Validate index integrity and consistency
        /// </summary>
        /// <returns>True if index is healthy</returns>
        Task<bool> ValidateIndexIntegrityAsync();

        #endregion
    }
}
