using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.FolderTags.Models;
using NoteNest.Application.FolderTags.Repositories;

namespace NoteNest.Infrastructure.Repositories;

/// <summary>
/// Repository for managing folder tags in the tree database.
/// Uses the existing folder_tags table in tree.db.
/// </summary>
public class FolderTagRepository : IFolderTagRepository
{
    private readonly string _connectionString;
    private readonly IAppLogger _logger;

    public FolderTagRepository(string connectionString, IAppLogger logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<FolderTag>> GetFolderTagsAsync(Guid folderId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"SELECT 
                folder_id as FolderId,
                tag as Tag,
                created_at as CreatedAt
            FROM folder_tags
            WHERE folder_id = @FolderId
            ORDER BY tag";

            var tags = await connection.QueryAsync<FolderTag>(sql, new { FolderId = folderId.ToString() });
            return tags.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get folder tags for {folderId}", ex);
            return new List<FolderTag>();
        }
    }

    public async Task<List<FolderTag>> GetInheritedTagsAsync(Guid folderId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // Use recursive CTE to find all ancestor folders and their tags
            var sql = @"
                WITH RECURSIVE ancestors AS (
                    -- Start with the given folder
                    SELECT id, parent_id, 0 as depth
                    FROM tree_nodes
                    WHERE id = @FolderId AND node_type = 'category'
                    
                    UNION ALL
                    
                    -- Recursively find parent folders
                    SELECT tn.id, tn.parent_id, a.depth + 1
                    FROM tree_nodes tn
                    JOIN ancestors a ON tn.id = a.parent_id
                    WHERE tn.node_type = 'category' AND a.depth < 20
                )
                SELECT DISTINCT
                    ft.folder_id as FolderId,
                    ft.tag as Tag,
                    ft.created_at as CreatedAt
                FROM ancestors a
                JOIN folder_tags ft ON a.parent_id = ft.folder_id
                ORDER BY ft.tag";

            var tags = await connection.QueryAsync<FolderTag>(sql, new { FolderId = folderId.ToString() });
            return tags.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get inherited tags for folder {folderId}", ex);
            return new List<FolderTag>();
        }
    }

    public async Task SetFolderTagsAsync(Guid folderId, List<string> tagNames, bool isAutoSuggested = false, bool inheritToChildren = true)
    {
        if (tagNames == null)
            tagNames = new List<string>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            // Remove existing tags
            await connection.ExecuteAsync(
                "DELETE FROM folder_tags WHERE folder_id = @FolderId",
                new { FolderId = folderId.ToString() },
                transaction
            );

            // Add new tags
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var tagName in tagNames.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                await connection.ExecuteAsync(
                    @"INSERT INTO folder_tags (folder_id, tag, created_at)
                      VALUES (@FolderId, @Tag, @CreatedAt)",
                    new 
                    { 
                        FolderId = folderId.ToString(),
                        Tag = tagName.Trim(),
                        CreatedAt = now
                    },
                    transaction
                );
            }

            transaction.Commit();
            _logger.Info($"Set {tagNames.Count} tags for folder {folderId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to set tags for folder {folderId}", ex);
            throw;
        }
    }

    public async Task AddFolderTagAsync(Guid folderId, string tagName, bool isAutoSuggested = false, bool inheritToChildren = true)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            await connection.ExecuteAsync(
                @"INSERT OR REPLACE INTO folder_tags (folder_id, tag, created_at)
                  VALUES (@FolderId, @Tag, @CreatedAt)",
                new 
                { 
                    FolderId = folderId.ToString(),
                    Tag = tagName.Trim(),
                    CreatedAt = now
                }
            );

            _logger.Info($"Added tag '{tagName}' to folder {folderId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to add tag '{tagName}' to folder {folderId}", ex);
            throw;
        }
    }

    public async Task RemoveFolderTagsAsync(Guid folderId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(
                "DELETE FROM folder_tags WHERE folder_id = @FolderId",
                new { FolderId = folderId.ToString() }
            );

            _logger.Info($"Removed all tags from folder {folderId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove tags from folder {folderId}", ex);
            throw;
        }
    }

    public async Task RemoveFolderTagAsync(Guid folderId, string tagName)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(
                "DELETE FROM folder_tags WHERE folder_id = @FolderId AND tag = @Tag",
                new 
                { 
                    FolderId = folderId.ToString(),
                    Tag = tagName.Trim()
                }
            );

            _logger.Info($"Removed tag '{tagName}' from folder {folderId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove tag '{tagName}' from folder {folderId}", ex);
            throw;
        }
    }

    public async Task<List<Guid>> GetTaggedFoldersAsync()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"SELECT DISTINCT folder_id FROM folder_tags";
            var folderIds = await connection.QueryAsync<string>(sql);
            
            return folderIds.Select(id => Guid.Parse(id)).ToList();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to get tagged folders", ex);
            return new List<Guid>();
        }
    }

    public async Task<bool> HasTagsAsync(Guid folderId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var count = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM folder_tags WHERE folder_id = @FolderId",
                new { FolderId = folderId.ToString() }
            );

            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to check if folder {folderId} has tags", ex);
            return false;
        }
    }

    public async Task<List<Guid>> GetChildFolderIdsAsync(Guid folderId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // Use recursive CTE to find all descendant folders
            var sql = @"
                WITH RECURSIVE descendants AS (
                    -- Start with direct children
                    SELECT id
                    FROM tree_nodes
                    WHERE parent_id = @FolderId AND node_type = 'category'
                    
                    UNION ALL
                    
                    -- Recursively find children of children
                    SELECT tn.id
                    FROM tree_nodes tn
                    JOIN descendants d ON tn.parent_id = d.id
                    WHERE tn.node_type = 'category'
                )
                SELECT id FROM descendants";

            var childIds = await connection.QueryAsync<string>(sql, new { FolderId = folderId.ToString() });
            return childIds.Select(id => Guid.Parse(id)).ToList();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get child folder IDs for {folderId}", ex);
            return new List<Guid>();
        }
    }
}