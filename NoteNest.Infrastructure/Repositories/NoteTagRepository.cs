using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.NoteTags.Models;
using NoteNest.Application.NoteTags.Repositories;

namespace NoteNest.Infrastructure.Repositories;

/// <summary>
/// Repository for managing note tags in the tree database.
/// Uses the existing note_tags table in tree.db.
/// </summary>
public class NoteTagRepository : INoteTagRepository
{
    private readonly string _connectionString;
    private readonly IAppLogger _logger;

    public NoteTagRepository(string connectionString, IAppLogger logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<NoteTag>> GetNoteTagsAsync(Guid noteId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"SELECT 
                note_id as NoteId,
                tag as Tag,
                created_at as CreatedAt
            FROM note_tags
            WHERE note_id = @NoteId
            ORDER BY tag";

            var tags = await connection.QueryAsync<NoteTag>(sql, new { NoteId = noteId.ToString() });
            return tags.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get note tags for {noteId}", ex);
            return new List<NoteTag>();
        }
    }

    public async Task SetNoteTagsAsync(Guid noteId, List<string> tagNames, bool isAuto = false)
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
                "DELETE FROM note_tags WHERE note_id = @NoteId",
                new { NoteId = noteId.ToString() },
                transaction
            );

            // Add new tags
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var tagName in tagNames.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                await connection.ExecuteAsync(
                    @"INSERT INTO note_tags (note_id, tag, created_at)
                      VALUES (@NoteId, @Tag, @CreatedAt)",
                    new 
                    { 
                        NoteId = noteId.ToString(),
                        Tag = tagName.Trim(),
                        CreatedAt = now
                    },
                    transaction
                );
            }

            transaction.Commit();
            _logger.Info($"Set {tagNames.Count} tags for note {noteId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to set tags for note {noteId}", ex);
            throw;
        }
    }

    public async Task AddNoteTagAsync(Guid noteId, string tagName, bool isAuto = false)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            await connection.ExecuteAsync(
                @"INSERT OR REPLACE INTO note_tags (note_id, tag, created_at)
                  VALUES (@NoteId, @Tag, @CreatedAt)",
                new 
                { 
                    NoteId = noteId.ToString(),
                    Tag = tagName.Trim(),
                    CreatedAt = now
                }
            );

            _logger.Info($"Added tag '{tagName}' to note {noteId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to add tag '{tagName}' to note {noteId}", ex);
            throw;
        }
    }

    public async Task RemoveNoteTagsAsync(Guid noteId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(
                "DELETE FROM note_tags WHERE note_id = @NoteId",
                new { NoteId = noteId.ToString() }
            );

            _logger.Info($"Removed all tags from note {noteId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove tags from note {noteId}", ex);
            throw;
        }
    }

    public async Task RemoveNoteTagAsync(Guid noteId, string tagName)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(
                "DELETE FROM note_tags WHERE note_id = @NoteId AND tag = @Tag",
                new 
                { 
                    NoteId = noteId.ToString(),
                    Tag = tagName.Trim()
                }
            );

            _logger.Info($"Removed tag '{tagName}' from note {noteId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove tag '{tagName}' from note {noteId}", ex);
            throw;
        }
    }

    public async Task<bool> HasTagsAsync(Guid noteId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var count = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM note_tags WHERE note_id = @NoteId",
                new { NoteId = noteId.ToString() }
            );

            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to check if note {noteId} has tags", ex);
            return false;
        }
    }
}