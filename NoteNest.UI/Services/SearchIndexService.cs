using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NoteNest.Core.Models;
using NoteNest.Core.Interfaces.Services;

namespace NoteNest.Core.Services
{
    public class SearchIndexService
    {
        private readonly Dictionary<string, HashSet<SearchResult>> _searchIndex;
        private readonly object _indexLock = new object();
        private DateTime _lastIndexTime;
        private bool _indexDirty = true;
        private readonly int _contentWordLimit;
        private readonly IMarkdownService _markdownService;

        public SearchIndexService(int contentWordLimit = 500, IMarkdownService markdownService = null)
        {
            _searchIndex = new Dictionary<string, HashSet<SearchResult>>(StringComparer.OrdinalIgnoreCase);
            _contentWordLimit = contentWordLimit > 100 ? contentWordLimit : 500;
            _markdownService = markdownService ?? new NoteNest.Core.Services.MarkdownService(null);
        }

        public class SearchResult
        {
            public string NoteId { get; set; }
            public string Title { get; set; }
            public string FilePath { get; set; }
            public string CategoryId { get; set; }
            public string Preview { get; set; }
            public float Relevance { get; set; }
        }

        public void BuildIndex(List<CategoryModel> categories, List<NoteModel> allNotes)
        {
            lock (_indexLock)
            {
                _searchIndex.Clear();
                
                System.Diagnostics.Debug.WriteLine($"[SearchIndex] Building index with {allNotes.Count} notes and {categories.Count} categories");

                // Use ParallelOptions to limit parallelism and prevent thread pool exhaustion
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };
                
                // Index all notes
                var notesIndexed = 0;
                Parallel.ForEach(allNotes, parallelOptions, note =>
                {
                    IndexNote(note);
                    System.Threading.Interlocked.Increment(ref notesIndexed);
                });
                
                System.Diagnostics.Debug.WriteLine($"[SearchIndex] Indexed {notesIndexed} notes");

                // Index categories (sequential - they're usually few)
                foreach (var category in categories)
                {
                    IndexCategory(category);
                }
                
                System.Diagnostics.Debug.WriteLine($"[SearchIndex] Total index entries: {_searchIndex.Count}");
                
                _lastIndexTime = DateTime.Now;
                _indexDirty = false;
            }
        }

        private void IndexNote(NoteModel note)
        {
            if (note == null) return;

            var result = new SearchResult
            {
                NoteId = note.Id,
                Title = note.Title,
                FilePath = note.FilePath,
                CategoryId = note.CategoryId,
                Preview = GetPreview(note.Content),
                Relevance = 1.0f
            };
            
            // Debug logging
            System.Diagnostics.Debug.WriteLine($"[SearchIndex] Indexing note: Title='{note.Title}', Content length={note.Content?.Length ?? 0}, Id='{note.Id}'");

            // Index title words
            var titleWords = TokenizeText(note.Title);
            foreach (var word in titleWords)
            {
                AddToIndex(word, result, 2.0f); // Higher weight for title matches
            }

            // Index content words (if loaded)
            if (!string.IsNullOrEmpty(note.Content))
            {
                var contentToIndex = note.Content;
                if (note.Format == NoteFormat.Markdown)
                {
                    contentToIndex = _markdownService.StripMarkdownForIndex(contentToIndex);
                }
                var contentWords = TokenizeText(contentToIndex);
                System.Diagnostics.Debug.WriteLine($"[SearchIndex] Content has {contentWords.Count} unique words");
                foreach (var word in contentWords.Take(_contentWordLimit)) // Configurable limit
                {
                    AddToIndex(word, result, 1.0f);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[SearchIndex] Note '{note.Title}' has no content to index");
            }

            // Index file name without extension
            var fileName = System.IO.Path.GetFileNameWithoutExtension(note.FilePath);
            var fileWords = TokenizeText(fileName);
            foreach (var word in fileWords)
            {
                AddToIndex(word, result, 1.5f);
            }
        }

        private void IndexCategory(CategoryModel category)
        {
            if (category == null) return;

            var result = new SearchResult
            {
                CategoryId = category.Id,
                Title = category.Name,
                FilePath = category.Path,
                Relevance = 0.8f
            };

            var words = TokenizeText(category.Name);
            foreach (var word in words)
            {
                AddToIndex(word, result, 1.0f);
            }

            // Index tags
            foreach (var tag in category.Tags ?? Enumerable.Empty<string>())
            {
                AddToIndex(tag, result, 1.2f);
            }
        }

        private void AddToIndex(string word, SearchResult result, float weight)
        {
            if (string.IsNullOrWhiteSpace(word) || word.Length < 2) return;

            lock (_indexLock)
            {
                if (!_searchIndex.TryGetValue(word, out var results))
                {
                    results = new HashSet<SearchResult>();
                    _searchIndex[word] = results;
                }

                result.Relevance *= weight;
                results.Add(result);
            }
        }

        public List<SearchResult> Search(string query, int maxResults = 50)
        {
            if (string.IsNullOrWhiteSpace(query)) 
                return new List<SearchResult>();

            System.Diagnostics.Debug.WriteLine($"[SearchIndex] Searching for: '{query}'");

            var queryWords = TokenizeText(query);
            System.Diagnostics.Debug.WriteLine($"[SearchIndex] Query tokenized into {queryWords.Count} words: {string.Join(", ", queryWords)}");
            
            var resultScores = new Dictionary<string, (SearchResult result, float score)>();

            lock (_indexLock)
            {
                System.Diagnostics.Debug.WriteLine($"[SearchIndex] Index contains {_searchIndex.Count} unique words");
                
                foreach (var word in queryWords)
                {
                    // Exact match
                    if (_searchIndex.TryGetValue(word, out var exactMatches))
                    {
                        System.Diagnostics.Debug.WriteLine($"[SearchIndex] Found {exactMatches.Count} exact matches for '{word}'");
                        foreach (var result in exactMatches)
                        {
                            UpdateResultScore(resultScores, result, 1.0f);
                        }
                    }

                    // Prefix match for autocomplete
                    var prefixMatches = _searchIndex
                        .Where(kvp => kvp.Key.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                        .SelectMany(kvp => kvp.Value);

                    var prefixCount = prefixMatches.Count();
                    if (prefixCount > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SearchIndex] Found {prefixCount} prefix matches for '{word}'");
                    }

                    foreach (var result in prefixMatches)
                    {
                        UpdateResultScore(resultScores, result, 0.7f);
                    }

                    // Fuzzy match (contains)
                    if (word.Length >= 3)
                    {
                        var fuzzyMatches = _searchIndex
                            .Where(kvp => kvp.Key.Contains(word, StringComparison.OrdinalIgnoreCase))
                            .SelectMany(kvp => kvp.Value);

                        var fuzzyCount = fuzzyMatches.Count();
                        if (fuzzyCount > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SearchIndex] Found {fuzzyCount} fuzzy matches for '{word}'");
                        }

                        foreach (var result in fuzzyMatches)
                        {
                            UpdateResultScore(resultScores, result, 0.5f);
                        }
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[SearchIndex] Total unique results: {resultScores.Count}");

            // Sort by score and return top results
            var finalResults = resultScores
                .OrderByDescending(kvp => kvp.Value.score)
                .Take(maxResults)
                .Select(kvp => kvp.Value.result)
                .ToList();
                
            System.Diagnostics.Debug.WriteLine($"[SearchIndex] Returning {finalResults.Count} results");
            
            return finalResults;
        }

        public async Task<List<SearchResult>> SearchAsync(string query, int maxResults = 50, System.Threading.CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<SearchResult>();
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var results = Search(query, maxResults);
                cancellationToken.ThrowIfCancellationRequested();
                return results;
            }, cancellationToken);
        }

        private void UpdateResultScore(Dictionary<string, (SearchResult, float)> scores, 
            SearchResult result, float weight)
        {
            var key = result.NoteId ?? result.CategoryId;
            if (scores.TryGetValue(key, out var existing))
            {
                scores[key] = (existing.Item1, existing.Item2 + (result.Relevance * weight));
            }
            else
            {
                scores[key] = (result, result.Relevance * weight);
            }
        }

        private HashSet<string> TokenizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new HashSet<string>();

            // Simple tokenization - can be enhanced with proper NLP
            var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();

            foreach (char c in text.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
                else if (sb.Length > 0)
                {
                    words.Add(sb.ToString());
                    sb.Clear();
                }
            }

            if (sb.Length > 0)
                words.Add(sb.ToString());

            return words;
        }

        private string GetPreview(string content, int maxLength = 150)
        {
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;

            var preview = content.Length > maxLength 
                ? content.Substring(0, maxLength) + "..." 
                : content;

            // Clean up whitespace
            return System.Text.RegularExpressions.Regex.Replace(preview, @"\s+", " ").Trim();
        }

        public void MarkDirty()
        {
            _indexDirty = true;
        }

        public bool NeedsReindex => _indexDirty || 
            (DateTime.Now - _lastIndexTime).TotalMinutes > 30;
    }
}