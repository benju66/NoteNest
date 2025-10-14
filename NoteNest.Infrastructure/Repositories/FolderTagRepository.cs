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
/// Follows existing TreeDatabaseRepository patterns.
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

            var tags = await connection.QueryAsync<FolderTagDto>(
                @"SELECT folder_id, tag, is_auto_suggested, inherit_to_children, created_at, created_by
                  FROM folder_tags
                  WHERE folder_id = @FolderId
                  ORDER BY created_at ASC",
                new { FolderId = folderId.ToString() }
            );

            return tags.Select(MapFromDto).ToList();
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

            // Recursive CTE to get all ancestor folders
            var tags = await connection.QueryAsync<FolderTagDto>(
                @"WITH RECURSIVE ancestors AS (
                    SELECT id, parent_id FROM tree_nodes WHERE id = @FolderId
                    UNION ALL
                    SELECT tn.id, tn.parent_id 
                    FROM tree_nodes tn
                    INNER JOIN ancestors a ON tn.id = a.parent_id
                    WHERE a.parent_id IS NOT NULL
                  )
                  SELECT DISTINCT ft.folder_id, ft.tag, ft.is_auto_suggested, ft.inherit_to_children, ft.created_at, ft.created_by
                  FROM folder_tags ft
                  INNER JOIN ancestors a ON ft.folder_id = a.id
                  WHERE ft.inherit_to_children = 1 OR ft.folder_id = @FolderId
                  ORDER BY ft.created_at ASC",
                new { FolderId = folderId.ToString() }
            );

            return tags.Select(MapFromDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get inherited tags for {folderId}", ex);
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
                // Remove existing tags
                await connection.ExecuteAsync(
                    "DELETE FROM folder_tags WHERE folder_id = @FolderId",
                    new { FolderId = folderId.ToString() },
                    transaction
                );

                // Insert new tags
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                foreach (var tag in tags.Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    await connection.ExecuteAsync(
                        @"INSERT INTO folder_tags (folder_id, tag, is_auto_suggested, inherit_to_children, created_at, created_by)
                          VALUES (@FolderId, @Tag, @IsAutoSuggested, @InheritToChildren, @CreatedAt, 'user')",
                        new
                        {
                            FolderId = folderId.ToString(),
                            Tag = tag.Trim(),
                            IsAutoSuggested = isAutoSuggested ? 1 : 0,
                            InheritToChildren = inheritToChildren ? 1 : 0,
                            CreatedAt = now
                        },
                        transaction
                    );
                }

                transaction.Commit();
                _logger.Info($"Set {tags.Count} tags for folder {folderId}");
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to set folder tags for {folderId}", ex);
            throw;
        }
    }

    public async Task AddFolderTagAsync(Guid folderId, string tag, bool isAutoSuggested = false, bool inheritToChildren = true)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await connection.ExecuteAsync(
                @"INSERT OR REPLACE INTO folder_tags (folder_id, tag, is_auto_suggested, inherit_to_children, created_at, created_by)
                  VALUES (@FolderId, @Tag, @IsAutoSuggested, @InheritToChildren, @CreatedAt, 'user')",
                new
                {
                    FolderId = folderId.ToString(),
                    Tag = tag.Trim(),
                    IsAutoSuggested = isAutoSuggested ? 1 : 0,
                    InheritToChildren = inheritToChildren ? 1 : 0,
                    CreatedAt = now
                }
            );

            _logger.Info($"Added tag '{tag}' to folder {folderId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to add tag '{tag}' to folder {folderId}", ex);
            throw;
        }
    }

    public async Task RemoveFolderTagsAsync(Guid folderId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var count = await connection.ExecuteAsync(
                "DELETE FROM folder_tags WHERE folder_id = @FolderId",
                new { FolderId = folderId.ToString() }
            );

            _logger.Info($"Removed {count} tags from folder {folderId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove folder tags for {folderId}", ex);
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
                new { FolderId = folderId.ToString(), Tag = tag }
            );

            _logger.Info($"Removed tag '{tag}' from folder {folderId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove tag '{tag}' from folder {folderId}", ex);
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
                "SELECT DISTINCT folder_id FROM folder_tags ORDER BY folder_id"
            );

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

            // Recursive CTE to get all descendants
            var childIds = await connection.QueryAsync<string>(
                @"WITH RECURSIVE descendants AS (
                    SELECT id FROM tree_nodes WHERE parent_id = @FolderId AND type = 'category'
                    UNION ALL
                    SELECT tn.id FROM tree_nodes tn
                    INNER JOIN descendants d ON tn.parent_id = d.id
                    WHERE tn.type = 'category'
                  )
                  SELECT id FROM descendants",
                new { FolderId = folderId.ToString() }
            );

            return childIds.Select(id => Guid.Parse(id)).ToList();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get child folder IDs for {folderId}", ex);
            return new List<Guid>();
        }
    }

    // Helper method to map from DTO to domain model
    private static FolderTag MapFromDto(FolderTagDto dto)
    {
        return new FolderTag
        {
            FolderId = Guid.Parse(dto.folder_id),
            Tag = dto.tag,
            IsAutoSuggested = dto.is_auto_suggested == 1,
            InheritToChildren = dto.inherit_to_children == 1,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(dto.created_at).DateTime,
            CreatedBy = dto.created_by ?? "user"
        };
    }

    // DTO for Dapper mapping (matches SQLite column names)
    private class FolderTagDto
    {
        public string folder_id { get; set; } = string.Empty;
        public string tag { get; set; } = string.Empty;
        public int is_auto_suggested { get; set; }
        public int inherit_to_children { get; set; }
        public long created_at { get; set; }
        public string? created_by { get; set; }
    }
}

