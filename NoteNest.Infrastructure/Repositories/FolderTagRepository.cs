using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Application.FolderTags.Models;
using NoteNest.Application.FolderTags.Repositories;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.Repositories
{
    /// <summary>
    /// Repository for managing folder tags in tree.db.
    /// Implements folder tag persistence and inheritance up the tree hierarchy.
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

                var sql = @"
                    SELECT 
                        folder_id as FolderId,
                        tag as Tag,
                        is_auto_suggested as IsAutoSuggested,
                        inherit_to_children as InheritToChildren,
                        created_at as CreatedAt,
                        created_by as CreatedBy
                    FROM folder_tags
                    WHERE folder_id = @FolderId
                    ORDER BY tag";

                var tags = await connection.QueryAsync<FolderTag>(sql,
                    new { FolderId = folderId.ToString() });

                return tags.ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get folder tags for {folderId}");
                return new List<FolderTag>();
            }
        }

        public async Task<List<FolderTag>> GetInheritedTagsAsync(Guid folderId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Recursive CTE to walk up folder hierarchy and collect inherited tags
                var sql = @"
                    WITH RECURSIVE folder_hierarchy AS (
                        -- Start with target folder
                        SELECT id, parent_id, 0 as depth
                        FROM tree_nodes
                        WHERE id = @FolderId AND node_type = 'category'
                        
                        UNION ALL
                        
                        -- Walk up to parent folders
                        SELECT tn.id, tn.parent_id, fh.depth + 1
                        FROM tree_nodes tn
                        INNER JOIN folder_hierarchy fh ON tn.id = fh.parent_id
                        WHERE fh.depth < 20  -- Prevent infinite loops (max 20 levels)
                          AND tn.node_type = 'category'
                    )
                    SELECT DISTINCT
                        ft.folder_id as FolderId,
                        ft.tag as Tag,
                        ft.is_auto_suggested as IsAutoSuggested,
                        ft.inherit_to_children as InheritToChildren,
                        ft.created_at as CreatedAt,
                        ft.created_by as CreatedBy
                    FROM folder_hierarchy fh
                    INNER JOIN folder_tags ft ON ft.folder_id = fh.id
                    WHERE ft.inherit_to_children = 1
                    ORDER BY fh.depth, ft.tag";

                var tags = await connection.QueryAsync<FolderTag>(sql,
                    new { FolderId = folderId.ToString() });

                return tags.ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get inherited tags for {folderId}");
                return new List<FolderTag>();
            }
        }

        public async Task SetFolderTagsAsync(Guid folderId, List<string> tags, bool isAutoSuggested = false, bool inheritToChildren = true)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                using var transaction = connection.BeginTransaction();

                try
                {
                    // Delete existing tags for this folder
                    await connection.ExecuteAsync(
                        "DELETE FROM folder_tags WHERE folder_id = @FolderId",
                        new { FolderId = folderId.ToString() },
                        transaction);

                    // Insert new tags
                    foreach (var tag in tags)
                    {
                        await connection.ExecuteAsync(@"
                            INSERT INTO folder_tags 
                            (folder_id, tag, is_auto_suggested, inherit_to_children, created_at, created_by)
                            VALUES (@FolderId, @Tag, @IsAuto, @Inherit, @CreatedAt, 'user')",
                            new
                            {
                                FolderId = folderId.ToString(),
                                Tag = tag,
                                IsAuto = isAutoSuggested ? 1 : 0,
                                Inherit = inheritToChildren ? 1 : 0,
                                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                            },
                            transaction);
                    }

                    transaction.Commit();
                    _logger.Info($"✅ Saved {tags.Count} tags for folder {folderId}");
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to set folder tags for {folderId}");
                throw;
            }
        }

        public async Task AddFolderTagAsync(Guid folderId, string tag, bool isAutoSuggested = false, bool inheritToChildren = true)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                await connection.ExecuteAsync(@"
                    INSERT OR REPLACE INTO folder_tags 
                    (folder_id, tag, is_auto_suggested, inherit_to_children, created_at, created_by)
                    VALUES (@FolderId, @Tag, @IsAuto, @Inherit, @CreatedAt, 'user')",
                    new
                    {
                        FolderId = folderId.ToString(),
                        Tag = tag,
                        IsAuto = isAutoSuggested ? 1 : 0,
                        Inherit = inheritToChildren ? 1 : 0,
                        CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    });

                _logger.Debug($"Added tag '{tag}' to folder {folderId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to add tag '{tag}' to folder {folderId}");
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
                    new { FolderId = folderId.ToString() });

                _logger.Debug($"Removed all tags from folder {folderId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to remove tags from folder {folderId}");
                throw;
            }
        }

        public async Task RemoveFolderTagAsync(Guid folderId, string tag)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                await connection.ExecuteAsync(
                    "DELETE FROM folder_tags WHERE folder_id = @FolderId AND tag = @Tag",
                    new { FolderId = folderId.ToString(), Tag = tag });

                _logger.Debug($"Removed tag '{tag}' from folder {folderId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to remove tag '{tag}' from folder {folderId}");
                throw;
            }
        }

        public async Task<List<Guid>> GetTaggedFoldersAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var folderIds = await connection.QueryAsync<string>(
                    "SELECT DISTINCT folder_id FROM folder_tags");

                return folderIds.Select(id => Guid.Parse(id)).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get tagged folders");
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
                    new { FolderId = folderId.ToString() });

                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to check if folder {folderId} has tags");
                return false;
            }
        }

        public async Task<List<Guid>> GetChildFolderIdsAsync(Guid folderId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Recursive CTE to get all descendant folders
                var sql = @"
                    WITH RECURSIVE folder_tree AS (
                        SELECT id 
                        FROM tree_nodes 
                        WHERE parent_id = @FolderId AND node_type = 'category'
                        
                        UNION ALL
                        
                        SELECT tn.id 
                        FROM tree_nodes tn
                        INNER JOIN folder_tree ft ON tn.parent_id = ft.id
                        WHERE tn.node_type = 'category'
                    )
                    SELECT id FROM folder_tree";

                var ids = await connection.QueryAsync<string>(sql,
                    new { FolderId = folderId.ToString() });

                return ids.Select(id => Guid.Parse(id)).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get child folder IDs for {folderId}");
                return new List<Guid>();
            }
        }
    }
}

