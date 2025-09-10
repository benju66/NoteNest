using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Services.Search;
using NoteNest.Core.Interfaces.Services;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Services
{
    public interface ISearchService
    {
        Task<List<SearchResultViewModel>> SearchAsync(string query, CancellationToken cancellationToken = default);
        Task<List<string>> GetSuggestionsAsync(string query, int maxResults = 10);
        void InvalidateIndex();
        bool IsIndexReady { get; }
        Task<bool> InitializeAsync();
    }

    public class SearchService : ISearchService
    {
        private readonly SearchIndexService _searchIndex;
        private readonly NoteService _noteService;
        private readonly ConfigurationService _configService;
        private readonly IAppLogger _logger;
        private readonly FileWatcherService _fileWatcher;
        private readonly SearchDebouncer _debouncer;
        private readonly SearchIndexPersistence _persistence;
        
        private readonly SemaphoreSlim _searchLock = new(1, 1);
        private readonly SemaphoreSlim _indexBuildLock = new(1, 1);
        
        private volatile bool _isIndexBuilt = false;
        private volatile bool _isInitializing = false;
        
        // Enhanced cache with LRU and size limits
        private readonly LRUCache<string, List<SearchResultViewModel>> _searchCache;
        private const int MaxCacheEntries = 50;
        private const int CacheExpirySeconds = 30;

        public bool IsIndexReady => _isIndexBuilt;

        public SearchService(
            NoteService noteService,
            ConfigurationService configService,
            FileWatcherService fileWatcher,
            IAppLogger logger)
        {
            _noteService = noteService ?? throw new ArgumentNullException(nameof(noteService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _fileWatcher = fileWatcher ?? throw new ArgumentNullException(nameof(fileWatcher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            var contentWordLimit = _configService.Settings?.SearchIndexContentWordLimit ?? 500;
            
            _searchIndex = new SearchIndexService(
                contentWordLimit,
                new MarkdownService(_logger),
                new DefaultFileSystemProvider(),
                _logger);
            
            // Initialize persistence
            var rootPath = _configService.Settings?.DefaultNotePath ?? PathService.ProjectsPath;
            _persistence = new SearchIndexPersistence(rootPath, _logger);
            
            // Initialize debouncer (2 second delay for file changes)
            _debouncer = new SearchDebouncer(2000, _logger);
            
            // Initialize LRU cache
            _searchCache = new LRUCache<string, List<SearchResultViewModel>>(MaxCacheEntries, CacheExpirySeconds);
            
            // Subscribe to file changes with debouncing
            if (_fileWatcher != null)
            {
                _fileWatcher.FileChanged += OnFileChanged;
                _fileWatcher.FileCreated += OnFileCreated;
                _fileWatcher.FileDeleted += OnFileDeleted;
                _fileWatcher.FileRenamed += OnFileRenamed;
            }
            
            _logger?.Debug("SearchService created with persistence and debouncing");
        }

        /// <summary>
        /// Initializes the search service and builds the initial index
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            if (_isIndexBuilt || _isInitializing)
                return _isIndexBuilt;

            _isInitializing = true;
            try
            {
                _logger?.Info("Initializing search service with persistence...");
                
                // Try to load persisted index first
                var persistedIndex = await _persistence.LoadIndexAsync();
                if (persistedIndex != null)
                {
                    var rootPath = _configService.Settings?.DefaultNotePath ?? PathService.ProjectsPath;
                    if (await _persistence.ValidateIndexAsync(persistedIndex, rootPath))
                    {
                        _logger?.Info("Loaded valid persisted index, performing quick update...");
                        await _searchIndex.LoadFromPersistedAsync(persistedIndex);
                        _isIndexBuilt = true;
                        
                        // Do incremental update in background
                        _ = Task.Run(async () => await UpdateIndexForModifiedFiles(persistedIndex));
                        
                        return true;
                    }
                }
                
                // Fall back to full index build
                _logger?.Info("Building fresh search index...");
                var success = await BuildIndexAsync();
                
                // Persist the new index
                if (success)
                {
                    await PersistCurrentIndexAsync();
                }
                
                return success;
            }
            finally
            {
                _isInitializing = false;
            }
        }

        /// <summary>
        /// Builds or rebuilds the search index
        /// </summary>
        private async Task<bool> BuildIndexAsync()
        {
            await _indexBuildLock.WaitAsync();
            try
            {
                _logger?.Info("Building search index...");
                
                // Clear cache when rebuilding
                _searchCache?.Clear();

                // Load settings and get categories
                await _configService.LoadSettingsAsync();
                var metadataPath = _configService.Settings.MetadataPath;
                var categories = await _noteService.LoadCategoriesAsync(metadataPath) ?? new List<CategoryModel>();
                var allNotes = new List<NoteModel>();

                foreach (var category in categories)
                {
                    try
                    {
                        var notes = await _noteService.GetNotesInCategoryAsync(category);
                        if (notes != null)
                        {
                            allNotes.AddRange(notes);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"Failed to get notes for category {category.Name}: {ex.Message}");
                    }
                }

                _logger?.Debug($"Building index with {allNotes.Count} notes from {categories.Count} categories");

                // Build the index using the new async method
                _isIndexBuilt = await _searchIndex.BuildIndexAsync(categories, allNotes);

                if (_isIndexBuilt)
                {
                    _logger?.Info($"Search index built successfully with {allNotes.Count} notes");
                }
                else
                {
                    _logger?.Error("Failed to build search index");
                }

                return _isIndexBuilt;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Exception during index build");
                _isIndexBuilt = false;
                return false;
            }
            finally
            {
                _indexBuildLock.Release();
            }
        }

        /// <summary>
        /// Performs a search query with word variants and enhanced scoring
        /// </summary>
        public async Task<List<SearchResultViewModel>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<SearchResultViewModel>();
            
            // Check cache first
            if (_searchCache.TryGetValue(query, out var cached))
            {
                _logger?.Debug($"Cache hit for query: {query}");
                return cached;
            }
            
            await _searchLock.WaitAsync(cancellationToken);
            try
            {
                if (!_isIndexBuilt)
                {
                    _logger?.Warning("Search attempted before index ready");
                    return new List<SearchResultViewModel>();
                }
                
                var results = await SearchInternalAsync(query, cancellationToken);
                
                // Update cache
                _searchCache.Set(query, results);
                
                return results;
            }
            finally
            {
                _searchLock.Release();
            }
        }

        private async Task<List<SearchResultViewModel>> SearchInternalAsync(
            string query, 
            CancellationToken cancellationToken)
        {
            // Generate word variants for the query
            var tokens = WordVariantProcessor.TokenizeQuery(query);
            var expandedQuery = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var token in tokens)
            {
                var variants = WordVariantProcessor.GenerateVariants(token);
                foreach (var variant in variants)
                {
                    expandedQuery.Add(variant);
                }
            }
            
            _logger?.Debug($"Searching for: {string.Join(", ", expandedQuery)}");
            
            // Search with expanded query
            var searchTask = _searchIndex.SearchAsync(expandedQuery, cancellationToken);
            var results = await searchTask;
            
            // Score and sort results
            var scoredResults = results.Select(r => new SearchResultViewModel
            {
                NoteId = r.NoteId,
                Title = r.Title ?? "Untitled",
                FilePath = r.FilePath,
                CategoryId = r.CategoryId,
                Preview = r.Preview ?? "",
                Relevance = r.Relevance,
                Score = CalculateScore(r, tokens, expandedQuery),
                ResultType = string.IsNullOrEmpty(r.NoteId) ? SearchResultType.Category : SearchResultType.Note
            })
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.LastModified)
            .ToList();
            
            return scoredResults;
        }

        private int CalculateScore(SearchIndexService.SearchResult result, List<string> originalTokens, HashSet<string> allVariants)
        {
            int score = 0;
            
            // Title matches are worth more
            foreach (var token in originalTokens)
            {
                if (result.Title?.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 10;
            }
            
            // Recent files score higher (assuming we can get LastModified from result)
            // For now, just add base content score
            score += 1;
            
            return score;
        }

        /// <summary>
        /// Gets search suggestions for autocomplete
        /// </summary>
        public async Task<List<string>> GetSuggestionsAsync(string query, int maxResults = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<string>();

            try
            {
                var results = await SearchAsync(query.Trim());
                return results
                    .OrderByDescending(r => r.Score)
                    .Take(maxResults)
                    .Select(r => r.Title)
                    .Distinct()
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to get search suggestions");
                return new List<string>();
            }
        }

        /// <summary>
        /// Invalidates the search index and clears cache
        /// </summary>
        public void InvalidateIndex()
        {
            _searchCache?.Clear();
            _searchIndex.Clear();
            _isIndexBuilt = false;
            _logger?.Debug("Search index invalidated");
        }

        // File watcher event handlers with debouncing
        private void OnFileChanged(object sender, FileChangedEventArgs e)
        {
            if (IsNoteFile(e.FilePath))
            {
                _debouncer.Debounce(e.FilePath, async () =>
                {
                    _searchCache?.Clear(); // Clear cache when files change
                    await _searchIndex.UpdateFileAsync(e.FilePath);
                    await PersistCurrentIndexAsync();
                });
            }
        }

        private void OnFileCreated(object sender, FileChangedEventArgs e)
        {
            if (IsNoteFile(e.FilePath))
            {
                _debouncer.Debounce(e.FilePath, async () =>
                {
                    _searchCache?.Clear();
                    await _searchIndex.AddFileAsync(e.FilePath);
                    await PersistCurrentIndexAsync();
                });
            }
        }

        private void OnFileDeleted(object sender, FileChangedEventArgs e)
        {
            if (IsNoteFile(e.FilePath))
            {
                // No debouncing for deletes - remove immediately
                _searchCache?.Clear();
                _searchIndex.RemoveFile(e.FilePath);
                _ = PersistCurrentIndexAsync();
            }
        }

        private void OnFileRenamed(object sender, FileRenamedEventArgs e)
        {
            if (IsNoteFile(e.NewPath))
            {
                _searchCache?.Clear();
                _searchIndex.RenameFile(e.OldPath, e.NewPath);
                _ = PersistCurrentIndexAsync();
            }
        }

        private async Task PersistCurrentIndexAsync()
        {
            try
            {
                var index = await _searchIndex.ExportForPersistenceAsync();
                await _persistence.SaveIndexAsync(index);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to persist search index: {ex.Message}");
            }
        }

        private async Task UpdateIndexForModifiedFiles(PersistedIndex oldIndex)
        {
            // Compare current files with persisted index and update only changed ones
            // This is a placeholder - could be implemented for even faster startup
            await Task.Delay(1);
        }

        private bool IsNoteFile(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext == ".md" || ext == ".txt";
        }

        public void Dispose()
        {
            _debouncer?.Dispose();
            _searchCache?.Dispose();
        }
    }

    public class SearchResultViewModel
    {
        public string NoteId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string CategoryId { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public float Relevance { get; set; }
        public int Score { get; set; } // NEW: Add scoring for relevance
        public DateTime LastModified { get; set; } // NEW: Add LastModified for sorting
        public SearchResultType ResultType { get; set; }
        
        // UI-friendly properties
        public string DisplayTitle => !string.IsNullOrEmpty(Title) ? Title : "Untitled";
        public string DisplayPreview => !string.IsNullOrEmpty(Preview) ? Preview : "No content preview";
        public string ResultIcon => ResultType == SearchResultType.Note ? "📄" : "📁";
        
        // Override ToString to prevent "SearchResultViewModel" from appearing in search box
        public override string ToString()
        {
            return DisplayTitle;
        }
    }

    public enum SearchResultType
    {
        Note,
        Category
    }

    // LRU Cache implementation
    public class LRUCache<TKey, TValue> : IDisposable
    {
        private class CacheEntry
        {
            public TValue Value { get; set; } = default!;
            public DateTime LastAccessed { get; set; }
            public DateTime Created { get; set; }
        }

        private readonly Dictionary<TKey, CacheEntry> _cache = new();
        private readonly int _maxEntries;
        private readonly int _expirySeconds;
        private readonly object _lock = new();

        public LRUCache(int maxEntries, int expirySeconds)
        {
            _maxEntries = maxEntries;
            _expirySeconds = expirySeconds;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    if ((DateTime.UtcNow - entry.Created).TotalSeconds < _expirySeconds)
                    {
                        entry.LastAccessed = DateTime.UtcNow;
                        value = entry.Value;
                        return true;
                    }
                    else
                    {
                        _cache.Remove(key);
                    }
                }
                value = default!;
                return false;
            }
        }

        public void Set(TKey key, TValue value)
        {
            lock (_lock)
            {
                // Evict LRU if at capacity
                if (_cache.Count >= _maxEntries && !_cache.ContainsKey(key))
                {
                    var lru = _cache.OrderBy(kvp => kvp.Value.LastAccessed).First();
                    _cache.Remove(lru.Key);
                }

                _cache[key] = new CacheEntry
                {
                    Value = value,
                    LastAccessed = DateTime.UtcNow,
                    Created = DateTime.UtcNow
                };
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _cache.Clear();
            }
        }

        public void Dispose()
        {
            Clear();
        }
    }
}