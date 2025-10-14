using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence
{
    /// <summary>
    /// Repository for managing global tag vocabulary.
    /// Used for autocomplete and tracking popular tags.
    /// </summary>
    public class GlobalTagRepository : IGlobalTagRepository
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;

        public GlobalTagRepository(string connectionString, IAppLogger logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<TagSuggestion>> GetPopularTagsAsync(int limit = 20)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT 
                        tag as Tag,
                        usage_count as UsageCount,
                        color as Color,
                        icon as Icon,
                        category as Category
                    FROM global_tags
                    WHERE usage_count > 0
                    ORDER BY usage_count DESC, tag ASC
                    LIMIT @Limit";

                var results = await connection.QueryAsync<TagSuggestion>(sql, new { Limit = limit });
                return results.ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"[GlobalTagRepository] GetPopularTagsAsync failed: {ex.Message}");
                throw;
            }
        }

        public async Task<List<TagSuggestion>> GetSuggestionsAsync(string prefix, int limit = 20)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT 
                        tag as Tag,
                        usage_count as UsageCount,
                        color as Color,
                        icon as Icon,
                        category as Category
                    FROM global_tags
                    WHERE tag LIKE @Prefix || '%'
                    ORDER BY usage_count DESC, tag ASC
                    LIMIT @Limit";

                var results = await connection.QueryAsync<TagSuggestion>(sql, new 
                { 
                    Prefix = prefix, 
                    Limit = limit 
                });

                return results.ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"[GlobalTagRepository] GetSuggestionsAsync failed: {ex.Message}");
                throw;
            }
        }

        public async Task IncrementUsageAsync(string tagName)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Use UPSERT (INSERT OR REPLACE) to handle new tags
                var sql = @"
                    INSERT INTO global_tags (tag, usage_count, created_at, color, category, icon)
                    VALUES (@Tag, 1, @CreatedAt, NULL, NULL, NULL)
                    ON CONFLICT(tag) DO UPDATE SET
                        usage_count = usage_count + 1";

                await connection.ExecuteAsync(sql, new
                {
                    Tag = tagName,
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });

                _logger.Debug($"[GlobalTagRepository] Incremented usage for tag '{tagName}'");
            }
            catch (Exception ex)
            {
                _logger.Error($"[GlobalTagRepository] IncrementUsageAsync failed: {ex.Message}");
                throw;
            }
        }

        public async Task DecrementUsageAsync(string tagName)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    UPDATE global_tags
                    SET usage_count = MAX(0, usage_count - 1)
                    WHERE tag = @Tag";

                var rowsAffected = await connection.ExecuteAsync(sql, new { Tag = tagName });

                _logger.Debug($"[GlobalTagRepository] Decremented usage for tag '{tagName}' ({rowsAffected} rows)");
            }
            catch (Exception ex)
            {
                _logger.Error($"[GlobalTagRepository] DecrementUsageAsync failed: {ex.Message}");
                throw;
            }
        }

        public async Task EnsureExistsAsync(string tagName)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    INSERT OR IGNORE INTO global_tags (tag, usage_count, created_at, color, category, icon)
                    VALUES (@Tag, 0, @CreatedAt, NULL, NULL, NULL)";

                await connection.ExecuteAsync(sql, new
                {
                    Tag = tagName,
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"[GlobalTagRepository] EnsureExistsAsync failed: {ex.Message}");
                throw;
            }
        }
    }
}

