using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Tags.Services;

namespace NoteNest.Infrastructure.Services
{
    /// <summary>
    /// Implementation of unified tag view that respects database isolation.
    /// Queries tree.db for folders/notes and todos.db for todos.
    /// </summary>
    public class UnifiedTagViewService : IUnifiedTagViewService
    {
        private readonly string _treeDbConnectionString;
        private readonly string _todosDbConnectionString;
        private readonly IAppLogger _logger;

        public UnifiedTagViewService(string treeDbConnectionString, string todosDbConnectionString, IAppLogger logger)
        {
            _treeDbConnectionString = treeDbConnectionString ?? throw new ArgumentNullException(nameof(treeDbConnectionString));
            _todosDbConnectionString = todosDbConnectionString ?? throw new ArgumentNullException(nameof(todosDbConnectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<TagInfo>> GetEntityTagsAsync(Guid entityId, string entityType)
        {
            try
            {
                var connectionString = GetConnectionString(entityType);
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                if (entityType == "todo")
                {
                    // Query todos.db for todo tags
                    var sql = @"SELECT 
                        tag as TagName,
                        display_name as DisplayName,
                        source as Source,
                        created_at as CreatedAt
                    FROM todo_tags
                    WHERE todo_id = @EntityId
                    ORDER BY display_name";

                    var tags = await connection.QueryAsync<TagInfo>(sql, new { EntityId = entityId.ToString() });
                    return tags.ToList();
                }
                else
                {
                    // Query tree.db for folder/note tags
                    // Query appropriate table based on entity type
                    string tableName = entityType == "folder" ? "folder_tags" : "note_tags";
                    string idColumn = entityType == "folder" ? "folder_id" : "note_id";
                    
                    var sql = $@"SELECT 
                        tag as TagName,
                        tag as DisplayName,
                        'manual' as Source,
                        created_at as CreatedAt
                    FROM {tableName}
                    WHERE {idColumn} = @EntityId
                    ORDER BY tag";

                    var tags = await connection.QueryAsync<TagInfo>(sql, new 
                    { 
                        EntityId = entityId.ToString()
                    });
                    return tags.ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get tags for {entityType} {entityId}", ex);
                return new List<TagInfo>();
            }
        }

        public async Task<List<string>> GetAllTagSuggestionsAsync(string prefix = "", int limit = 20)
        {
            try
            {
                var suggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Get suggestions from tree.db
                using (var treeConnection = new SqliteConnection(_treeDbConnectionString))
                {
                    await treeConnection.OpenAsync();
                    var treeSql = @"SELECT DISTINCT tag 
                        FROM (
                            SELECT tag FROM folder_tags
                            UNION ALL
                            SELECT tag FROM note_tags
                        )
                        WHERE tag LIKE @Prefix || '%'
                        GROUP BY tag
                        ORDER BY COUNT(*) DESC
                        LIMIT @Limit";

                    var treeTags = await treeConnection.QueryAsync<string>(treeSql, new { Prefix = prefix, Limit = limit });
                    foreach (var tag in treeTags)
                        suggestions.Add(tag);
                }

                // Get suggestions from todos.db
                using (var todoConnection = new SqliteConnection(_todosDbConnectionString))
                {
                    await todoConnection.OpenAsync();
                    var todoSql = @"SELECT DISTINCT display_name 
                        FROM todo_tags 
                        WHERE display_name LIKE @Prefix || '%'
                        GROUP BY display_name
                        ORDER BY COUNT(*) DESC
                        LIMIT @Limit";

                    var todoTags = await todoConnection.QueryAsync<string>(todoSql, new { Prefix = prefix, Limit = limit });
                    foreach (var tag in todoTags)
                        suggestions.Add(tag);
                }

                return suggestions.Take(limit).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to get tag suggestions", ex);
                return new List<string>();
            }
        }


        public async Task<List<TagSuggestion>> GetPopularTagsAsync(string entityType, int limit = 10)
        {
            try
            {
                var connectionString = GetConnectionString(entityType);
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                if (entityType == "todo")
                {
                    var sql = @"SELECT 
                        tag as Tag,
                        display_name as DisplayName,
                        COUNT(*) as UsageCount
                    FROM todo_tags
                    GROUP BY tag, display_name
                    ORDER BY UsageCount DESC
                    LIMIT @Limit";

                    var suggestions = await connection.QueryAsync<TagSuggestion>(sql, new { Limit = limit });
                    return suggestions.ToList();
                }
                else
                {
                    string tableName = entityType == "folder" ? "folder_tags" : "note_tags";
                    
                    var sql = $@"SELECT 
                        tag as Tag,
                        tag as DisplayName,
                        COUNT(*) as UsageCount
                    FROM {tableName}
                    GROUP BY tag
                    ORDER BY UsageCount DESC
                    LIMIT @Limit";

                    var suggestions = await connection.QueryAsync<TagSuggestion>(sql, new 
                    { 
                        Limit = limit 
                    });
                    return suggestions.ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get popular tags for {entityType}", ex);
                return new List<TagSuggestion>();
            }
        }

        public async Task AddTagAsync(Guid entityId, string entityType, string tagName, string source = "manual")
        {
            try
            {
                var connectionString = GetConnectionString(entityType);
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                using var transaction = connection.BeginTransaction();

                if (entityType == "todo")
                {
                    // Add to todos.db
                    var normalizedTag = tagName.ToLower().Trim();
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                    await connection.ExecuteAsync(
                        @"INSERT OR REPLACE INTO todo_tags 
                          (todo_id, tag, display_name, source, created_at)
                          VALUES (@TodoId, @Tag, @DisplayName, @Source, @CreatedAt)",
                        new
                        {
                            TodoId = entityId.ToString(),
                            Tag = normalizedTag,
                            DisplayName = tagName.Trim(),
                            Source = source,
                            CreatedAt = now
                        },
                        transaction
                    );

                    // Update local tag metadata
                    await connection.ExecuteAsync(
                        @"INSERT INTO tag_metadata (tag, display_name, category, created_at)
                          VALUES (@Tag, @DisplayName, 'todo', @CreatedAt)
                          ON CONFLICT(tag) DO UPDATE SET
                            usage_count = usage_count + 1,
                            last_used_at = @CreatedAt",
                        new
                        {
                            Tag = normalizedTag,
                            DisplayName = tagName.Trim(),
                            CreatedAt = now
                        },
                        transaction
                    );
                }
                else
                {
                    // Add to tree.db appropriate table
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    string tableName = entityType == "folder" ? "folder_tags" : "note_tags";
                    string idColumn = entityType == "folder" ? "folder_id" : "note_id";

                    await connection.ExecuteAsync(
                        $@"INSERT OR REPLACE INTO {tableName} 
                          ({idColumn}, tag, created_at)
                          VALUES (@EntityId, @Tag, @CreatedAt)",
                        new
                        {
                            EntityId = entityId.ToString(),
                            Tag = tagName.Trim(),
                            CreatedAt = now
                        },
                        transaction
                    );
                }

                transaction.Commit();
                _logger.Info($"Added tag '{tagName}' to {entityType} {entityId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to add tag '{tagName}' to {entityType} {entityId}", ex);
                throw;
            }
        }

        public async Task RemoveTagAsync(Guid entityId, string entityType, string tagName)
        {
            try
            {
                var connectionString = GetConnectionString(entityType);
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                if (entityType == "todo")
                {
                    await connection.ExecuteAsync(
                        "DELETE FROM todo_tags WHERE todo_id = @TodoId AND tag = @Tag",
                        new
                        {
                            TodoId = entityId.ToString(),
                            Tag = tagName.ToLower().Trim()
                        }
                    );

                    // Update usage count
                    await connection.ExecuteAsync(
                        @"UPDATE tag_metadata SET 
                            usage_count = (SELECT COUNT(*) FROM todo_tags WHERE tag = @Tag)
                          WHERE tag = @Tag",
                        new { Tag = tagName.ToLower().Trim() }
                    );
                }
                else
                {
                    string tableName = entityType == "folder" ? "folder_tags" : "note_tags";
                    string idColumn = entityType == "folder" ? "folder_id" : "note_id";

                    await connection.ExecuteAsync(
                        $@"DELETE FROM {tableName} 
                          WHERE {idColumn} = @EntityId AND tag = @Tag",
                        new
                        {
                            EntityId = entityId.ToString(),
                            Tag = tagName.Trim()
                        }
                    );
                }

                _logger.Info($"Removed tag '{tagName}' from {entityType} {entityId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to remove tag '{tagName}' from {entityType} {entityId}", ex);
                throw;
            }
        }

        public async Task SetEntityTagsAsync(Guid entityId, string entityType, List<string> tagNames, string source = "manual")
        {
            try
            {
                var connectionString = GetConnectionString(entityType);
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                using var transaction = connection.BeginTransaction();

                if (entityType == "todo")
                {
                    // Remove existing tags
                    await connection.ExecuteAsync(
                        "DELETE FROM todo_tags WHERE todo_id = @TodoId",
                        new { TodoId = entityId.ToString() },
                        transaction
                    );

                    // Add new tags
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    foreach (var tagName in tagNames.Where(t => !string.IsNullOrWhiteSpace(t)))
                    {
                        var normalizedTag = tagName.ToLower().Trim();

                        await connection.ExecuteAsync(
                            @"INSERT INTO todo_tags 
                              (todo_id, tag, display_name, source, created_at)
                              VALUES (@TodoId, @Tag, @DisplayName, @Source, @CreatedAt)",
                            new
                            {
                                TodoId = entityId.ToString(),
                                Tag = normalizedTag,
                                DisplayName = tagName.Trim(),
                                Source = source,
                                CreatedAt = now
                            },
                            transaction
                        );

                        // Update metadata
                        await connection.ExecuteAsync(
                            @"INSERT INTO tag_metadata (tag, display_name, category, created_at)
                              VALUES (@Tag, @DisplayName, 'todo', @CreatedAt)
                              ON CONFLICT(tag) DO UPDATE SET
                                usage_count = usage_count + 1,
                                last_used_at = @CreatedAt",
                            new
                            {
                                Tag = normalizedTag,
                                DisplayName = tagName.Trim(),
                                CreatedAt = now
                            },
                            transaction
                        );
                    }
                }
                else
                {
                    string tableName = entityType == "folder" ? "folder_tags" : "note_tags";
                    string idColumn = entityType == "folder" ? "folder_id" : "note_id";

                    // Remove existing tags
                    await connection.ExecuteAsync(
                        $@"DELETE FROM {tableName} 
                          WHERE {idColumn} = @EntityId",
                        new 
                        { 
                            EntityId = entityId.ToString()
                        },
                        transaction
                    );

                    // Add new tags
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    foreach (var tagName in tagNames.Where(t => !string.IsNullOrWhiteSpace(t)))
                    {
                        await connection.ExecuteAsync(
                            $@"INSERT INTO {tableName} 
                              ({idColumn}, tag, created_at)
                              VALUES (@EntityId, @Tag, @CreatedAt)",
                            new
                            {
                                EntityId = entityId.ToString(),
                                Tag = tagName.Trim(),
                                CreatedAt = now
                            },
                            transaction
                        );
                    }
                }

                transaction.Commit();
                _logger.Info($"Set {tagNames.Count} tags for {entityType} {entityId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to set tags for {entityType} {entityId}", ex);
                throw;
            }
        }

        private string GetConnectionString(string entityType)
        {
            return entityType == "todo" ? _todosDbConnectionString : _treeDbConnectionString;
        }

    }
}
