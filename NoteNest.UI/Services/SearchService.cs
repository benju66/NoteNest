using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Services
{
    public interface ISearchService
    {
        Task<List<SearchResultViewModel>> SearchAsync(string query, CancellationToken cancellationToken = default);
        Task<List<string>> GetSuggestionsAsync(string query, int maxResults = 10);
        void InvalidateIndex(); // Called when notes change
        bool IsIndexReady { get; }
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
        private bool _indexDirty = true;
        private DateTime _lastIndexTime = DateTime.MinValue;
        
        // Simple cache for recent searches
        private readonly Dictionary<string, (List<SearchResultViewModel> results, DateTime timestamp)> _searchCache = new();
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromSeconds(30);

        public bool IsIndexReady => _isIndexBuilt && !_indexDirty;

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
            _searchIndex = new SearchIndexService(contentWordLimit);
            
            // Subscribe to file changes for incremental updates
            _fileWatcher.FileChanged += OnFileChanged;
            _fileWatcher.FileCreated += OnFileCreated;
            _fileWatcher.FileDeleted += OnFileDeleted;
            _fileWatcher.FileRenamed += OnFileRenamed;
            
            // Build initial index in background
            _ = Task.Run(async () => await BuildInitialIndexAsync());
        }

        public async Task<List<SearchResultViewModel>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                _logger.Debug("Search query is empty, returning empty results");
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

            // Ensure index is built
            if (!_isIndexBuilt)
            {
                _logger.Info("Index not ready, building now...");
                await BuildInitialIndexAsync();
            }

            _logger.Debug($"Starting search for query: '{query}'");

            await _searchLock.WaitAsync(cancellationToken);
            try
            {
                // USE THE SOPHISTICATED INDEX!
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

        public async Task<List<string>> GetSuggestionsAsync(string query, int maxResults = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<string>();

            try
            {
                // Use the sophisticated search for suggestions
                var results = await SearchAsync(query.Trim());
                return results
                    .OrderByDescending(r => r.Relevance)
                    .Take(maxResults)
                    .Select(r => r.Title)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get search suggestions");
                return new List<string>();
            }
        }

        public void InvalidateIndex()
        {
            _indexDirty = true;
            InvalidateCache();
            _logger.Debug("Search index invalidated");
        }

        private async Task BuildInitialIndexAsync()
        {
            await _indexBuildLock.WaitAsync();
            try
            {
                if (_isIndexBuilt && !_indexDirty) return;
                
                _logger.Info("Building search index in background...");
                var startTime = DateTime.Now;
                
                // Load categories and notes
                var metadataPath = _configService.Settings.MetadataPath;
                var categories = await _noteService.LoadCategoriesAsync(metadataPath) ?? new List<CategoryModel>();
                
                var allNotes = new List<NoteModel>();
                foreach (var category in categories)
                {
                    var categoryNotes = await _noteService.GetNotesInCategoryAsync(category);
                    allNotes.AddRange(categoryNotes);
                }
                
                _logger.Debug($"Loaded {allNotes.Count} notes from {categories.Count} categories");
                
                // Build index on background thread to avoid UI freeze
                await Task.Run(() => _searchIndex.BuildIndex(categories, allNotes));
                
                _isIndexBuilt = true;
                _indexDirty = false;
                _lastIndexTime = DateTime.Now;
                
                var elapsed = DateTime.Now - startTime;
                _logger.Info($"Search index built successfully in {elapsed.TotalSeconds:F2}s: {allNotes.Count} notes indexed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to build search index");
                _isIndexBuilt = false;
            }
            finally
            {
                _indexBuildLock.Release();
            }
        }

        // File watcher event handlers
        private async void OnFileChanged(object sender, FileChangedEventArgs e)
        {
            if (!IsRelevantFile(e.FilePath)) return;
            
            _logger.Debug($"File changed: {e.FilePath}");
            InvalidateCache();
            
            // For now, mark index as dirty and rebuild on next search
            // TODO: Implement incremental update
            _indexDirty = true;
        }

        private async void OnFileCreated(object sender, FileChangedEventArgs e)
        {
            if (!IsRelevantFile(e.FilePath)) return;
            
            _logger.Debug($"File created: {e.FilePath}");
            InvalidateCache();
            _indexDirty = true;
        }

        private async void OnFileDeleted(object sender, FileChangedEventArgs e)
        {
            if (!IsRelevantFile(e.FilePath)) return;
            
            _logger.Debug($"File deleted: {e.FilePath}");
            InvalidateCache();
            _indexDirty = true;
        }

        private async void OnFileRenamed(object sender, FileRenamedEventArgs e)
        {
            if (!IsRelevantFile(e.NewPath)) return;
            
            _logger.Debug($"File renamed: {e.OldPath} -> {e.NewPath}");
            InvalidateCache();
            _indexDirty = true;
        }

        private bool IsRelevantFile(string filePath)
        {
            var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            return ext == ".md" || ext == ".txt" || ext == ".markdown";
        }

        private void InvalidateCache()
        {
            lock (_searchCache)
            {
                _searchCache.Clear();
            }
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
