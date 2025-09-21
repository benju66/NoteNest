using System;

namespace NoteNest.Core.Models.Search
{
    /// <summary>
    /// Search result model returned from FTS5 queries
    /// Contains document data plus search-specific metadata
    /// </summary>
    public class FtsSearchResult
    {
        /// <summary>
        /// Note unique identifier
        /// </summary>
        public string NoteId { get; set; } = string.Empty;

        /// <summary>
        /// Note title
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Plain text content (may be truncated)
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Pre-generated smart preview (optimized for UI display)
        /// </summary>
        public string ContentPreview { get; set; } = string.Empty;

        /// <summary>
        /// Category identifier for filtering
        /// </summary>
        public string CategoryId { get; set; } = string.Empty;

        /// <summary>
        /// Full file path for navigation
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Last modification timestamp
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// BM25 relevance score (lower = more relevant)
        /// FTS5 returns negative scores where lower values indicate higher relevance
        /// </summary>
        public double Relevance { get; set; }

        /// <summary>
        /// Snippet with highlighted search terms from FTS5 snippet() function
        /// Contains HTML-like markup: <mark>term</mark>
        /// </summary>
        public string Snippet { get; set; } = string.Empty;

        /// <summary>
        /// Usage count from metadata (for popularity ranking)
        /// </summary>
        public int UsageCount { get; set; }

        /// <summary>
        /// Last accessed timestamp (for recency boost)
        /// </summary>
        public DateTime LastAccessed { get; set; }

        /// <summary>
        /// File size in bytes
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// Creation date
        /// </summary>
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// Calculate composite relevance score combining BM25, usage, and recency
        /// Higher scores indicate better matches (inverted from FTS5's negative BM25)
        /// </summary>
        public double CalculateCompositeScore()
        {
            // Convert BM25 to positive score (lower BM25 = higher relevance)
            var baseScore = Math.Max(0, -Relevance);
            
            // Usage boost (frequently accessed notes get slight boost)
            var usageBoost = UsageCount * 0.1;
            
            // Recency boost (recently modified notes get slight boost)
            var daysSinceModified = (DateTime.Now - LastModified).TotalDays;
            var recencyBoost = daysSinceModified < 7 ? 0.5 :    // Last week
                              daysSinceModified < 30 ? 0.2 :   // Last month
                              daysSinceModified < 90 ? 0.1 :   // Last 3 months
                              0.0;                             // Older files
            
            return baseScore + usageBoost + recencyBoost;
        }

        /// <summary>
        /// Get preview text from snippet or content
        /// </summary>
        public string GetPreviewText(int maxLength = 200)
        {
            // Prefer snippet with highlights
            if (!string.IsNullOrEmpty(Snippet))
            {
                // Remove HTML-like markup for plain text preview if needed
                var preview = Snippet.Length > maxLength 
                    ? Snippet.Substring(0, maxLength) + "..."
                    : Snippet;
                return preview;
            }
            
            // Fallback to content
            if (!string.IsNullOrEmpty(Content))
            {
                var preview = Content.Length > maxLength
                    ? Content.Substring(0, maxLength) + "..."
                    : Content;
                return preview;
            }

            return string.Empty;
        }

        /// <summary>
        /// Create from FTS5 query result reader
        /// </summary>
        public static FtsSearchResult FromDataReader(Microsoft.Data.Sqlite.SqliteDataReader reader)
        {
            return new FtsSearchResult
            {
                NoteId = reader.IsDBNull(reader.GetOrdinal("note_id")) ? string.Empty : reader.GetString(reader.GetOrdinal("note_id")),
                Title = reader.IsDBNull(reader.GetOrdinal("title")) ? string.Empty : reader.GetString(reader.GetOrdinal("title")),
                Content = reader.IsDBNull(reader.GetOrdinal("content")) ? string.Empty : reader.GetString(reader.GetOrdinal("content")),
                ContentPreview = reader.IsDBNull(reader.GetOrdinal("content_preview")) ? string.Empty : reader.GetString(reader.GetOrdinal("content_preview")),
                CategoryId = reader.IsDBNull(reader.GetOrdinal("category_id")) ? string.Empty : reader.GetString(reader.GetOrdinal("category_id")),
                FilePath = reader.IsDBNull(reader.GetOrdinal("file_path")) ? string.Empty : reader.GetString(reader.GetOrdinal("file_path")),
                LastModified = reader.IsDBNull(reader.GetOrdinal("last_modified")) ? DateTime.MinValue : 
                              DateTime.TryParse(reader.GetString(reader.GetOrdinal("last_modified")), out var lastMod) ? lastMod : DateTime.MinValue,
                Relevance = reader.IsDBNull(reader.GetOrdinal("relevance")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("relevance")),
                Snippet = reader.IsDBNull(reader.GetOrdinal("snippet")) ? string.Empty : reader.GetString(reader.GetOrdinal("snippet")),
                UsageCount = reader.IsDBNull(reader.GetOrdinal("usage_count")) ? 0 : reader.GetInt32(reader.GetOrdinal("usage_count")),
                LastAccessed = reader.IsDBNull(reader.GetOrdinal("last_accessed")) ? DateTime.MinValue : 
                              DateTime.TryParse(reader.GetString(reader.GetOrdinal("last_accessed")), out var lastAcc) ? lastAcc : DateTime.MinValue,
                FileSize = reader.IsDBNull(reader.GetOrdinal("file_size")) ? 0 : reader.GetInt64(reader.GetOrdinal("file_size")),
                CreatedDate = reader.IsDBNull(reader.GetOrdinal("created_date")) ? DateTime.MinValue :
                             DateTime.TryParse(reader.GetString(reader.GetOrdinal("created_date")), out var created) ? created : DateTime.MinValue
            };
        }
    }
}
