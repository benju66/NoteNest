using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NoteNest.Core.Configuration;
using NoteNest.Core.Interfaces.Search;
using NoteNest.Core.Models.Search;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Services.Search;
using NoteNest.UI.Interfaces;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// FTS5-based search service replacing the legacy SearchService
    /// Single Responsibility: UI layer search operations and coordination
    /// </summary>
    public class FTS5SearchService : NoteNest.UI.Interfaces.ISearchService
    {
        private readonly IFts5Repository _repository;
        private readonly ISearchResultMapper _mapper;
        private readonly ISearchIndexManager _indexManager;
        private readonly IAppLogger? _logger;
        private readonly ISearchOptions _searchOptions;
        private readonly IStorageOptions _storageOptions;

        private bool _isInitialized = false;

        /// <summary>
        /// Indicates whether the search index is ready for queries
        /// </summary>
        public bool IsIndexReady => _isInitialized && _repository.IsInitialized;

        public FTS5SearchService(
            IFts5Repository repository,
            ISearchResultMapper mapper, 
            ISearchIndexManager indexManager,
            ISearchOptions searchOptions,
            IStorageOptions storageOptions,
            IAppLogger? logger = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _indexManager = indexManager ?? throw new ArgumentNullException(nameof(indexManager));
            _searchOptions = searchOptions ?? throw new ArgumentNullException(nameof(searchOptions));
            _storageOptions = storageOptions ?? throw new ArgumentNullException(nameof(storageOptions));
            _logger = logger;
        }

        #region Service Lifecycle

        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            try
            {
                // Use clean configuration - no complex path resolution needed
                var databasePath = _searchOptions.DatabasePath;
                
                // Initialize repository
                await _repository.InitializeAsync(databasePath);
                
                // Initialize index manager
                var indexManagerSettings = CreateIndexManagerSettings();
                await _indexManager.InitializeAsync(_repository, indexManagerSettings);

                // If database is empty or invalid, trigger initial index build
                var documentCount = await _repository.GetDocumentCountAsync();
                if (documentCount == 0)
                {
                    _logger?.Info("Empty search index detected, starting initial build");
                    _ = Task.Run(async () => await _indexManager.RebuildIndexAsync());
                }

                _isInitialized = true;
                _logger?.Info($"FTS5 Search Service initialized with database: {_searchOptions.DatabasePath}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to initialize FTS5 Search Service");
                throw;
            }
        }

        public async Task ShutdownAsync()
        {
            try
            {
                // Wait for any ongoing indexing to complete
                var timeout = TimeSpan.FromSeconds(10);
                var startTime = DateTime.Now;
                
                while (_indexManager.IsIndexing && (DateTime.Now - startTime) < timeout)
                {
                    await Task.Delay(100);
                }

                if (_indexManager.IsIndexing)
                {
                    _logger?.Warning("Search index manager still running during shutdown");
                }

                _repository?.Dispose();
                _isInitialized = false;
                
                _logger?.Info("FTS5 Search Service shut down");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error during FTS5 Search Service shutdown");
            }
        }

        #endregion

        #region Search Operations

        public async Task<List<SearchResultViewModel>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
                await InitializeAsync();

            if (string.IsNullOrWhiteSpace(query))
                return new List<SearchResultViewModel>();

            // Check for cancellation
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var searchOptions = SearchOptions.RealTime;
                searchOptions.MaxResults = 50; // Default for UI

                var ftsResults = await _repository.SearchAsync(query, searchOptions);
                var dtos = _mapper.MapToDtos(ftsResults, query);
                var viewModels = SearchResultViewModel.FromDtos(dtos);

                // Update usage stats for accessed results (fire and forget)
                _ = Task.Run(async () => 
                {
                    foreach (var result in ftsResults.Take(5)) // Only track top 5 results
                    {
                        try
                        {
                            await _repository.UpdateUsageStatsAsync(result.NoteId);
                        }
                        catch
                        {
                            // Ignore usage stat failures
                        }
                    }
                }, cancellationToken);

                return viewModels;
            }
            catch (OperationCanceledException)
            {
                return new List<SearchResultViewModel>(); // Return empty results on cancellation
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Search failed for query: '{query}'");
                return new List<SearchResultViewModel>();
            }
        }

        public async Task<List<SearchResultViewModel>> SearchInCategoryAsync(string query, string categoryId, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
                await InitializeAsync();

            if (string.IsNullOrWhiteSpace(query))
                return new List<SearchResultViewModel>();

            // Check for cancellation
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var ftsResults = await _repository.SearchInCategoryAsync(query, categoryId, 50);
                var dtos = _mapper.MapToDtos(ftsResults, query);
                var viewModels = SearchResultViewModel.FromDtos(dtos);

                return viewModels;
            }
            catch (OperationCanceledException)
            {
                return new List<SearchResultViewModel>(); // Return empty results on cancellation
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Category search failed for query: '{query}', category: '{categoryId}'");
                return new List<SearchResultViewModel>();
            }
        }

        public async Task<List<string>> GetSearchSuggestionsAsync(string partialQuery, int maxResults = 10)
        {
            if (!_isInitialized)
                await InitializeAsync();

            if (string.IsNullOrWhiteSpace(partialQuery) || partialQuery.Length < 2)
                return new List<string>();

            try
            {
                return await _repository.GetSuggestionsAsync(partialQuery, maxResults);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to get suggestions for: '{partialQuery}'");
                return new List<string>();
            }
        }

        #endregion

        #region Index Management

        public async Task RebuildSearchIndexAsync(IProgress<string>? progress = null)
        {
            if (!_isInitialized)
                await InitializeAsync();

            try
            {
                var indexProgress = new Progress<IndexingProgress>(p => 
                {
                    var message = $"Processing {p.Processed}/{p.Total} files... {p.CurrentFile ?? ""}";
                    progress?.Report(message);
                });

                await _indexManager.RebuildIndexAsync(indexProgress);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to rebuild search index");
                throw;
            }
        }

        public async Task OptimizeSearchIndexAsync()
        {
            if (!_isInitialized)
                await InitializeAsync();

            try
            {
                await _indexManager.OptimizeIndexAsync();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to optimize search index");
                throw;
            }
        }

        public async Task RebuildIndexAsync()
        {
            if (!_isInitialized)
                await InitializeAsync();

            try
            {
                await _indexManager.RebuildIndexAsync();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to rebuild search index");
                throw;
            }
        }

        public async Task<int> GetIndexedDocumentCountAsync()
        {
            if (!_isInitialized)
                await InitializeAsync();

            try
            {
                return await _repository.GetDocumentCountAsync();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to get indexed document count");
                return 0;
            }
        }

        public async Task<long> GetIndexSizeAsync()
        {
            if (!_isInitialized)
                await InitializeAsync();

            try
            {
                return await _repository.GetDatabaseSizeAsync();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to get index size");
                return 0;
            }
        }

        #endregion

        #region File Event Handling

        public async Task HandleNoteCreatedAsync(string filePath)
        {
            if (!_isInitialized)
                await InitializeAsync();

            try
            {
                await _indexManager.HandleFileCreatedAsync(filePath);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to handle note created: {filePath}");
            }
        }

        public async Task HandleNoteUpdatedAsync(string filePath)
        {
            if (!_isInitialized)
                await InitializeAsync();

            try
            {
                await _indexManager.HandleFileModifiedAsync(filePath);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to handle note updated: {filePath}");
            }
        }

        public async Task HandleNoteDeletedAsync(string filePath)
        {
            if (!_isInitialized)
                await InitializeAsync();

            try
            {
                await _indexManager.HandleFileDeletedAsync(filePath);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to handle note deleted: {filePath}");
            }
        }

        public async Task HandleNoteRenamedAsync(string oldPath, string newPath)
        {
            if (!_isInitialized)
                await InitializeAsync();

            try
            {
                await _indexManager.HandleFileRenamedAsync(oldPath, newPath);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to handle note renamed: {oldPath} -> {newPath}");
            }
        }

        #endregion

        #region Statistics and Monitoring

        public async Task<SearchStatistics> GetSearchStatisticsAsync()
        {
            if (!_isInitialized)
                await InitializeAsync();

            try
            {
                return await _repository.GetStatisticsAsync();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to get search statistics");
                return new SearchStatistics();
            }
        }

        public bool IsIndexing()
        {
            return _indexManager.IsIndexing;
        }

        public IndexingProgress? GetIndexingProgress()
        {
            return _indexManager.CurrentProgress;
        }

        #endregion

        #region Compatibility Methods (for legacy interface support)

        // These methods provide compatibility with the existing ISearchService interface
        // TODO: Update ISearchService interface or create adapter pattern

        public async Task LoadSettingsAsync()
        {
            // FTS5 uses AppSettings directly, no separate loading needed
            await Task.CompletedTask;
        }

        public async Task SaveSettingsAsync()
        {
            // FTS5 uses AppSettings directly, no separate saving needed  
            await Task.CompletedTask;
        }

        public void ClearCache()
        {
            // FTS5 doesn't use a memory cache, but we can clear category cache
            _mapper.ClearCategoryCache();
        }

        #endregion

        #region Private Helper Methods

        // GetDatabasePath method removed - using clean _searchOptions.DatabasePath instead

        private IndexManagerSettings CreateIndexManagerSettings()
        {
            return new IndexManagerSettings
            {
                IndexedExtensions = new HashSet<string> { ".rtf" },
                MaxFileSizeBytes = _searchOptions.MaxFileSizeBytes,
                BatchSize = 100,
                FileProcessingTimeoutMs = 30000,
                AutoOptimizeAfterBatch = _searchOptions.AutoOptimizeIndex,
                ExcludedDirectories = new HashSet<string> 
                { 
                    ".git", ".temp", ".wal", ".backup", ".notenest", ".metadata"
                },
                ProcessHiddenFiles = false
            };
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            try
            {
                _repository?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Error disposing FTS5SearchService: {ex.Message}");
            }
        }

        #endregion
    }
}
