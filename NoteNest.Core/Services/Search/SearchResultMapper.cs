using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NoteNest.Core.Interfaces.Search;
using NoteNest.Core.Models;
using NoteNest.Core.Models.Search;

namespace NoteNest.Core.Services.Search
{
    /// <summary>
    /// Maps FTS5 search results to UI ViewModels and converts NoteModels to SearchDocuments
    /// Enhanced with smart preview caching for optimal performance
    /// Single Responsibility: Data transformation and mapping between layers
    /// </summary>
    public class SearchResultMapper : ISearchResultMapper
    {
        private readonly Dictionary<string, string> _categoryNameCache = new();
        private readonly MinimalPreviewCache _previewCache;

        /// <summary>
        /// Initialize mapper with smart preview caching
        /// </summary>
        public SearchResultMapper()
        {
            _previewCache = new MinimalPreviewCache(50); // 50 items = ~7.5KB memory
            System.Diagnostics.Debug.WriteLine($"[MAPPER] SearchResultMapper initialized with preview cache");
        }

        #region FTS Result to ViewModel Mapping

        public SearchResultDto MapToDto(FtsSearchResult ftsResult, string originalQuery)
        {
            return new SearchResultDto
            {
                NoteId = ftsResult.NoteId,
                Title = ftsResult.Title ?? "Untitled",
                FilePath = ftsResult.FilePath,
                CategoryId = ftsResult.CategoryId,
                Preview = _previewCache.GetPreview(ftsResult), // Use smart preview cache
                Relevance = (float)ftsResult.Relevance,
                Score = CalculateCompositeScore(ftsResult, originalQuery),
                ResultType = DetermineResultType(ftsResult),
                SearchQuery = originalQuery,
                LastModified = ftsResult.LastModified,
                HighlightedSnippet = ProcessSnippetHighlighting(ftsResult.Snippet)
            };
        }

        public List<SearchResultDto> MapToDtos(List<FtsSearchResult> ftsResults, string originalQuery)
        {
            return ftsResults.Select(result => MapToDto(result, originalQuery)).ToList();
        }

        #endregion

        #region NoteModel to SearchDocument Mapping

        public SearchDocument MapFromNoteModel(NoteModel note, string plainTextContent)
        {
            return SearchDocument.FromNoteModel(note, plainTextContent);
        }

        public List<SearchDocument> MapFromNoteModels(List<NoteModel> notes, Func<string, string> contentExtractor)
        {
            var documents = new List<SearchDocument>();

            foreach (var note in notes)
            {
                try
                {
                    var plainText = contentExtractor(note.Content ?? string.Empty);
                    var document = MapFromNoteModel(note, plainText);
                    documents.Add(document);
                }
                catch (Exception)
                {
                    // Skip problematic notes rather than failing entire batch
                    continue;
                }
            }

            return documents;
        }

        #endregion

        #region Score and Preview Processing

        public int CalculateCompositeScore(FtsSearchResult ftsResult, string originalQuery)
        {
            // Base BM25 score (convert negative to positive, higher = better)
            var baseScore = Math.Max(0, -ftsResult.Relevance * 100);

            // Usage boost (frequently accessed notes)
            var usageBoost = ftsResult.UsageCount * 5;

            // Recency boost (recently modified files)
            var recencyBoost = CalculateRecencyBoost(ftsResult.LastModified);

            // Query match boost (exact matches in title get higher score)
            var queryMatchBoost = CalculateQueryMatchBoost(ftsResult, originalQuery);

            // File size penalty for very small files (likely empty or template)
            var sizeBoost = ftsResult.FileSize < 1024 ? -10 : 0; // Penalize files < 1KB

            var totalScore = (int)(baseScore + usageBoost + recencyBoost + queryMatchBoost + sizeBoost);

            // Ensure positive score
            return Math.Max(1, totalScore);
        }

        public string GeneratePreview(FtsSearchResult ftsResult, int maxLength = 200)
        {
            // Prefer highlighted snippet from FTS5
            if (!string.IsNullOrEmpty(ftsResult.Snippet))
            {
                var snippet = ftsResult.Snippet;
                
                // Truncate if needed
                if (snippet.Length > maxLength)
                {
                    snippet = snippet.Substring(0, maxLength);
                    
                    // Try to end at word boundary
                    var lastSpace = snippet.LastIndexOf(' ');
                    if (lastSpace > maxLength * 0.8) // If we can trim to a space without losing too much
                    {
                        snippet = snippet.Substring(0, lastSpace);
                    }
                    
                    snippet += "...";
                }
                
                return snippet;
            }

            // Fallback to content
            if (!string.IsNullOrEmpty(ftsResult.Content))
            {
                var content = ftsResult.Content;
                
                if (content.Length > maxLength)
                {
                    content = content.Substring(0, maxLength);
                    
                    // Try to end at word boundary
                    var lastSpace = content.LastIndexOf(' ');
                    if (lastSpace > maxLength * 0.8)
                    {
                        content = content.Substring(0, lastSpace);
                    }
                    
                    content += "...";
                }
                
                return content;
            }

            // Fallback to title if no content
            return ftsResult.Title ?? "No preview available";
        }

        public string ProcessSnippetHighlighting(string snippet, HighlightFormat targetFormat = HighlightFormat.Html)
        {
            if (string.IsNullOrEmpty(snippet))
                return snippet;

            return targetFormat switch
            {
                HighlightFormat.Html => snippet, // FTS5 already provides HTML-like format
                
                HighlightFormat.Asterisk => snippet
                    .Replace("<mark>", "*")
                    .Replace("</mark>", "*"),
                
                HighlightFormat.None => snippet
                    .Replace("<mark>", "")
                    .Replace("</mark>", ""),
                
                HighlightFormat.WpfRuns => snippet, // Would need more complex processing for WPF
                
                _ => snippet
            };
        }

        #endregion

        #region Utility Methods

        public SearchResultType DetermineResultType(FtsSearchResult ftsResult)
        {
            // All our results are notes (could be extended for categories, tags, etc.)
            return SearchResultType.Note;
        }

        public string GetCategoryDisplayName(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId))
                return "Uncategorized";

            // Check cache first
            if (_categoryNameCache.TryGetValue(categoryId, out var cachedName))
                return cachedName;

            // TODO: In real implementation, this would lookup category name from CategoryService
            // For now, return the ID or a default name
            var displayName = categoryId.Length > 8 
                ? categoryId.Substring(0, 8) + "..."
                : categoryId;

            // Cache the result
            _categoryNameCache[categoryId] = displayName;
            
            return displayName;
        }

        public string FormatModificationTime(DateTime lastModified)
        {
            if (lastModified == DateTime.MinValue)
                return "Unknown";

            var now = DateTime.Now;
            var diff = now - lastModified;

            if (diff.TotalMinutes < 1)
                return "Just now";
            
            if (diff.TotalMinutes < 60)
                return $"{(int)diff.TotalMinutes} minute{(diff.TotalMinutes >= 2 ? "s" : "")} ago";
            
            if (diff.TotalHours < 24)
                return $"{(int)diff.TotalHours} hour{(diff.TotalHours >= 2 ? "s" : "")} ago";
            
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays} day{(diff.TotalDays >= 2 ? "s" : "")} ago";
            
            if (diff.TotalDays < 30)
            {
                var weeks = (int)(diff.TotalDays / 7);
                return $"{weeks} week{(weeks > 1 ? "s" : "")} ago";
            }
            
            if (diff.TotalDays < 365)
            {
                var months = (int)(diff.TotalDays / 30);
                return $"{months} month{(months > 1 ? "s" : "")} ago";
            }

            var years = (int)(diff.TotalDays / 365);
            return $"{years} year{(years > 1 ? "s" : "")} ago";
        }

        #endregion

        #region Private Helper Methods

        private static int CalculateRecencyBoost(DateTime lastModified)
        {
            if (lastModified == DateTime.MinValue)
                return 0;

            var daysSince = (DateTime.Now - lastModified).TotalDays;

            return daysSince switch
            {
                <= 1 => 50,      // Last day
                <= 7 => 30,      // Last week  
                <= 30 => 15,     // Last month
                <= 90 => 5,      // Last 3 months
                _ => 0           // Older files
            };
        }

        private static int CalculateQueryMatchBoost(FtsSearchResult ftsResult, string originalQuery)
        {
            if (string.IsNullOrEmpty(originalQuery) || string.IsNullOrEmpty(ftsResult.Title))
                return 0;

            var queryLower = originalQuery.ToLowerInvariant();
            var titleLower = ftsResult.Title.ToLowerInvariant();

            // Exact title match
            if (titleLower == queryLower)
                return 100;

            // Title starts with query
            if (titleLower.StartsWith(queryLower))
                return 50;

            // Title contains query
            if (titleLower.Contains(queryLower))
                return 25;

            // Check individual query terms
            var queryTerms = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var matchingTerms = queryTerms.Count(term => titleLower.Contains(term));

            if (matchingTerms > 0)
            {
                // Boost based on percentage of terms matched
                var matchPercentage = (double)matchingTerms / queryTerms.Length;
                return (int)(matchPercentage * 20);
            }

            return 0;
        }

        /// <summary>
        /// Clear category name cache (call when categories are updated)
        /// </summary>
        public void ClearCategoryCache()
        {
            _categoryNameCache.Clear();
        }

        /// <summary>
        /// Update category name in cache
        /// </summary>
        public void UpdateCategoryName(string categoryId, string displayName)
        {
            if (!string.IsNullOrEmpty(categoryId) && !string.IsNullOrEmpty(displayName))
            {
                _categoryNameCache[categoryId] = displayName;
            }
        }

        /// <summary>
        /// Clear preview cache (call when preview logic changes or for memory management)
        /// </summary>
        public void ClearPreviewCache()
        {
            _previewCache.Clear();
        }

        /// <summary>
        /// Get cache statistics for monitoring and debugging
        /// </summary>
        public MinimalPreviewCache.CacheStatistics GetCacheStatistics()
        {
            return _previewCache.GetStatistics();
        }

        #endregion
    }
}
