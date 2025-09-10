using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
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
        private readonly SemaphoreSlim _searchLock = new(1, 1);
        private readonly SemaphoreSlim _indexBuildLock = new(1, 1);
        
        // Track index state
        private volatile bool _isIndexBuilt = false;
        private volatile bool _isInitializing = false;
        
        // Simple cache for recent searches
        private readonly Dictionary<string, (List<SearchResultViewModel> results, DateTime timestamp)> _searchCache = new();
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromSeconds(30);

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
            
            // Create search index with proper dependencies
            _searchIndex = new SearchIndexService(
                contentWordLimit,
                new MarkdownService(_logger),
                new DefaultFileSystemProvider(),
                _logger);
            
            // Subscribe to file changes for incremental updates
            if (_fileWatcher != null)
            {
            _fileWatcher.FileChanged += OnFileChanged;
            _fileWatcher.FileCreated += OnFileCreated;
            _fileWatcher.FileDeleted += OnFileDeleted;
            _fileWatcher.FileRenamed += OnFileRenamed;
            }
            
            // Don't build index in constructor - wait for InitializeAsync
            _logger.Debug("SearchService created, waiting for initialization");
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
                _logger.Info("Initializing search service...");
                
                // Wait a moment for the app to fully initialize
                await Task.Delay(500);
                
                // Build the initial index
                var success = await BuildIndexAsync();
                
                if (success)
                {
                    _logger.Info("Search service initialized successfully");
                }
                else
                {
                    _logger.Error("Search service initialization failed");
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
                _logger.Info("Building search index...");
                
                // Clear cache when rebuilding
                lock (_searchCache)
                {
                    _searchCache.Clear();
                }

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
                        _logger.Warning($"Failed to get notes for category {category.Name}: {ex.Message}");
                    }
                }

                _logger.Debug($"Building index with {allNotes.Count} notes from {categories.Count} categories");

                // Build the index using the new async method
                _isIndexBuilt = await _searchIndex.BuildIndexAsync(categories, allNotes);

                if (_isIndexBuilt)
                {
                    _logger.Info($"Search index built successfully with {allNotes.Count} notes");
                }
                else
                {
                    _logger.Error("Failed to build search index");
                }

                return _isIndexBuilt;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception during index build");
                _isIndexBuilt = false;
                return false;
            }
            finally
            {
                _indexBuildLock.Release();
            }
        }

        /// <summary>
        /// Performs a search query
        /// </summary>
        public async Task<List<SearchResultViewModel>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                _logger.Debug("Search query is empty, returning empty results");
                return new List<SearchResultViewModel>();
            }

            // Ensure index is built (with lazy initialization)
            if (!_isIndexBuilt && !_isInitializing)
            {
                _logger.Info("Index not ready, initializing now...");
                await InitializeAsync();
            }

            // If still not built, return empty
            if (!_isIndexBuilt)
            {
                _logger.Warning("Search index not available, returning empty results");
                return new List<SearchResultViewModel>();
            }

            // Check cache first
            var cacheKey = query.ToLowerInvariant();
            lock (_searchCache)
            {
                if (_searchCache.TryGetValue(cacheKey, out var cached))
                {
                    if (DateTime.Now - cached.timestamp < _cacheExpiry)
                    {
                        _logger.Debug($"Cache hit for query: '{query}'");
                        return cached.results;
                    }
                    _searchCache.Remove(cacheKey);
                }
            }

            _logger.Debug($"Starting search for query: '{query}'");

            await _searchLock.WaitAsync(cancellationToken);
            try
            {
                // Perform the search
                var searchResults = await Task.Run(() => 
                    _searchIndex.Search(query, maxResults: 50), 
                    cancellationToken);
                
                _logger.Debug($"Index search returned {searchResults.Count} results");
                
                // Convert to ViewModels
                var viewModels = searchResults.Select(r => new SearchResultViewModel
                {
                    NoteId = r.NoteId,
                    Title = r.Title ?? "Untitled",
                    FilePath = r.FilePath,
                    CategoryId = r.CategoryId,
                    Preview = r.Preview ?? "",
                    Relevance = r.Relevance,
                    ResultType = string.IsNullOrEmpty(r.NoteId) ? SearchResultType.Category : SearchResultType.Note
                }).ToList();
                
                // Cache the results
                lock (_searchCache)
                {
                    _searchCache[cacheKey] = (viewModels, DateTime.Now);
                    
                    // Limit cache size
                    if (_searchCache.Count > 100)
                    {
                        var oldest = _searchCache.OrderBy(kvp => kvp.Value.timestamp).First().Key;
                        _searchCache.Remove(oldest);
                    }
                }
                
                _logger.Debug($"Returning {viewModels.Count} search results for query: '{query}'");
                return viewModels;
            }
            finally
            {
                _searchLock.Release();
            }
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
                    .OrderByDescending(r => r.Relevance)
                    .Take(maxResults)
                    .Select(r => r.Title)
                    .Distinct()
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get search suggestions");
                return new List<string>();
            }
        }

        /// <summary>
        /// Invalidates the search index and clears cache
        /// </summary>
        public void InvalidateIndex()
        {
            _searchIndex.MarkDirty();
            InvalidateCache();
            _logger.Debug("Search index invalidated");
        }

        /// <summary>
        /// Clears the search cache
        /// </summary>
        private void InvalidateCache()
        {
            lock (_searchCache)
            {
                _searchCache.Clear();
            }
        }

        // File watcher event handlers
        private async void OnFileChanged(object sender, FileChangedEventArgs e)
        {
            if (IsNoteFile(e.FilePath))
            {
                _logger.Debug($"Note file changed: {e.FilePath}");
                InvalidateCache();
                // TODO: Implement incremental index update
            }
        }

        private async void OnFileCreated(object sender, FileChangedEventArgs e)
        {
            if (IsNoteFile(e.FilePath))
            {
                _logger.Debug($"Note file created: {e.FilePath}");
                InvalidateCache();
                // TODO: Implement incremental index update
            }
        }

        private async void OnFileDeleted(object sender, FileChangedEventArgs e)
        {
            if (IsNoteFile(e.FilePath))
            {
                _logger.Debug($"Note file deleted: {e.FilePath}");
                InvalidateCache();
                // TODO: Implement incremental index update
            }
        }

        private async void OnFileRenamed(object sender, FileRenamedEventArgs e)
        {
            if (IsNoteFile(e.NewPath) || IsNoteFile(e.OldPath))
            {
                _logger.Debug($"Note file renamed: {e.OldPath} -> {e.NewPath}");
                InvalidateCache();
                // TODO: Implement incremental index update
            }
        }

        private bool IsNoteFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var extension = Path.GetExtension(path)?.ToLowerInvariant();
            return extension == ".md" || extension == ".txt" || extension == ".markdown";
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
}