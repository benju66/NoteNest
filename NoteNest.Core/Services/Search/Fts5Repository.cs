using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NoteNest.Core.Interfaces.Search;
using NoteNest.Core.Models.Search;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Search
{
    /// <summary>
    /// SQLite FTS5 repository implementation for search operations
    /// Single Responsibility: Database operations for search documents
    /// </summary>
    public class Fts5Repository : IFts5Repository
    {
        private readonly IAppLogger? _logger;
        private SqliteConnection? _connection;
        private string? _databasePath;
        private bool _disposed = false;

        public string? DatabasePath => _databasePath;
        public bool IsInitialized => _connection != null && _connection.State == System.Data.ConnectionState.Open;

        public Fts5Repository(IAppLogger? logger = null)
        {
            _logger = logger;
        }

        #region Database Lifecycle

        public async Task InitializeAsync(string databasePath)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Fts5Repository));

            if (IsInitialized && _databasePath == databasePath)
                return; // Already initialized with same path

            try
            {
                // Close existing connection if different path
                if (IsInitialized && _databasePath != databasePath)
                {
                    await _connection!.CloseAsync();
                    _connection.Dispose();
                    _connection = null;
                }

                _databasePath = databasePath;

                // Initialize database schema
                await DatabaseConfig.InitializeDatabaseAsync(databasePath, _logger);

                // Create and configure connection
                _connection = await DatabaseConfig.CreateConnectionAsync(databasePath);

                _logger?.Info($"FTS5 repository initialized: {databasePath}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to initialize FTS5 repository: {databasePath}");
                throw;
            }
        }

        public async Task<bool> DatabaseExistsAsync(string databasePath)
        {
            try
            {
                return await DatabaseConfig.ValidateDatabaseAsync(databasePath, _logger);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Database validation failed: {databasePath} - {ex.Message}");
                return false;
            }
        }

        public async Task CreateDatabaseAsync(string databasePath)
        {
            try
            {
                // Delete existing database
                if (File.Exists(databasePath))
                {
                    File.Delete(databasePath);
                    _logger?.Info($"Deleted existing database: {databasePath}");
                }

                // Create fresh database
                await DatabaseConfig.InitializeDatabaseAsync(databasePath, _logger);
                _logger?.Info($"Created new FTS5 database: {databasePath}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to create database: {databasePath}");
                throw;
            }
        }

        #endregion

        #region Document Management

        public async Task IndexDocumentAsync(SearchDocument document)
        {
            ThrowIfNotInitialized();

            // Defensive validation
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (string.IsNullOrEmpty(document.NoteId))
                throw new ArgumentException("NoteId cannot be empty", nameof(document));
            if (string.IsNullOrEmpty(document.FilePath))
                throw new ArgumentException("FilePath cannot be empty", nameof(document));

            _logger?.Debug($"Indexing document: ID={document.NoteId}, Title='{document.Title}', Path='{document.FilePath}'");

            try
            {
                const string sql = @"
                    INSERT OR REPLACE INTO notes_fts (title, content, category_id, file_path, note_id, last_modified)
                    VALUES (@title, @content, @category_id, @file_path, @note_id, @last_modified)";

                using var command = _connection!.CreateCommand();
                command.CommandText = sql;

                // Use named parameters matching the SQL exactly
                command.Parameters.AddWithValue("@title", document.Title ?? "");
                command.Parameters.AddWithValue("@content", document.Content ?? "");
                command.Parameters.AddWithValue("@category_id", document.CategoryId ?? "");
                command.Parameters.AddWithValue("@file_path", document.FilePath ?? "");
                command.Parameters.AddWithValue("@note_id", document.NoteId ?? "");
                command.Parameters.AddWithValue("@last_modified", document.LastModified.ToString("O"));

                var rowsAffected = await command.ExecuteNonQueryAsync();

                // Also update metadata table
                await UpsertMetadataAsync(document);

                _logger?.Debug($"Indexed document: {document.NoteId} ({document.Title})");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to index document: {document.NoteId}");
                throw;
            }
        }

        public async Task UpdateDocumentAsync(SearchDocument document)
        {
            // For FTS5, update is same as index (INSERT OR REPLACE)
            await IndexDocumentAsync(document);
        }

        public async Task RemoveDocumentAsync(string noteId)
        {
            ThrowIfNotInitialized();

            try
            {
                const string ftsDeleteSql = "DELETE FROM notes_fts WHERE note_id = ?";
                const string metadataDeleteSql = "DELETE FROM note_metadata WHERE note_id = ?";

                using var transaction = _connection!.BeginTransaction();

                // Remove from FTS table
                using var ftsCommand = _connection.CreateCommand();
                ftsCommand.Transaction = transaction;
                ftsCommand.CommandText = ftsDeleteSql;
                ftsCommand.Parameters.AddWithValue("@noteId", noteId);
                await ftsCommand.ExecuteNonQueryAsync();

                // Remove from metadata table
                using var metaCommand = _connection.CreateCommand();
                metaCommand.Transaction = transaction;
                metaCommand.CommandText = metadataDeleteSql;
                metaCommand.Parameters.AddWithValue("@noteId", noteId);
                await metaCommand.ExecuteNonQueryAsync();

                await transaction.CommitAsync();

                _logger?.Debug($"Removed document: {noteId}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to remove document: {noteId}");
                throw;
            }
        }

        public async Task RemoveByFilePathAsync(string filePath)
        {
            ThrowIfNotInitialized();

            try
            {
                const string sql = "DELETE FROM notes_fts WHERE file_path = @file_path";

                using var command = _connection!.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("@file_path", filePath);

                var rowsAffected = await command.ExecuteNonQueryAsync();
                _logger?.Debug($"Removed document by path: {filePath} ({rowsAffected} rows)");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to remove document by path: {filePath}");
                throw;
            }
        }

        public async Task<bool> DocumentExistsAsync(string noteId)
        {
            ThrowIfNotInitialized();

            try
            {
                const string sql = "SELECT 1 FROM notes_fts WHERE note_id = ? LIMIT 1";

                using var command = _connection!.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("@noteId", noteId);

                var result = await command.ExecuteScalarAsync();
                return result != null;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to check document existence: {noteId}");
                return false;
            }
        }

        #endregion

        #region Search Operations

        public async Task<List<FtsSearchResult>> SearchAsync(string query, SearchOptions? options = null)
        {
            ThrowIfNotInitialized();

            if (string.IsNullOrWhiteSpace(query))
                return new List<FtsSearchResult>();

            options ??= SearchOptions.Default;
            options.Validate();

            try
            {
                var startTime = DateTime.Now;
                var sql = BuildSearchQuery(options);
                var ftsQuery = ProcessSearchQuery(query);

                using var command = _connection!.CreateCommand();
                command.CommandText = sql;
                
                // Add parameters
                var paramIndex = 0;
                command.Parameters.AddWithValue($"@query{paramIndex++}", ftsQuery);

                if (options.CategoryFilter != null)
                    command.Parameters.AddWithValue($"@category{paramIndex++}", options.CategoryFilter);

                if (options.ModifiedAfter.HasValue)
                    command.Parameters.AddWithValue($"@modifiedAfter{paramIndex++}", options.ModifiedAfter.Value.ToString("O"));

                if (options.ModifiedBefore.HasValue)
                    command.Parameters.AddWithValue($"@modifiedBefore{paramIndex++}", options.ModifiedBefore.Value.ToString("O"));

                var results = new List<FtsSearchResult>();

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var result = FtsSearchResult.FromDataReader(reader);
                    results.Add(result);
                }

                var searchTime = (DateTime.Now - startTime).TotalMilliseconds;
                _logger?.Debug($"FTS5 search completed: '{query}' -> {results.Count} results in {searchTime:F1}ms");

                return results;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"FTS5 search failed: '{query}'");
                return new List<FtsSearchResult>();
            }
        }

        public async Task<List<string>> GetSuggestionsAsync(string partialQuery, int maxResults = 10)
        {
            ThrowIfNotInitialized();

            if (string.IsNullOrWhiteSpace(partialQuery) || partialQuery.Length < 2)
                return new List<string>();

            try
            {
                // Use FTS5 to find documents matching partial query, extract unique terms
                const string sql = @"
                    SELECT DISTINCT title 
                    FROM notes_fts 
                    WHERE notes_fts MATCH ? 
                    ORDER BY bm25(notes_fts) 
                    LIMIT ?";

                var ftsQuery = $"{partialQuery.Trim()}*"; // Prefix search

                using var command = _connection!.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("@query", ftsQuery);
                command.Parameters.AddWithValue("@limit", maxResults);

                var suggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var title = reader.IsDBNull(reader.GetOrdinal("title")) ? string.Empty : reader.GetString(reader.GetOrdinal("title"));
                    if (!string.IsNullOrEmpty(title))
                    {
                        // Extract words that start with partial query
                        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var word in words)
                        {
                            if (word.StartsWith(partialQuery, StringComparison.OrdinalIgnoreCase) && 
                                word.Length > partialQuery.Length)
                            {
                                suggestions.Add(word);
                                if (suggestions.Count >= maxResults) break;
                            }
                        }
                        if (suggestions.Count >= maxResults) break;
                    }
                }

                return suggestions.Take(maxResults).ToList();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to get suggestions for: '{partialQuery}'");
                return new List<string>();
            }
        }

        public async Task<List<FtsSearchResult>> SearchInCategoryAsync(string query, string categoryId, int maxResults = 50)
        {
            var options = SearchOptions.ForCategory(categoryId);
            options.MaxResults = maxResults;
            return await SearchAsync(query, options);
        }

        #endregion

        #region Batch Operations

        public async Task IndexDocumentsBatchAsync(IEnumerable<SearchDocument> documents)
        {
            ThrowIfNotInitialized();

            if (documents == null)
                throw new ArgumentNullException(nameof(documents));

            var documentList = documents.ToList();
            if (!documentList.Any()) 
            {
                _logger?.Debug("No documents to index in batch");
                return;
            }

            // Validate all documents before starting transaction
            foreach (var doc in documentList)
            {
                if (string.IsNullOrEmpty(doc.NoteId))
                    throw new ArgumentException($"Document with empty NoteId found in batch");
                if (string.IsNullOrEmpty(doc.FilePath))
                    throw new ArgumentException($"Document {doc.NoteId} has empty FilePath");
            }

            _logger?.Debug($"Starting batch index of {documentList.Count} documents");

            try
            {
                using var transaction = _connection!.BeginTransaction();

                // Use named parameters for clarity and reliability
                const string sql = @"
                    INSERT OR REPLACE INTO notes_fts (title, content, category_id, file_path, note_id, last_modified)
                    VALUES (@title, @content, @category_id, @file_path, @note_id, @last_modified)";

                foreach (var document in documentList)
                {
                    using var command = _connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = sql;

                    // Use named parameters matching the SQL exactly
                    command.Parameters.AddWithValue("@title", document.Title ?? "");
                    command.Parameters.AddWithValue("@content", document.Content ?? "");
                    command.Parameters.AddWithValue("@category_id", document.CategoryId ?? "");
                    command.Parameters.AddWithValue("@file_path", document.FilePath ?? "");
                    command.Parameters.AddWithValue("@note_id", document.NoteId ?? "");
                    command.Parameters.AddWithValue("@last_modified", document.LastModified.ToString("O"));

                    await command.ExecuteNonQueryAsync();

                    // Update metadata
                    await UpsertMetadataAsync(document, transaction);
                }

                await transaction.CommitAsync();

                _logger?.Info($"Batch indexed {documentList.Count} documents");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to batch index {documentList.Count} documents");
                throw;
            }
        }

        public async Task ClearIndexAsync()
        {
            ThrowIfNotInitialized();

            try
            {
                using var transaction = _connection!.BeginTransaction();

                // Clear FTS table
                using var ftsCommand = _connection.CreateCommand();
                ftsCommand.Transaction = transaction;
                ftsCommand.CommandText = "DELETE FROM notes_fts";
                await ftsCommand.ExecuteNonQueryAsync();

                // Clear metadata table
                using var metaCommand = _connection.CreateCommand();
                metaCommand.Transaction = transaction;
                metaCommand.CommandText = "DELETE FROM note_metadata";
                await metaCommand.ExecuteNonQueryAsync();

                await transaction.CommitAsync();

                _logger?.Info("Search index cleared");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to clear search index");
                throw;
            }
        }

        public async Task OptimizeIndexAsync()
        {
            ThrowIfNotInitialized();

            try
            {
                // FTS5 optimize command
                using var command = _connection!.CreateCommand();
                command.CommandText = "INSERT INTO notes_fts(notes_fts) VALUES('optimize')";
                await command.ExecuteNonQueryAsync();

                // SQLite optimize
                using var optimizeCommand = _connection.CreateCommand();
                optimizeCommand.CommandText = "PRAGMA optimize";
                await optimizeCommand.ExecuteNonQueryAsync();

                _logger?.Info("Search index optimized");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to optimize search index");
                throw;
            }
        }

        #endregion

        #region Metadata Operations

        public async Task UpdateUsageStatsAsync(string noteId)
        {
            ThrowIfNotInitialized();

            try
            {
                const string sql = @"
                    INSERT INTO note_metadata (note_id, usage_count, last_accessed)
                    VALUES (@note_id, 1, @last_accessed)
                    ON CONFLICT(note_id) DO UPDATE SET
                        usage_count = usage_count + 1,
                        last_accessed = @last_accessed2";

                var now = DateTime.Now.ToString("O");

                using var command = _connection!.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("@note_id", noteId);
                command.Parameters.AddWithValue("@last_accessed", now);
                command.Parameters.AddWithValue("@last_accessed2", now);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to update usage stats for {noteId}: {ex.Message}");
                // Don't throw - usage stats are non-critical
            }
        }

        public async Task<SearchStatistics> GetStatisticsAsync()
        {
            ThrowIfNotInitialized();

            try
            {
                var stats = new SearchStatistics();

                // Get document count
                using var countCommand = _connection!.CreateCommand();
                countCommand.CommandText = "SELECT COUNT(*) FROM notes_fts";
                var countResult = await countCommand.ExecuteScalarAsync();
                stats.TotalDocuments = Convert.ToInt32(countResult);

                // Get database size
                stats.DatabaseSizeBytes = await GetDatabaseSizeAsync();

                // Get average search time (placeholder - would need to track this)
                stats.SearchTimeMs = 0;

                return stats;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to get search statistics");
                return new SearchStatistics();
            }
        }

        public async Task<int> GetDocumentCountAsync()
        {
            ThrowIfNotInitialized();

            try
            {
                using var command = _connection!.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM notes_fts";
                var result = await command.ExecuteScalarAsync();
                return result != null ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to get document count");
                return 0;
            }
        }

        public async Task<long> GetDatabaseSizeAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_databasePath) || !File.Exists(_databasePath))
                    return -1;

                return await Task.Run(() => new FileInfo(_databasePath).Length);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to get database size");
                return -1;
            }
        }

        #endregion

        #region Maintenance Operations

        public async Task RebuildIndexAsync()
        {
            await ClearIndexAsync();
        }

        public async Task PerformMaintenanceAsync()
        {
            ThrowIfNotInitialized();

            try
            {
                // Vacuum database
                using var vacuumCommand = _connection!.CreateCommand();
                vacuumCommand.CommandText = "VACUUM";
                await vacuumCommand.ExecuteNonQueryAsync();

                // Analyze for query optimization
                using var analyzeCommand = _connection.CreateCommand();
                analyzeCommand.CommandText = "ANALYZE";
                await analyzeCommand.ExecuteNonQueryAsync();

                // Optimize FTS5 index
                await OptimizeIndexAsync();

                _logger?.Info("Database maintenance completed");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Database maintenance failed");
                throw;
            }
        }

        public async Task<bool> ValidateIndexIntegrityAsync()
        {
            ThrowIfNotInitialized();

            try
            {
                // Check FTS5 integrity
                using var integrityCommand = _connection!.CreateCommand();
                integrityCommand.CommandText = "INSERT INTO notes_fts(notes_fts) VALUES('integrity-check')";
                await integrityCommand.ExecuteNonQueryAsync();

                // Basic count check
                var count = await GetDocumentCountAsync();
                
                return count >= 0; // Basic validation - more sophisticated checks could be added
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Index integrity validation failed");
                return false;
            }
        }

        #endregion

        #region Private Helper Methods

        private void ThrowIfNotInitialized()
        {
            if (!IsInitialized)
                throw new InvalidOperationException("Repository is not initialized. Call InitializeAsync first.");
        }

        private string BuildSearchQuery(SearchOptions options)
        {
            var sql = new StringBuilder();
            sql.AppendLine("SELECT");
            sql.AppendLine("    notes_fts.note_id,");
            sql.AppendLine("    notes_fts.title,");
            if (options.IncludeContent)
                sql.AppendLine("    notes_fts.content,");
            else
                sql.AppendLine("    '' as content,");
            sql.AppendLine("    notes_fts.content_preview,");  // Always include pre-generated preview
            sql.AppendLine("    notes_fts.category_id,");
            sql.AppendLine("    notes_fts.file_path,");
            sql.AppendLine("    notes_fts.last_modified,");
            sql.AppendLine("    bm25(notes_fts) as relevance,");
            
            if (options.HighlightSnippets)
                sql.AppendLine($"    snippet(notes_fts, 0, '<mark>', '</mark>', '...', {options.SnippetContextWords}) as snippet,");
            else
                sql.AppendLine("    '' as snippet,");
                
            sql.AppendLine("    COALESCE(m.usage_count, 0) as usage_count,");
            sql.AppendLine("    COALESCE(m.last_accessed, '') as last_accessed,");
            sql.AppendLine("    COALESCE(m.file_size, 0) as file_size,");
            sql.AppendLine("    COALESCE(m.created_date, '') as created_date");
            sql.AppendLine("FROM notes_fts");
            sql.AppendLine("LEFT JOIN note_metadata m ON notes_fts.note_id = m.note_id");
            sql.AppendLine("WHERE notes_fts MATCH @query0");

            var paramIndex = 1;

            // Add filters
            if (options.CategoryFilter != null)
                sql.AppendLine($"AND notes_fts.category_id = @category{paramIndex++}");

            if (options.ModifiedAfter.HasValue)
                sql.AppendLine($"AND notes_fts.last_modified >= @modifiedAfter{paramIndex++}");

            if (options.ModifiedBefore.HasValue)
                sql.AppendLine($"AND notes_fts.last_modified <= @modifiedBefore{paramIndex++}");

            // Add ordering
            switch (options.SortOrder)
            {
                case SearchSortOrder.Relevance:
                    sql.AppendLine("ORDER BY (bm25(notes_fts) + (COALESCE(m.usage_count, 0) * 0.1)) ASC");
                    break;
                case SearchSortOrder.LastModified:
                    sql.AppendLine("ORDER BY notes_fts.last_modified DESC");
                    break;
                case SearchSortOrder.Usage:
                    sql.AppendLine("ORDER BY COALESCE(m.usage_count, 0) DESC, bm25(notes_fts) ASC");
                    break;
                case SearchSortOrder.Title:
                    sql.AppendLine("ORDER BY notes_fts.title ASC");
                    break;
                case SearchSortOrder.CreatedDate:
                    sql.AppendLine("ORDER BY COALESCE(m.created_date, '') DESC");
                    break;
                case SearchSortOrder.FileSize:
                    sql.AppendLine("ORDER BY COALESCE(m.file_size, 0) DESC");
                    break;
            }

            sql.AppendLine($"LIMIT {options.MaxResults}");

            return sql.ToString();
        }

        private static string ProcessSearchQuery(string query)
        {
            query = query.Trim();

            // Handle phrase queries (already quoted)
            if (query.StartsWith("\"") && query.EndsWith("\""))
                return query;

            // Handle single terms - add prefix matching
            var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 1)
                return $"{terms[0]}*";

            // Multiple terms - create AND query with prefix matching
            var processedTerms = terms.Select(t => $"{t}*");
            return string.Join(" AND ", processedTerms);
        }

        private async Task UpsertMetadataAsync(SearchDocument document, SqliteTransaction? transaction = null)
        {
            const string sql = @"
                INSERT INTO note_metadata (note_id, file_size, created_date, usage_count, last_accessed)
                VALUES (@note_id, @file_size, @created_date, 0, @last_accessed)
                ON CONFLICT(note_id) DO UPDATE SET
                    file_size = @file_size2,
                    created_date = @created_date2";

            using var command = _connection!.CreateCommand();
            if (transaction != null)
                command.Transaction = transaction;

            command.CommandText = sql;
            command.Parameters.AddWithValue("@note_id", document.NoteId);
            command.Parameters.AddWithValue("@file_size", document.FileSize);
            command.Parameters.AddWithValue("@created_date", document.CreatedDate.ToString("O"));
            command.Parameters.AddWithValue("@last_accessed", DateTime.Now.ToString("O"));
            command.Parameters.AddWithValue("@file_size2", document.FileSize);
            command.Parameters.AddWithValue("@created_date2", document.CreatedDate.ToString("O"));

            await command.ExecuteNonQueryAsync();
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                try
                {
                    _connection?.Close();
                    _connection?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Error disposing FTS5 repository: {ex.Message}");
                }
                finally
                {
                    _connection = null;
                    _disposed = true;
                }
            }
        }

        #endregion
    }
}
