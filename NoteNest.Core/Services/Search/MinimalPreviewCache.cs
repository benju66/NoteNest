using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NoteNest.Core.Models.Search;

namespace NoteNest.Core.Services.Search
{
    /// <summary>
    /// Memory-bounded LRU cache for search result previews.
    /// Optimized for production use with automatic eviction and thread-safety considerations.
    /// Memory usage: ~7.5KB for 50 items (150 chars each)
    /// </summary>
    public class MinimalPreviewCache
    {
        private readonly int _capacity;
        private readonly Dictionary<string, LinkedListNode<CacheItem>> _cache;
        private readonly LinkedList<CacheItem> _lruList;
        private readonly object _lockObject = new object();

        /// <summary>
        /// Initialize cache with specified capacity
        /// </summary>
        /// <param name="capacity">Maximum number of items to cache (default: 50)</param>
        public MinimalPreviewCache(int capacity = 50)
        {
            _capacity = Math.Max(1, capacity); // Ensure at least 1 item
            _cache = new Dictionary<string, LinkedListNode<CacheItem>>(capacity);
            _lruList = new LinkedList<CacheItem>();
        }

        /// <summary>
        /// Get preview for search result using intelligent fallback strategy
        /// </summary>
        /// <param name="result">FTS5 search result</param>
        /// <returns>Optimized preview text</returns>
        public string GetPreview(FtsSearchResult result)
        {
            System.Diagnostics.Debug.WriteLine($"[CACHE] GetPreview called for: {result?.Title ?? "null"}");
            
            if (result == null || string.IsNullOrEmpty(result.NoteId))
            {
                System.Diagnostics.Debug.WriteLine($"[CACHE] Returning early - null result or empty NoteId");
                return "No preview available";
            }

            lock (_lockObject)
            {
                // Try cache first (most common case for recent searches)
                if (_cache.TryGetValue(result.NoteId, out var node))
                {
                    // Move to front (most recently used)
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                    return node.Value.Preview;
                }

                // Generate preview using intelligent fallback hierarchy
                System.Diagnostics.Debug.WriteLine($"[CACHE] Cache miss for {result.NoteId}, generating new preview");
                var preview = GeneratePreview(result);
                System.Diagnostics.Debug.WriteLine($"[CACHE] Generated preview: '{preview}' for {result.Title}");
                
                // Cache the generated preview
                AddToCache(result.NoteId, preview);
                
                return preview;
            }
        }

        /// <summary>
        /// Clear all cached previews
        /// </summary>
        public void Clear()
        {
            lock (_lockObject)
            {
                _cache.Clear();
                _lruList.Clear();
            }
        }

        /// <summary>
        /// Get current cache statistics for monitoring
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            lock (_lockObject)
            {
                return new CacheStatistics
                {
                    CurrentItems = _cache.Count,
                    MaxCapacity = _capacity,
                    MemoryUsageEstimate = _cache.Count * 200 // ~200 bytes per item (estimate)
                };
            }
        }

        #region Private Methods

        private string GeneratePreview(FtsSearchResult result)
        {
            // Strategy 1: Use pre-generated preview from index (best performance)
            System.Diagnostics.Debug.WriteLine($"[CACHE] Strategy 1 Check: ContentPreview = '{result.ContentPreview}' (length: {result.ContentPreview?.Length ?? 0})");
            if (!string.IsNullOrEmpty(result.ContentPreview))
            {
                System.Diagnostics.Debug.WriteLine($"[CACHE] Using Strategy 1 (ContentPreview): '{result.ContentPreview}'");
                return result.ContentPreview;
            }

            // Strategy 2: Clean and use FTS5 snippet (good for search context)
            System.Diagnostics.Debug.WriteLine($"[CACHE] Strategy 2 Check: Snippet = '{result.Snippet}' (length: {result.Snippet?.Length ?? 0})");
            if (!string.IsNullOrEmpty(result.Snippet))
            {
                System.Diagnostics.Debug.WriteLine($"[CACHE] Using Strategy 2 (Snippet)");
                return CleanSnippet(result.Snippet);
            }

            // Strategy 3: Generate from full content (fallback)
            System.Diagnostics.Debug.WriteLine($"[CACHE] Strategy 3 Check: Content = '{result.Content?.Substring(0, Math.Min(50, result.Content?.Length ?? 0))}...' (length: {result.Content?.Length ?? 0})");
            if (!string.IsNullOrEmpty(result.Content))
            {
                System.Diagnostics.Debug.WriteLine($"[CACHE] Using Strategy 3 (Content)");
                return GeneratePreviewFromContent(result.Content, 150);
            }

            // Strategy 4: Use title as last resort
            System.Diagnostics.Debug.WriteLine($"[CACHE] Strategy 4 Check: Title = '{result.Title}' (length: {result.Title?.Length ?? 0})");
            if (!string.IsNullOrEmpty(result.Title))
            {
                System.Diagnostics.Debug.WriteLine($"[CACHE] Using Strategy 4 (Title)");
                return $"Note: {result.Title}";
            }

            return "No preview available";
        }

        private string CleanSnippet(string snippet)
        {
            if (string.IsNullOrEmpty(snippet))
                return "No preview available";

            try
            {
                // Remove FTS5 highlight marks but preserve the highlighted content
                var cleaned = snippet
                    .Replace("<mark>", "")
                    .Replace("</mark>", "")
                    .Replace("...", " ")
                    .Replace("  ", " ") // Normalize double spaces
                    .Trim();

                // If snippet is too short after cleaning, pad with ellipsis
                if (cleaned.Length < 20)
                    cleaned += "...";

                return cleaned;
            }
            catch
            {
                return "Preview unavailable";
            }
        }

        private string GeneratePreviewFromContent(string content, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "Empty note";

            try
            {
                // Remove extra whitespace
                var normalized = Regex.Replace(content, @"\s+", " ").Trim();
                
                if (normalized.Length <= maxLength)
                    return normalized;

                // Smart truncation at word boundary
                var truncated = normalized.Substring(0, maxLength);
                var lastSpace = truncated.LastIndexOf(' ');

                if (lastSpace > maxLength * 0.7) // If we're past 70% of max length
                    truncated = normalized.Substring(0, lastSpace);

                return truncated.Trim() + "...";
            }
            catch
            {
                return "Preview unavailable";
            }
        }

        private void AddToCache(string noteId, string preview)
        {
            if (string.IsNullOrEmpty(noteId) || preview == null)
                return;

            // If already exists, just update and move to front
            if (_cache.TryGetValue(noteId, out var existingNode))
            {
                _lruList.Remove(existingNode);
                existingNode.Value.Preview = preview;
                _lruList.AddFirst(existingNode);
                return;
            }

            // Evict oldest if at capacity
            if (_cache.Count >= _capacity)
            {
                var oldest = _lruList.Last;
                if (oldest != null)
                {
                    _lruList.RemoveLast();
                    _cache.Remove(oldest.Value.NoteId);
                }
            }

            // Add new item to front
            var cacheItem = new CacheItem(noteId, preview);
            var newNode = _lruList.AddFirst(cacheItem);
            _cache[noteId] = newNode;
        }

        #endregion

        #region Cache Item and Statistics Classes

        private class CacheItem
        {
            public string NoteId { get; }
            public string Preview { get; set; }

            public CacheItem(string noteId, string preview)
            {
                NoteId = noteId ?? throw new ArgumentNullException(nameof(noteId));
                Preview = preview ?? string.Empty;
            }
        }

        public class CacheStatistics
        {
            public int CurrentItems { get; set; }
            public int MaxCapacity { get; set; }
            public long MemoryUsageEstimate { get; set; }
        }

        #endregion
    }
}
