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
using NoteNest.Core.Services.Search;
using NoteNest.Core.Diagnostics;

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
        private DateTime _lastIndexTime;
        
        // SupervisedTaskRunner removed - simplified service without complex task coordination
        private bool _indexDirty = true;
        private volatile bool _contentLoadingComplete = false;
        private volatile int _contentLoadProgress = 0;
        private volatile int _totalNotesToLoad = 0;
        private TaskCompletionSource<bool> _contentLoadingTask = new();

        public bool IsContentLoaded => _contentLoadingComplete;
        public Task<bool> WaitForContentAsync() => _contentLoadingTask.Task;
        public int ContentLoadProgress => _totalNotesToLoad > 0 ? (_contentLoadProgress * 100 / _totalNotesToLoad) : 0;
        public bool NeedsReindex => _indexDirty || (DateTime.Now - _lastIndexTime).TotalMinutes > 30;

        public class SearchResult
        {
            public string NoteId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public string CategoryId { get; set; } = string.Empty;
            public string Preview { get; set; } = string.Empty;
            public float Relevance { get; set; }
            
            public override bool Equals(object? obj)
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
            IFileSystemProvider? fileSystem = null,
            IAppLogger? logger = null)
        {
            _searchIndex = new Dictionary<string, HashSet<SearchResult>>(StringComparer.OrdinalIgnoreCase);
            _contentWordLimit = contentWordLimit > 100 ? contentWordLimit : 500;
            _fileSystem = fileSystem ?? new DefaultFileSystemProvider();
            _logger = logger ?? AppLogger.Instance;
        }

        /// <summary>
        /// Builds the search index asynchronously with proper error handling
        /// </summary>
        public async Task<bool> BuildIndexAsync(List<CategoryModel> categories, List<NoteModel> allNotes)
        {
            #if DEBUG
            bool result = false;
            await EnhancedMemoryTracker.TrackServiceOperationAsync<SearchIndexService>("BuildIndexAsync", async () =>
            {
            #endif
                try
                {
                    _logger?.Debug($"[SearchIndex] Starting async index build with {allNotes?.Count ?? 0} notes and {categories?.Count ?? 0} categories");
                    
                    if (allNotes == null || categories == null)
                    {
                        _logger?.Warning("[SearchIndex] Null notes or categories provided to BuildIndexAsync");
                        #if DEBUG
                        result = false;
                        return;
                        #else
                        return false;
                        #endif
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
                }).ConfigureAwait(false);

                _logger?.Info($"[SearchIndex] Initial index built with {_searchIndex.Count} unique terms");
                #if DEBUG
                result = true;
                #else
                return true;
                #endif
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "[SearchIndex] Failed to build index");
                    #if DEBUG
                    result = false;
                    #else
                    return false;
                    #endif
                }
            #if DEBUG
            });
            return result;
            #endif
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
        /// Loads note content asynchronously in parallel for better performance
        /// </summary>
        private async Task LoadContentInBackgroundAsync(List<NoteModel> notes)
        {
            _logger?.Debug("[SearchIndex] Starting parallel background content loading");
            _totalNotesToLoad = notes.Count;
            _contentLoadProgress = 0;
            _contentLoadingTask = new TaskCompletionSource<bool>();
            
            int loadedCount = 0;
            int failedCount = 0;
            int skippedCount = 0;
            
            // Use SemaphoreSlim to limit concurrent file operations
            using var semaphore = new SemaphoreSlim(4, 4); // Max 4 concurrent file reads
            var loadTasks = new List<Task>();
            
            foreach (var note in notes)
            {
                if (note == null || string.IsNullOrWhiteSpace(note.FilePath))
                {
                    Interlocked.Increment(ref skippedCount);
                    Interlocked.Increment(ref _contentLoadProgress);
                    continue;
                }
                
                var loadTask = Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        // Check if file exists
                        bool fileExists = _fileSystem != null 
                            ? await _fileSystem.ExistsAsync(note.FilePath)
                            : File.Exists(note.FilePath);

                        if (!fileExists)
                        {
                            _logger?.Debug($"[SearchIndex] File not found: {note.FilePath}");
                            Interlocked.Increment(ref skippedCount);
                            return;
                        }

                        // Load content from file
                        string content = await LoadNoteContentAsync(note.FilePath);
                        if (!string.IsNullOrEmpty(content))
                        {
                            // Generate preview immediately
                            var preview = GetPreview(content);
                            
                            // Update the search result with preview
                            lock (_indexLock)
                            {
                                // Find and update existing search results for this note
                                foreach (var termResults in _searchIndex.Values)
                                {
                                    var noteResult = termResults.FirstOrDefault(r => r.NoteId == note.Id);
                                    if (noteResult != null)
                                    {
                                        noteResult.Preview = preview;
                                    }
                                }
                                
                                // Cache the content
                                _contentCache[note.Id] = content;
                            }
                            
                            // Index the full content for better search
                            IndexNoteContent(note, content);
                            Interlocked.Increment(ref loadedCount);
                        }
                        else
                        {
                            Interlocked.Increment(ref skippedCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Debug($"[SearchIndex] Failed to load content for {note.Title}: {ex.Message}");
                        Interlocked.Increment(ref failedCount);
                    }
                    finally
                    {
                        Interlocked.Increment(ref _contentLoadProgress);
                        semaphore.Release();
                    }
                });
                
                loadTasks.Add(loadTask);
            }
            
            // Wait for all load tasks to complete
            await Task.WhenAll(loadTasks);
            
            _contentLoadingComplete = true;
            _contentLoadingTask.SetResult(true);
            _logger?.Info($"[SearchIndex] Parallel content loading complete: {loadedCount} loaded, {failedCount} failed, {skippedCount} skipped");
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

            return string.Empty;
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

            // Process content based on format FIRST - RTF PRIORITY implementation
            // RTF-ONLY: Extract plain text for indexing
            var contentToIndex = RTFTextExtractor.ExtractPlainText(content);
            float relevanceBoost = 1.0f;
            
            _logger?.Debug($"[SearchIndex] RTF content processed for {note.Title}: {content.Length} → {contentToIndex.Length} chars");

            // FIXED: Generate preview from PROCESSED content (clean text, not raw RTF codes)
            var preview = GetPreview(contentToIndex);
            _logger?.Debug($"[SearchIndex] Preview generated from processed content for {note.Title}: '{preview?.Substring(0, Math.Min(50, preview?.Length ?? 0)) ?? "NULL"}'");

            var result = new SearchResult
            {
                NoteId = note.Id,
                Title = note.Title ?? "Untitled",
                FilePath = note.FilePath ?? "",
                CategoryId = note.CategoryId ?? "",
                Preview = preview ?? "",
                Relevance = relevanceBoost
            };

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
            #if DEBUG
            List<SearchResult> result = null;
            EnhancedMemoryTracker.TrackServiceOperation<SearchIndexService>("Search", () =>
            {
            #endif
                if (string.IsNullOrWhiteSpace(query)) 
                {
                    #if DEBUG
                    result = new List<SearchResult>();
                    return;
                    #else
                    return new List<SearchResult>();
                    #endif
                }

                _logger?.Debug($"[SearchIndex] Searching for: '{query}'");

                var queryWords = TokenizeText(query);
                if (!queryWords.Any())
                {
                    #if DEBUG
                    result = new List<SearchResult>();
                    return;
                    #else
                    return new List<SearchResult>();
                    #endif
                }

                var resultScores = new Dictionary<string, (SearchResult result, float score)>();

                lock (_indexLock)
                {
                    foreach (var word in queryWords)
                    {
                        var lowerWord = word.ToLowerInvariant();

                        // Exact match
                        if (_searchIndex.TryGetValue(lowerWord, out var exactMatches))
                        {
                            foreach (var searchResult in exactMatches)
                            {
                                UpdateResultScore(resultScores, searchResult, 1.0f);
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
                #if DEBUG
                result = finalResults;
            });
            return result;
            #else
                return finalResults;
            #endif
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
        /// Creates a preview snippet from content with better list handling
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
                // Split into lines first to preserve structure
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var meaningfulLines = new List<string>();
                
                foreach (var line in lines.Take(5)) // Take first 5 lines max
                {
                    var processed = line.Trim();
                    
                    // Skip empty lines
                    if (string.IsNullOrWhiteSpace(processed))
                        continue;
                        
                    // Convert markdown headers to plain text
                    processed = System.Text.RegularExpressions.Regex.Replace(processed, @"^#+\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);
                    
                    // Convert bullet points to readable format
                    processed = System.Text.RegularExpressions.Regex.Replace(processed, @"^[\s]*[-*+]\s+", "• ", System.Text.RegularExpressions.RegexOptions.Multiline);
                    
                    // Convert numbered lists
                    processed = System.Text.RegularExpressions.Regex.Replace(processed, @"^[\s]*\d+\.\s+", "• ", System.Text.RegularExpressions.RegexOptions.Multiline);
                    
                    // Remove excessive markdown formatting
                    processed = System.Text.RegularExpressions.Regex.Replace(processed, @"\*\*(.+?)\*\*", "$1", System.Text.RegularExpressions.RegexOptions.Multiline);
                    processed = System.Text.RegularExpressions.Regex.Replace(processed, @"__(.+?)__", "$1", System.Text.RegularExpressions.RegexOptions.Multiline);
                    processed = System.Text.RegularExpressions.Regex.Replace(processed, @"\*(.+?)\*", "$1", System.Text.RegularExpressions.RegexOptions.Multiline);
                    processed = System.Text.RegularExpressions.Regex.Replace(processed, @"_(.+?)_", "$1", System.Text.RegularExpressions.RegexOptions.Multiline);
                    
                    if (!string.IsNullOrWhiteSpace(processed))
                    {
                        meaningfulLines.Add(processed);
                    }
                }
                
                // Join lines with space separator
                var preview = string.Join(" ", meaningfulLines);
                
                // Remove excessive whitespace
                preview = System.Text.RegularExpressions.Regex.Replace(preview, @"\s+", " ").Trim();
                
                _logger?.Debug($"[SearchIndex] GetPreview: generated {preview.Length} char preview from {content.Length} chars");
                
                if (string.IsNullOrWhiteSpace(preview))
                {
                    // Fallback: just take first non-empty line
                    var firstLine = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
                    if (!string.IsNullOrEmpty(firstLine))
                    {
                        return firstLine.Length > maxLength 
                            ? firstLine.Substring(0, maxLength) + "..." 
                            : firstLine;
                    }
                    return "Content available";
                }

                // Truncate if needed
                if (preview.Length <= maxLength)
                    return preview;

                // Try to break at a word boundary
                var truncated = preview.Substring(0, maxLength);
                var lastSpace = truncated.LastIndexOf(' ');
                if (lastSpace > maxLength * 0.8) // If we have a reasonable break point
                {
                    truncated = truncated.Substring(0, lastSpace);
                }

                return truncated.Trim() + "...";
            }
            catch (Exception ex)
            {
                _logger?.Warning($"[SearchIndex] GetPreview failed: {ex.Message}");
                // Safe fallback
                var safeLength = Math.Min(maxLength, content.Length);
                return content.Substring(0, safeLength) + (content.Length > maxLength ? "..." : "");
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
        public string? GetCachedContent(string noteId)
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

        #region Persistence Methods

        /// <summary>
        /// Loads index from persisted data with proper preview restoration
        /// </summary>
        public Task LoadFromPersistedAsync(PersistedIndex persisted)
        {
            lock (_indexLock)
            {
                _searchIndex.Clear();
                _contentCache.Clear();
                _contentLoadingComplete = false;
            }
            
            foreach (var entry in persisted.Entries)
            {
                var result = new SearchResult
                {
                    NoteId = entry.Id,
                    Title = entry.Title,
                    FilePath = entry.RelativePath,
                    CategoryId = "", // Categories handled separately
                    Preview = entry.ContentPreview ?? "", // Ensure preview is loaded
                    Relevance = 1.0f
                };
                
                // Cache the content preview if available
                if (!string.IsNullOrEmpty(entry.ContentPreview))
                {
                    lock (_contentCache)
                    {
                        _contentCache[entry.Id] = entry.ContentPreview;
                    }
                }
                
                // Index title words
                if (!string.IsNullOrWhiteSpace(entry.Title))
                {
                    var titleWords = TokenizeText(entry.Title);
                    foreach (var word in titleWords)
                    {
                        AddToIndex(word, result, 2.0f);
                    }
                }
                
                // Index content words from preview (for immediate searchability)
                if (!string.IsNullOrWhiteSpace(entry.ContentPreview))
                {
                    var contentWords = TokenizeText(entry.ContentPreview);
                    int wordCount = 0;
                    foreach (var word in contentWords)
                    {
                        if (wordCount >= 100) break; // Limit preview indexing
                        AddToIndex(word, result, 1.0f);
                        wordCount++;
                    }
                }
            }
            
            // Mark content as loaded since we have previews
            _contentLoadingComplete = true;
            _contentLoadingTask.SetResult(true);
            
            lock (_indexLock)
            {
                _lastIndexTime = persisted.CreatedAt;
                _indexDirty = false;
            }
            
            _logger?.Info($"[SearchIndex] Loaded {persisted.Entries.Count} entries from persistence with previews");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Exports current index for persistence
        /// </summary>
        public Task<PersistedIndex> ExportForPersistenceAsync()
        {
            var index = new PersistedIndex
            {
                CreatedAt = DateTime.UtcNow,
                Entries = new List<IndexEntry>()
            };
            
            var processedNotes = new HashSet<string>();
            
            lock (_indexLock)
            {
                // Collect all unique notes from the search index
                foreach (var resultSet in _searchIndex.Values)
                {
                    foreach (var result in resultSet)
                    {
                        if (!string.IsNullOrEmpty(result.NoteId) && !processedNotes.Contains(result.NoteId))
                        {
                            processedNotes.Add(result.NoteId);
                            
                            // Get cached content if available
                            string? content = null;
                            _contentCache.TryGetValue(result.NoteId, out content);
                            
                            index.Entries.Add(new IndexEntry
                            {
                                Id = result.NoteId,
                                RelativePath = result.FilePath,
                                Title = result.Title,
                                ContentPreview = content?.Length > 500 
                                    ? content.Substring(0, 500) 
                                    : content ?? "",
                                Tags = new List<string>(), // TODO: Add tags when available
                                LastModified = File.Exists(result.FilePath) 
                                    ? File.GetLastWriteTimeUtc(result.FilePath) 
                                    : DateTime.UtcNow,
                                FileSize = File.Exists(result.FilePath) 
                                    ? new FileInfo(result.FilePath).Length 
                                    : 0
                            });
                        }
                    }
                }
            }
            
            return Task.FromResult(index);
        }

        /// <summary>
        /// Updates index for a single file
        /// </summary>
        public async Task UpdateFileAsync(string filePath)
        {
            try
            {
                var content = await LoadNoteContentAsync(filePath);
                var title = Path.GetFileNameWithoutExtension(filePath);
                
                var result = new SearchResult
                {
                    NoteId = Guid.NewGuid().ToString(),
                    Title = title,
                    FilePath = filePath,
                    CategoryId = "",
                    Preview = GetPreview(content),
                    Relevance = 1.0f
                };

                // Remove existing entries for this file first
                RemoveFile(filePath);
                
                // Index title words
                if (!string.IsNullOrWhiteSpace(title))
                {
                    var titleWords = TokenizeText(title);
                    foreach (var word in titleWords)
                    {
                        AddToIndex(word, result, 2.0f);
                    }
                }
                
                // Index content words
                if (!string.IsNullOrEmpty(content))
                {
                    var contentWords = TokenizeText(content);
                    foreach (var word in contentWords)
                    {
                        AddToIndex(word, result, 1.0f);
                    }

                    // Cache content
                    lock (_contentCache)
                    {
                        _contentCache[result.NoteId] = content;
                    }
                }
                
                _logger?.Debug($"Updated index for file: {filePath}");
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to update index for file {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds a new file to the index
        /// </summary>
        public async Task AddFileAsync(string filePath)
        {
            await UpdateFileAsync(filePath); // Same logic for add
        }

        /// <summary>
        /// Removes a file from the index
        /// </summary>
        public void RemoveFile(string filePath)
        {
            lock (_indexLock)
            {
                var toRemove = new List<string>();
                
                foreach (var kvp in _searchIndex)
                {
                    var results = kvp.Value;
                    var toRemoveFromSet = results.Where(r => r.FilePath == filePath).ToList();
                    
                    foreach (var result in toRemoveFromSet)
                    {
                        results.Remove(result);
                        
                        // Remove from content cache
                        if (!string.IsNullOrEmpty(result.NoteId))
                        {
                            _contentCache.Remove(result.NoteId);
                        }
                    }
                    
                    if (!results.Any())
                    {
                        toRemove.Add(kvp.Key);
                    }
                }
                
                foreach (var key in toRemove)
                {
                    _searchIndex.Remove(key);
                }
            }
            
            _logger?.Debug($"Removed file from index: {filePath}");
        }

        /// <summary>
        /// Renames a file in the index
        /// </summary>
        public void RenameFile(string oldPath, string newPath)
        {
            lock (_indexLock)
            {
                foreach (var resultSet in _searchIndex.Values)
                {
                    var matchingResults = resultSet.Where(r => r.FilePath == oldPath).ToList();
                    foreach (var result in matchingResults)
                    {
                        result.FilePath = newPath;
                        result.Title = Path.GetFileNameWithoutExtension(newPath);
                    }
                }
            }
            
            _logger?.Debug($"Renamed file in index: {oldPath} -> {newPath}");
        }

        #endregion

        #region Enhanced Search Methods

        /// <summary>
        /// Search with word variants support
        /// </summary>
        public Task<List<SearchResult>> SearchAsync(
            HashSet<string> searchTerms, 
            CancellationToken cancellationToken = default)
        {
            var results = new List<SearchResult>();
            var resultScores = new Dictionary<string, (SearchResult result, float score)>();
            
            lock (_indexLock)
            {
                foreach (var term in searchTerms)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    
                    // Exact match
                    if (_searchIndex.TryGetValue(term.ToLowerInvariant(), out var exactMatches))
                    {
                        foreach (var result in exactMatches)
                        {
                            UpdateResultScore(resultScores, result, 1.0f);
                        }
                    }

                    // Prefix match
                    var prefixMatches = _searchIndex
                        .Where(kvp => kvp.Key.StartsWith(term.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                        .SelectMany(kvp => kvp.Value);

                    foreach (var result in prefixMatches)
                    {
                        UpdateResultScore(resultScores, result, 0.7f);
                    }

                    // Fuzzy match for longer terms
                    if (term.Length >= 3)
                    {
                        var fuzzyMatches = _searchIndex
                            .Where(kvp => kvp.Key.Contains(term.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase) 
                                         && !kvp.Key.StartsWith(term.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                            .SelectMany(kvp => kvp.Value);

                        foreach (var result in fuzzyMatches)
                        {
                            UpdateResultScore(resultScores, result, 0.5f);
                        }
                    }
                }
            }
            
            // Sort by score and return results
            results = resultScores
                .OrderByDescending(kvp => kvp.Value.score)
                .ThenBy(kvp => kvp.Value.result.Title)
                .Take(50)
                .Select(kvp => kvp.Value.result)
                .ToList();
            
            return Task.FromResult(results);
        }

        #endregion

        #region Helper Classes for Persistence

        private class SearchDocument
        {
            public string Id { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
            public List<string>? Tags { get; set; }
            public DateTime LastModified { get; set; }
        }

        #endregion
    }
}
