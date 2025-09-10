using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NoteNest.Core.Models;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    public class SearchIndexService
    {
        private readonly Dictionary<string, HashSet<SearchResult>> _searchIndex;
        private readonly Dictionary<string, string> _contentCache = new();
        private readonly object _indexLock = new object();
        private readonly IFileSystemProvider _fileSystem;
        private readonly IAppLogger _logger;
        private readonly int _contentWordLimit;
        private readonly IMarkdownService _markdownService;
        private DateTime _lastIndexTime;
        private bool _indexDirty = true;
        private volatile bool _contentLoadingComplete = false;

        public bool IsContentLoaded => _contentLoadingComplete;
        public bool NeedsReindex => _indexDirty || (DateTime.Now - _lastIndexTime).TotalMinutes > 30;

        public class SearchResult
        {
            public string NoteId { get; set; }
            public string Title { get; set; }
            public string FilePath { get; set; }
            public string CategoryId { get; set; }
            public string Preview { get; set; }
            public float Relevance { get; set; }
            
            public override bool Equals(object obj)
            {
                if (obj is SearchResult other)
                {
                    return NoteId == other.NoteId && CategoryId == other.CategoryId;
                }
                return false;
            }
            
            public override int GetHashCode()
            {
                return HashCode.Combine(NoteId, CategoryId);
            }
        }

        public SearchIndexService(
            int contentWordLimit = 500, 
            IMarkdownService markdownService = null,
            IFileSystemProvider fileSystem = null,
            IAppLogger logger = null)
        {
            _searchIndex = new Dictionary<string, HashSet<SearchResult>>(StringComparer.OrdinalIgnoreCase);
            _contentWordLimit = contentWordLimit > 100 ? contentWordLimit : 500;
            _markdownService = markdownService ?? new MarkdownService(null);
            _fileSystem = fileSystem ?? new DefaultFileSystemProvider();
            _logger = logger ?? AppLogger.Instance;
        }

        /// <summary>
        /// Builds the search index asynchronously with proper error handling
        /// </summary>
        public async Task<bool> BuildIndexAsync(List<CategoryModel> categories, List<NoteModel> allNotes)
        {
            try
            {
                _logger?.Debug($"[SearchIndex] Starting async index build with {allNotes?.Count ?? 0} notes and {categories?.Count ?? 0} categories");
                
                if (allNotes == null || categories == null)
                {
                    _logger?.Warning("[SearchIndex] Null notes or categories provided to BuildIndexAsync");
                    return false;
                }

                // Clear existing index
                lock (_indexLock)
                {
                    _searchIndex.Clear();
                    _contentCache.Clear();
                    _contentLoadingComplete = false;
                }

                // Phase 1: Index metadata immediately (no I/O, very fast)
                int metadataIndexed = 0;
                foreach (var note in allNotes)
                {
                    try
                    {
                        if (note != null)
                        {
                            IndexNoteMetadata(note);
                            metadataIndexed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"[SearchIndex] Failed to index metadata for note {note?.Title}: {ex.Message}");
                    }
                }

                _logger?.Info($"[SearchIndex] Indexed metadata for {metadataIndexed} notes");

                // Index categories
                int categoriesIndexed = 0;
                foreach (var category in categories)
                {
                    try
                    {
                        if (category != null)
                        {
                            IndexCategory(category);
                            categoriesIndexed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"[SearchIndex] Failed to index category {category?.Name}: {ex.Message}");
                    }
                }

                _logger?.Info($"[SearchIndex] Indexed {categoriesIndexed} categories");

                // Update index state
                lock (_indexLock)
                {
                    _lastIndexTime = DateTime.Now;
                    _indexDirty = false;
                }

                // Phase 2: Load content in background (non-blocking)
                _ = Task.Run(async () => 
                {
                    try
                    {
                        await LoadContentInBackgroundAsync(allNotes);
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, "[SearchIndex] Background content loading failed");
                    }
                });

                _logger?.Info($"[SearchIndex] Initial index built with {_searchIndex.Count} unique terms");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[SearchIndex] Failed to build index");
                return false;
            }
        }

        /// <summary>
        /// Indexes only the metadata of a note (no content loading)
        /// </summary>
        private void IndexNoteMetadata(NoteModel note)
        {
            if (note == null) return;

            var result = new SearchResult
            {
                NoteId = note.Id,
                Title = note.Title ?? "Untitled",
                FilePath = note.FilePath ?? "",
                CategoryId = note.CategoryId ?? "",
                Preview = "", // Will be populated when content loads
                Relevance = 1.0f
            };

            // Index title words (always available)
            if (!string.IsNullOrWhiteSpace(note.Title))
            {
                var titleWords = TokenizeText(note.Title);
                foreach (var word in titleWords)
                {
                    AddToIndex(word, result, 2.0f); // Higher weight for title
                }
            }

            // Index filename without extension (always available)
            if (!string.IsNullOrWhiteSpace(note.FilePath))
            {
                var fileName = Path.GetFileNameWithoutExtension(note.FilePath);
                if (!string.IsNullOrEmpty(fileName))
                {
                    var fileWords = TokenizeText(fileName);
                    foreach (var word in fileWords)
                    {
                        AddToIndex(word, result, 1.5f);
                    }
                }
            }

            // Note: NoteModel doesn't have Tags property in this version
            // This section is commented out until Tags support is added
        }

        /// <summary>
        /// Loads note content asynchronously in the background
        /// </summary>
        private async Task LoadContentInBackgroundAsync(List<NoteModel> notes)
        {
            _logger?.Debug("[SearchIndex] Starting background content loading");
            int loadedCount = 0;
            int failedCount = 0;
            int skippedCount = 0;

            foreach (var note in notes)
            {
                if (note == null || string.IsNullOrWhiteSpace(note.FilePath))
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    // Check if file exists
                    bool fileExists = _fileSystem != null 
                        ? await _fileSystem.ExistsAsync(note.FilePath)
                        : File.Exists(note.FilePath);

                    if (!fileExists)
                    {
                        _logger?.Debug($"[SearchIndex] File not found: {note.FilePath}");
                        skippedCount++;
                        continue;
                    }

                    // Load content from file
                    string content = await LoadNoteContentAsync(note.FilePath);
                    if (!string.IsNullOrEmpty(content))
                    {
                        // Cache the content
                        lock (_contentCache)
                        {
                            _contentCache[note.Id] = content;
                        }

                        // Index the content
                        IndexNoteContent(note, content);
                        loadedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Debug($"[SearchIndex] Failed to load content for {note.Title}: {ex.Message}");
                    failedCount++;
                }

                // Yield periodically to avoid blocking
                if ((loadedCount + failedCount + skippedCount) % 10 == 0)
                {
                    await Task.Yield();
                }
            }

            _contentLoadingComplete = true;
            _logger?.Info($"[SearchIndex] Background content loading complete: {loadedCount} loaded, {failedCount} failed, {skippedCount} skipped");
        }

        /// <summary>
        /// Loads note content with retry logic for locked files
        /// </summary>
        private async Task<string> LoadNoteContentAsync(string filePath)
        {
            const int maxRetries = 3;
            const int baseDelayMs = 100;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    if (_fileSystem != null)
                    {
                        return await _fileSystem.ReadTextAsync(filePath);
                    }
                    else
                    {
                        // Fallback to direct file access with proper async and sharing
                        using (var stream = new FileStream(
                            filePath, 
                            FileMode.Open, 
                            FileAccess.Read, 
                            FileShare.ReadWrite, 
                            bufferSize: 4096, 
                            useAsync: true))
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            return await reader.ReadToEndAsync();
                        }
                    }
                }
                catch (IOException) when (attempt < maxRetries - 1)
                {
                    // File might be locked, wait with exponential backoff
                    var delay = baseDelayMs * (int)Math.Pow(2, attempt);
                    _logger?.Debug($"[SearchIndex] File locked, retrying in {delay}ms: {Path.GetFileName(filePath)}");
                    await Task.Delay(delay);
                }
                catch (UnauthorizedAccessException)
                {
                    _logger?.Warning($"[SearchIndex] Access denied to file: {filePath}");
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.Debug($"[SearchIndex] Could not read file {filePath}: {ex.Message}");
                    break;
                }
            }

            return null;
        }

        /// <summary>
        /// Indexes the content of a note
        /// </summary>
        private void IndexNoteContent(NoteModel note, string content)
        {
            if (string.IsNullOrEmpty(content)) 
            {
                _logger?.Debug($"[SearchIndex] Empty content for note: {note?.Title}");
                return;
            }

            // Debug preview generation for bulleted content
            var preview = GetPreview(content);
            _logger?.Debug($"[SearchIndex] Content length: {content.Length}, Preview: '{preview?.Substring(0, Math.Min(50, preview?.Length ?? 0)) ?? "NULL"}'");

            var result = new SearchResult
            {
                NoteId = note.Id,
                Title = note.Title ?? "Untitled",
                FilePath = note.FilePath ?? "",
                CategoryId = note.CategoryId ?? "",
                Preview = preview,
                Relevance = 1.0f
            };

            // Process content based on format
            var contentToIndex = content;
            if (note.Format == NoteFormat.Markdown && _markdownService != null)
            {
                try
                {
                    contentToIndex = _markdownService.StripMarkdownForIndex(contentToIndex);
                }
                catch (Exception ex)
                {
                    _logger?.Debug($"[SearchIndex] Markdown stripping failed for {note.Title}: {ex.Message}");
                }
            }

            // Index content words
            var contentWords = TokenizeText(contentToIndex);
            lock (_indexLock)
            {
                int wordCount = 0;
                foreach (var word in contentWords)
                {
                    if (wordCount >= _contentWordLimit) break;
                    AddToIndex(word, result, 1.0f); // Normal weight for content
                    wordCount++;
                }
            }

            _logger?.Debug($"[SearchIndex] Indexed content for note: {note.Title} ({contentWords.Count} unique words)");
        }

        /// <summary>
        /// Indexes a category
        /// </summary>
        private void IndexCategory(CategoryModel category)
        {
            if (category == null) return;

            var result = new SearchResult
            {
                CategoryId = category.Id,
                Title = category.Name ?? "Unnamed Category",
                FilePath = category.Path ?? "",
                Relevance = 0.8f,
                Preview = "",
                NoteId = ""
            };

            // Index category name
            if (!string.IsNullOrWhiteSpace(category.Name))
            {
                var words = TokenizeText(category.Name);
                foreach (var word in words)
                {
                    AddToIndex(word, result, 1.0f);
                }
            }

            // Index tags
            if (category.Tags != null && category.Tags.Count > 0)
            {
                foreach (var tag in category.Tags.Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    AddToIndex(tag.ToLowerInvariant(), result, 1.2f);
                }
            }
        }

        /// <summary>
        /// Adds a word to the index with proper locking
        /// </summary>
        private void AddToIndex(string word, SearchResult result, float weight)
        {
            if (string.IsNullOrWhiteSpace(word) || word.Length < 2) return;

            var normalizedWord = word.ToLowerInvariant().Trim();
            if (normalizedWord.Length < 2) return;

            lock (_indexLock)
            {
                if (!_searchIndex.TryGetValue(normalizedWord, out var results))
                {
                    results = new HashSet<SearchResult>();
                    _searchIndex[normalizedWord] = results;
                }

                // Create a weighted copy of the result
                var weightedResult = new SearchResult
                {
                    NoteId = result.NoteId,
                    Title = result.Title,
                    FilePath = result.FilePath,
                    CategoryId = result.CategoryId,
                    Preview = result.Preview,
                    Relevance = result.Relevance * weight
                };

                results.Add(weightedResult);
            }
        }

        /// <summary>
        /// Performs a search with multiple matching strategies
        /// </summary>
        public List<SearchResult> Search(string query, int maxResults = 50)
        {
            if (string.IsNullOrWhiteSpace(query)) 
                return new List<SearchResult>();

            _logger?.Debug($"[SearchIndex] Searching for: '{query}'");

            var queryWords = TokenizeText(query);
            if (!queryWords.Any())
                return new List<SearchResult>();

            var resultScores = new Dictionary<string, (SearchResult result, float score)>();

            lock (_indexLock)
            {
                foreach (var word in queryWords)
                {
                    var lowerWord = word.ToLowerInvariant();

                    // Exact match
                    if (_searchIndex.TryGetValue(lowerWord, out var exactMatches))
                    {
                        foreach (var result in exactMatches)
                        {
                            UpdateResultScore(resultScores, result, 1.0f);
                        }
                    }

                    // Prefix match (for autocomplete)
                    var prefixMatches = _searchIndex
                        .Where(kvp => kvp.Key.StartsWith(lowerWord, StringComparison.OrdinalIgnoreCase))
                        .SelectMany(kvp => kvp.Value);

                    foreach (var result in prefixMatches)
                    {
                        UpdateResultScore(resultScores, result, 0.7f);
                    }

                    // Fuzzy match (contains) - only for words 3+ characters
                    if (word.Length >= 3)
                    {
                        var fuzzyMatches = _searchIndex
                            .Where(kvp => kvp.Key.Contains(lowerWord, StringComparison.OrdinalIgnoreCase) 
                                         && !kvp.Key.StartsWith(lowerWord, StringComparison.OrdinalIgnoreCase))
                            .SelectMany(kvp => kvp.Value);

                        foreach (var result in fuzzyMatches)
                        {
                            UpdateResultScore(resultScores, result, 0.5f);
                        }
                    }
                }
            }

            // Sort by score and return top results
            var finalResults = resultScores
                .OrderByDescending(kvp => kvp.Value.score)
                .ThenBy(kvp => kvp.Value.result.Title)
                .Take(maxResults)
                .Select(kvp => kvp.Value.result)
                .ToList();

            _logger?.Debug($"[SearchIndex] Returning {finalResults.Count} results");
            return finalResults;
        }

        /// <summary>
        /// Updates the cumulative score for a search result
        /// </summary>
        private void UpdateResultScore(Dictionary<string, (SearchResult, float)> scores, 
            SearchResult result, float weight)
        {
            var key = result.NoteId ?? result.CategoryId;
            if (string.IsNullOrEmpty(key)) return;

            if (scores.TryGetValue(key, out var existing))
            {
                scores[key] = (existing.Item1, existing.Item2 + (result.Relevance * weight));
            }
            else
            {
                scores[key] = (result, result.Relevance * weight);
            }
        }

        /// <summary>
        /// Tokenizes text into searchable words
        /// </summary>
        private HashSet<string> TokenizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new HashSet<string>();

            var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();

            foreach (char c in text.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    sb.Append(c);
                }
                else if (sb.Length > 0)
                {
                    var word = sb.ToString();
                    if (word.Length >= 2) // Only index words with 2+ characters
                    {
                        words.Add(word);
                    }
                    sb.Clear();
                }
            }

            if (sb.Length >= 2)
            {
                words.Add(sb.ToString());
            }

            return words;
        }

        /// <summary>
        /// Creates a preview snippet from content
        /// </summary>
        private string GetPreview(string content, int maxLength = 150)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger?.Debug("[SearchIndex] GetPreview: content is null or whitespace");
                return string.Empty;
            }

            try
            {
                // Clean up the content to create a readable preview
                var preview = content;
                
                // Convert bullet points to readable format (matching NoteNest's FormattedTextEditor style)
                preview = System.Text.RegularExpressions.Regex.Replace(preview, @"^\s*[-*+]\s+", "• ", System.Text.RegularExpressions.RegexOptions.Multiline);
                
                // Convert nested bullet points (with indentation)
                preview = System.Text.RegularExpressions.Regex.Replace(preview, @"^\s{2,}[-*+]\s+", " • ", System.Text.RegularExpressions.RegexOptions.Multiline);
                
                // Remove markdown headers
                preview = System.Text.RegularExpressions.Regex.Replace(preview, @"^#+\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);
                
                // Convert line breaks to spaces for single-line preview
                preview = System.Text.RegularExpressions.Regex.Replace(preview, @"\r?\n", " ");
                
                // Remove excessive whitespace
                preview = System.Text.RegularExpressions.Regex.Replace(preview, @"\s+", " ").Trim();
                
                _logger?.Debug($"[SearchIndex] GetPreview: original={content.Length} chars, normalized={preview.Length} chars");

                if (string.IsNullOrWhiteSpace(preview))
                {
                    _logger?.Warning("[SearchIndex] GetPreview: preview is empty after processing");
                    // Fallback: just take first line of original content
                    var firstLine = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
                    if (!string.IsNullOrEmpty(firstLine))
                    {
                        return firstLine.Length > maxLength ? firstLine.Substring(0, maxLength) + "..." : firstLine;
                    }
                    return "Content available";
                }

                if (preview.Length <= maxLength)
                    return preview;

                // Try to break at a word boundary
                var truncated = preview.Substring(0, maxLength);
                var lastSpace = truncated.LastIndexOf(' ');
                if (lastSpace > maxLength * 0.8) // If we have a reasonable break point
                {
                    truncated = truncated.Substring(0, lastSpace);
                }

                return truncated + "...";
            }
            catch (Exception ex)
            {
                _logger?.Warning($"[SearchIndex] GetPreview failed: {ex.Message}");
                // Safe fallback
                if (content.Length > maxLength) 
                {
                    return content.Substring(0, Math.Min(maxLength, content.Length)) + "...";
                }
                return content;
            }
        }

        /// <summary>
        /// Marks the index as needing rebuild
        /// </summary>
        public void MarkDirty()
        {
            _indexDirty = true;
        }

        /// <summary>
        /// Gets the cached content for a note (if available)
        /// </summary>
        public string GetCachedContent(string noteId)
        {
            lock (_contentCache)
            {
                return _contentCache.TryGetValue(noteId, out var content) ? content : null;
            }
        }

        /// <summary>
        /// Clears the entire index and cache
        /// </summary>
        public void Clear()
        {
            lock (_indexLock)
            {
                _searchIndex.Clear();
                _contentCache.Clear();
                _indexDirty = true;
                _contentLoadingComplete = false;
            }
        }
    }
}
