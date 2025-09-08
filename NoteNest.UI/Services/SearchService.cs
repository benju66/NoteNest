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
                return new List<SearchResultViewModel>();

            await _searchLock.WaitAsync(cancellationToken);
            try
            {
                // Ensure index is built
                await EnsureIndexAsync(cancellationToken);
                
                // Use existing SearchIndexService
                var results = await _searchIndex.SearchAsync(query, 50, cancellationToken);
                
                // Convert to ViewModels
                return results.Select(r => new SearchResultViewModel
                {
                    NoteId = r.NoteId,
                    Title = r.Title,
                    FilePath = r.FilePath,
                    CategoryId = r.CategoryId,
                    Preview = r.Preview,
                    Relevance = r.Relevance,
                    ResultType = string.IsNullOrEmpty(r.NoteId) ? SearchResultType.Category : SearchResultType.Note
                }).ToList();
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
                return; // Index is recent enough

            _logger.Debug("Building search index...");
            
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Get all categories and notes - simplified approach
                var allCategories = GetAllCategories();
                var allNotes = await GetAllNotesAsync();
                
                cancellationToken.ThrowIfCancellationRequested();
                
                // Build index using existing logic (run on background thread)
                await Task.Run(() => _searchIndex.BuildIndex(allCategories, allNotes), cancellationToken);
                
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

        private List<CategoryModel> GetAllCategories()
        {
            try
            {
                // Use existing NoteService to get categories
                return _noteService.LoadCategoriesAsync(_configService.Settings.MetadataPath).Result ?? new List<CategoryModel>();
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
                var categories = GetAllCategories();
                
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
    }

    public enum SearchResultType
    {
        Note,
        Category
    }
}
