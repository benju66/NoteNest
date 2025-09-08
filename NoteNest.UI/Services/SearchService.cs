using System;
using System.Collections.Generic;
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
        private readonly SemaphoreSlim _searchLock = new(1, 1);
        
        private DateTime _lastIndexTime = DateTime.MinValue;
        private bool _indexDirty = true;

        public bool IsIndexReady => !_indexDirty;

        public SearchService(
            NoteService noteService,
            ConfigurationService configService, 
            IAppLogger logger)
        {
            _noteService = noteService ?? throw new ArgumentNullException(nameof(noteService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Use existing SearchIndexService with current settings
            var contentWordLimit = _configService.Settings?.SearchIndexContentWordLimit ?? 500;
            _searchIndex = new SearchIndexService(contentWordLimit);
        }

        public async Task<List<SearchResultViewModel>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                _logger.Debug("Search query is empty, returning empty results");
                return new List<SearchResultViewModel>();
            }

            _logger.Debug($"Starting search for query: '{query}'");

            await _searchLock.WaitAsync(cancellationToken);
            try
            {
                // Get all notes for simple search
                var allNotes = await GetAllNotesAsync();
                
                // Use simple search implementation to avoid hanging issue
                _logger.Debug($"Using simple search with query: '{query}' on {allNotes.Count} notes");
                
                var results = new List<SearchIndexService.SearchResult>();
                var queryLower = query.ToLowerInvariant();
                
                // Search through all notes directly
                foreach (var note in allNotes)
                {
                    if (note.Title?.ToLowerInvariant().Contains(queryLower) == true ||
                        note.Content?.ToLowerInvariant().Contains(queryLower) == true)
                    {
                        results.Add(new SearchIndexService.SearchResult
                        {
                            NoteId = note.Id,
                            Title = note.Title,
                            FilePath = note.FilePath,
                            CategoryId = note.CategoryId,
                            Preview = GetPreview(note.Content),
                            Relevance = 1.0f
                        });
                    }
                }
                
                _logger.Debug($"Simple search returned {results.Count} results");
                
                // Log first few results for debugging
                foreach (var result in results.Take(3))
                {
                    _logger.Debug($"  Result: Title='{result.Title}', Preview='{result.Preview?.Substring(0, Math.Min(50, result.Preview?.Length ?? 0))}...', Relevance={result.Relevance}");
                }
                
                // Convert to ViewModels
                var viewModels = results.Select(r => new SearchResultViewModel
                {
                    NoteId = r.NoteId,
                    Title = r.Title,
                    FilePath = r.FilePath,
                    CategoryId = r.CategoryId,
                    Preview = r.Preview,
                    Relevance = r.Relevance,
                    ResultType = string.IsNullOrEmpty(r.NoteId) ? SearchResultType.Category : SearchResultType.Note
                }).ToList();
                
                _logger.Debug($"Returning {viewModels.Count} search results for query: '{query}'");
                return viewModels;
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("Search operation was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Search failed for query: {query}");
                return new List<SearchResultViewModel>();
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
                // Simple suggestion based on recent searches and note titles
                // This is a placeholder - can be enhanced later
                var results = await SearchAsync(query.Trim());
                return results.Take(maxResults).Select(r => r.Title).ToList();
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
            _logger.Debug("Search index invalidated");
        }

        private async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
        {
            if (!_indexDirty && (DateTime.Now - _lastIndexTime).TotalMinutes < 5)
            {
                _logger.Debug("Index is up-to-date, skipping rebuild");
                return; // Index is recent enough
            }

            _logger.Debug("Building search index...");
            
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Get all categories and notes - simplified approach
                var allCategories = await GetAllCategoriesAsync();
                _logger.Debug($"Loaded {allCategories.Count} categories");
                
                var allNotes = await GetAllNotesAsync();
                _logger.Debug($"Loaded {allNotes.Count} notes total");
                
                // Log sample note details
                if (allNotes.Any())
                {
                    var sampleNote = allNotes.First();
                    _logger.Debug($"Sample note: Title='{sampleNote.Title}', Content length={sampleNote.Content?.Length ?? 0}, FilePath='{sampleNote.FilePath}'");
                }
                
                cancellationToken.ThrowIfCancellationRequested();
                
                // Build index using existing logic (run on background thread)
                try
                {
                    _logger.Debug($"About to build index with {allCategories.Count} categories and {allNotes.Count} notes");
                    
                    // Don't use Task.Run to avoid potential deadlock
                    _searchIndex.BuildIndex(allCategories, allNotes);
                    
                    _logger.Debug("Index building completed");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during index building");
                    throw;
                }
                
                _indexDirty = false;
                _lastIndexTime = DateTime.Now;
                
                _logger.Info($"Search index built successfully. {allNotes.Count} notes, {allCategories.Count} categories");
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("Index building was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to build search index");
                throw;
            }
        }

        private async Task<List<CategoryModel>> GetAllCategoriesAsync()
        {
            try
            {
                // Use existing NoteService to get categories
                var metadataPath = _configService.Settings.MetadataPath;
                _logger.Debug($"Loading categories from metadata path: '{metadataPath}'");
                
                var categories = await _noteService.LoadCategoriesAsync(metadataPath) ?? new List<CategoryModel>();
                _logger.Debug($"Successfully loaded {categories.Count} categories");
                
                return categories;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load categories for search index");
                return new List<CategoryModel>();
            }
        }

        private async Task<List<NoteModel>> GetAllNotesAsync()
        {
            try
            {
                var allNotes = new List<NoteModel>();
                var categories = await GetAllCategoriesAsync();
                
                foreach (var category in categories)
                {
                    // Get notes for each category using the correct async method
                    var categoryNotes = await _noteService.GetNotesInCategoryAsync(category);
                    allNotes.AddRange(categoryNotes);
                }
                
                return allNotes;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load notes for search index");
                return new List<NoteModel>();
            }
        }
        
        private string GetPreview(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;
                
            // Take first 150 characters
            var preview = content.Length > 150 
                ? content.Substring(0, 150) + "..." 
                : content;
                
            // Remove newlines for cleaner preview
            return preview.Replace("\r\n", " ").Replace("\n", " ").Trim();
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
