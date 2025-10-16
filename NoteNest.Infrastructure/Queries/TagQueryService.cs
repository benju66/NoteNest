using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Queries;

namespace NoteNest.Infrastructure.Queries
{
    /// <summary>
    /// Query service for tag projection.
    /// Queries projections.db for unified tag data.
    /// </summary>
    public class TagQueryService : ITagQueryService
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;

        public TagQueryService(string projectionsConnectionString, IAppLogger logger)
        {
            _connectionString = projectionsConnectionString ?? throw new ArgumentNullException(nameof(projectionsConnectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<TagDto>> GetTagsForEntityAsync(Guid entityId, string entityType)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var tags = await connection.QueryAsync<TagDtoDb>(
                    @"SELECT tag, display_name, source, created_at
                      FROM entity_tags
                      WHERE entity_id = @EntityId AND entity_type = @EntityType
                      ORDER BY display_name",
                    new { EntityId = entityId.ToString(), EntityType = entityType });

                return tags.Select(t => new TagDto
                {
                    Tag = t.Tag,
                    DisplayName = t.DisplayName,
                    Source = t.Source,
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(t.CreatedAt).DateTime
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get tags for entity {entityId}", ex);
                return new List<TagDto>();
            }
        }

        public async Task<List<string>> GetAllTagsAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var tags = await connection.QueryAsync<string>(
                    "SELECT DISTINCT display_name FROM tag_vocabulary ORDER BY display_name");

                return tags.ToList();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to get all tags", ex);
                return new List<string>();
            }
        }

        public async Task<Dictionary<string, int>> GetTagCloudAsync(int topN = 50)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var tags = await connection.QueryAsync<TagCloudItem>(
                    @"SELECT display_name, usage_count
                      FROM tag_vocabulary
                      WHERE usage_count > 0
                      ORDER BY usage_count DESC, display_name
                      LIMIT @TopN",
                    new { TopN = topN });

                return tags.ToDictionary(t => t.DisplayName, t => t.UsageCount);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to get tag cloud", ex);
                return new Dictionary<string, int>();
            }
        }

        public async Task<List<TagSuggestion>> GetTagSuggestionsAsync(string prefix = "", int limit = 20)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var tags = await connection.QueryAsync<TagSuggestionDb>(
                    @"SELECT tag, display_name, usage_count
                      FROM tag_vocabulary
                      WHERE tag LIKE @Prefix || '%'
                      ORDER BY usage_count DESC, display_name
                      LIMIT @Limit",
                    new { Prefix = prefix.ToLowerInvariant(), Limit = limit });

                return tags.Select(t => new TagSuggestion
                {
                    Tag = t.Tag,
                    DisplayName = t.DisplayName,
                    UsageCount = t.UsageCount
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to get tag suggestions", ex);
                return new List<TagSuggestion>();
            }
        }

        public async Task<List<EntityWithTag>> SearchByTagAsync(string tag)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var results = await connection.QueryAsync<EntityWithTagDb>(
                    @"SELECT entity_id, entity_type, tag
                      FROM entity_tags
                      WHERE tag = @Tag
                      ORDER BY entity_type, entity_id",
                    new { Tag = tag.ToLowerInvariant() });

                return results.Select(r => new EntityWithTag
                {
                    EntityId = Guid.Parse(r.EntityId),
                    EntityType = r.EntityType,
                    Tag = r.Tag
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to search by tag: {tag}", ex);
                return new List<EntityWithTag>();
            }
        }

        public async Task<List<TagSuggestion>> GetPopularTagsAsync(string entityType, int limit = 10)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var tags = await connection.QueryAsync<TagSuggestionDb>(
                    @"SELECT et.tag, et.display_name, COUNT(*) as usage_count
                      FROM entity_tags et
                      WHERE et.entity_type = @EntityType
                      GROUP BY et.tag
                      ORDER BY usage_count DESC, et.display_name
                      LIMIT @Limit",
                    new { EntityType = entityType, Limit = limit });

                return tags.Select(t => new TagSuggestion
                {
                    Tag = t.Tag,
                    DisplayName = t.DisplayName,
                    UsageCount = t.UsageCount
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get popular tags for {entityType}", ex);
                return new List<TagSuggestion>();
            }
        }

        // DTOs for Dapper
        private class TagDtoDb
        {
            public string Tag { get; set; }
            public string DisplayName { get; set; }
            public string Source { get; set; }
            public long CreatedAt { get; set; }
        }

        private class TagCloudItem
        {
            public string DisplayName { get; set; }
            public int UsageCount { get; set; }
        }

        private class TagSuggestionDb
        {
            public string Tag { get; set; }
            public string DisplayName { get; set; }
            public int UsageCount { get; set; }
        }

        private class EntityWithTagDb
        {
            public string EntityId { get; set; }
            public string EntityType { get; set; }
            public string Tag { get; set; }
        }
    }
}

