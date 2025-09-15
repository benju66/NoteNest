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
        Task<bool> ForceRebuildAsync();  // For RTF support and stale index recovery
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
                    
                    // Enhanced validation: Check if RTF files exist but missing from index
                    bool indexValid = await _persistence.ValidateIndexAsync(persistedIndex, rootPath);
                    bool hasRTFFiles = false;
                    bool indexHasRTF = false;
                    
                    try
                    {
                        // Check for RTF files on disk
                        var rtfFiles = Directory.GetFiles(rootPath, "*.rtf", SearchOption.AllDirectories);
                        hasRTFFiles = rtfFiles.Length > 0;
                        
                        // Check if index contains RTF files
                        var rtfEntries = persistedIndex.Entries.Where(e => e.RelativePath.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase)).ToList();
                        indexHasRTF = rtfEntries.Any();
                        
                        // BULLETPROOF: Check for corrupted RTF previews with raw formatting codes
                        bool rtfPreviewsCorrupted = false;
                        if (indexHasRTF)
                        {
                            foreach (var rtfEntry in rtfEntries.Take(5)) // Check first 5 RTF entries as sample
                            {
                                if (!string.IsNullOrEmpty(rtfEntry.ContentPreview) && 
                                    (rtfEntry.ContentPreview.StartsWith("{\\rtf1") || 
                                     rtfEntry.ContentPreview.Contains("\\ansi") ||
                                     rtfEntry.ContentPreview.Contains("\\deff0") ||
                                     rtfEntry.ContentPreview.Contains("\\fonttbl")))
                                {
                                    _logger?.Info($"Detected corrupted RTF preview in cached index for: {rtfEntry.Title}");
                                    rtfPreviewsCorrupted = true;
                                    break;
                                }
                            }
                        }
                        
                        if (hasRTFFiles && !indexHasRTF)
                        {
                            _logger?.Info($"Found {rtfFiles.Length} RTF files on disk but none in index - forcing rebuild for RTF support");
                            indexValid = false;
                        }
                        else if (rtfPreviewsCorrupted)
                        {
                            _logger?.Info("Found RTF files with corrupted previews (raw formatting codes) - forcing rebuild for clean previews");
                            indexValid = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Debug($"RTF file detection failed: {ex.Message}");
                    }
                    
                    if (indexValid)
                    {
                        _logger?.Info("Loaded valid persisted index, restoring content...");
                        await _searchIndex.LoadFromPersistedAsync(persistedIndex);
                        _isIndexBuilt = true;
                        
                        // If previews are empty, trigger background content load
                        if (persistedIndex.Entries.Any(e => string.IsNullOrEmpty(e.ContentPreview)))
                        {
                            _logger?.Info("Some previews missing, loading content in background");
                            _ = Task.Run(async () => await UpdateIndexForModifiedFiles(persistedIndex));
                        }
                        
                        return true;
                    }
                    else
                    {
                        _logger?.Info("Index validation failed or RTF support needed - rebuilding");
                    }
                }
                
                // Fall back to full index build
                _logger?.Info("Building fresh search index...");
                var success = await BuildIndexAsync();
                
                if (success)
                {
                    // Wait for content to load (with timeout)
                    _logger?.Info("Waiting for content to load...");
                    var contentLoadTask = _searchIndex.WaitForContentAsync();
                    var timeoutTask = Task.Delay(5000); // 5 second timeout
                    
                    var completedTask = await Task.WhenAny(contentLoadTask, timeoutTask);
                    if (completedTask == timeoutTask)
                    {
                        _logger?.Warning("Content loading timed out after 5 seconds, search will use partial index");
                    }
                    else
                    {
                        _logger?.Info("Content loading completed");
                    }
                    
                    // Persist the index regardless
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
            // Validate minimum query length
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            {
                _logger?.Debug($"Query too short: '{query}'");
                return new List<SearchResultViewModel>();
            }
            
            query = query.Trim();
            
            // Check cache first
            if (_searchCache.TryGetValue(query, out var cached))
            {
                _logger?.Debug($"Cache hit for query: {query}");
                return cached;
            }
            
            await _searchLock.WaitAsync(cancellationToken);
            try
            {
                // If index isn't ready, use fallback search
                if (!_isIndexBuilt || !_searchIndex.IsContentLoaded)
                {
                    _logger?.Warning($"Index not ready (built={_isIndexBuilt}, content={_searchIndex.IsContentLoaded}), using fallback search");
                    return await FallbackFileSearchAsync(query, cancellationToken);
                }
                
                var results = await SearchInternalAsync(query, cancellationToken);
                
                // Update cache only for fully indexed results
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
                ResultType = string.IsNullOrEmpty(r.NoteId) ? SearchResultType.Category : SearchResultType.Note,
                SearchQuery = query, // Pass the original query for highlighting
                LastModified = File.Exists(r.FilePath) ? File.GetLastWriteTime(r.FilePath) : DateTime.MinValue
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
        /// Fallback search when index isn't ready - searches titles and recent files only
        /// </summary>
        private async Task<List<SearchResultViewModel>> FallbackFileSearchAsync(string query, CancellationToken cancellationToken)
        {
            _logger?.Info($"Performing fallback search for: {query}");
            var results = new List<SearchResultViewModel>();
            
            try
            {
                // Load categories and notes quickly
                var metadataPath = _configService.Settings?.MetadataPath ?? Path.Combine(PathService.ProjectsPath, ".notenest");
                var categories = await _noteService.LoadCategoriesAsync(metadataPath) ?? new List<CategoryModel>();
                
                // Get all notes
                var allNotes = new List<NoteModel>();
                foreach (var category in categories)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    
                    var notes = await _noteService.GetNotesInCategoryAsync(category);
                    if (notes != null)
                    {
                        allNotes.AddRange(notes);
                    }
                }
                
                // Generate word variants for better matching
                var tokens = WordVariantProcessor.TokenizeQuery(query);
                var searchTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var token in tokens)
                {
                    var variants = WordVariantProcessor.GenerateVariants(token);
                    searchTerms.UnionWith(variants);
                }
                
                // Search through notes (title and filename only for speed)
                foreach (var note in allNotes)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    
                    bool matches = false;
                    int score = 0;
                    
                    // Check title
                    if (!string.IsNullOrWhiteSpace(note.Title))
                    {
                        foreach (var term in searchTerms)
                        {
                            if (note.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
                            {
                                matches = true;
                                score += 10; // Title matches are worth more
                                break;
                            }
                        }
                    }
                    
                    // Check filename
                    if (!matches && !string.IsNullOrWhiteSpace(note.FilePath))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(note.FilePath);
                        foreach (var term in searchTerms)
                        {
                            if (fileName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                matches = true;
                                score += 5;
                                break;
                            }
                        }
                    }
                    
                    if (matches)
                    {
                        results.Add(new SearchResultViewModel
                        {
                            NoteId = note.Id,
                            Title = note.Title ?? "Untitled",
                            FilePath = note.FilePath ?? "",
                            Preview = "Content loading...", // Indicate content is being loaded
                            Score = score,
                            LastModified = note.LastModified,
                            ResultType = SearchResultType.Note,
                            SearchQuery = query
                        });
                    }
                }
                
                // Sort by score
                results = results.OrderByDescending(r => r.Score)
                                .ThenByDescending(r => r.LastModified)
                                .Take(20) // Limit results
                                .ToList();
                
                _logger?.Info($"Fallback search found {results.Count} results");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Fallback search failed");
            }
            
            return results;
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

        /// <summary>
        /// Forces a complete search index rebuild - includes RTF files and clears stale index
        /// </summary>
        public async Task<bool> ForceRebuildAsync()
        {
            _logger?.Info("Forcing complete search index rebuild for RTF support");
            
            // Clear in-memory state
            InvalidateIndex();
            
            // Delete persisted index to force fresh build with RTF files
            try
            {
                var rootPath = _configService.Settings?.DefaultNotePath ?? PathService.ProjectsPath;
                var indexPath = Path.Combine(rootPath, ".notenest", "search-index.json");
                if (File.Exists(indexPath))
                {
                    File.Delete(indexPath);
                    _logger?.Info("Deleted stale search index to include RTF files");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to delete old search index: {ex.Message}");
            }
            
            // Force fresh build that will include RTF files
            return await InitializeAsync();
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
            return ext == ".md" || ext == ".txt" || ext == ".rtf";  // BULLETPROOF RTF SUPPORT
        }

        public void Dispose()
        {
            _debouncer?.Dispose();
            _searchCache?.Dispose();
        }
    }

    public class SearchResultViewModel
    {
        private string _searchQuery = string.Empty;
        
        public string NoteId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string CategoryId { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public float Relevance { get; set; }
        public int Score { get; set; }
        public DateTime LastModified { get; set; }
        public SearchResultType ResultType { get; set; }
        
        // Store the search query for highlighting
        public string SearchQuery 
        { 
            get => _searchQuery;
            set => _searchQuery = value ?? string.Empty;
        }
        
        // UI-friendly properties
        public string DisplayTitle => !string.IsNullOrEmpty(Title) ? Title : "Untitled";
        
        public string DisplayPreview 
        {
            get
            {
                if (string.IsNullOrEmpty(Preview))
                    return "No content preview";
                    
                // Return highlighted preview if we have a search query
                if (!string.IsNullOrEmpty(SearchQuery))
                    return HighlightTerms(Preview, SearchQuery);
                    
                return Preview;
            }
        }
        
        public string HighlightedTitle
        {
            get
            {
                if (string.IsNullOrEmpty(Title))
                    return "Untitled";
                    
                if (!string.IsNullOrEmpty(SearchQuery))
                    return HighlightTerms(Title, SearchQuery);
                    
                return Title;
            }
        }
        
        public string ResultIcon => ResultType == SearchResultType.Note ? "📄" : "📁";
        
        /// <summary>
        /// Highlights search terms in text by wrapping them with markdown bold
        /// </summary>
        private string HighlightTerms(string text, string query)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query))
                return text;
            
            try
            {
                // Split query into terms
                var terms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var term in terms)
                {
                    if (term.Length < 2) continue; // Skip single characters
                    
                    // Use regex to wrap matches in bold markdown
                    // (?i) makes it case-insensitive, \b ensures word boundaries
                    var pattern = $@"\b({System.Text.RegularExpressions.Regex.Escape(term)}[a-z]*)\b";
                    text = System.Text.RegularExpressions.Regex.Replace(text, pattern, "**$1**", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
                
                return text;
            }
            catch
            {
                // If highlighting fails, return original text
                return text;
            }
        }
        
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