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
/// Follows same pattern as FolderTagRepository.
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

            var tags = await connection.QueryAsync<NoteTagDto>(
                @"SELECT 
                    note_id, 
                    tag, 
                    is_auto, 
                    datetime(created_at, 'unixepoch', 'localtime') as created_at
                  FROM note_tags
                  WHERE note_id = @NoteId
                  ORDER BY created_at ASC",
                new { NoteId = noteId.ToString() }
            );

            return tags.Select(MapFromDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get note tags for {noteId}", ex);
            return new List<NoteTag>();
        }
    }

    public async Task SetNoteTagsAsync(Guid noteId, List<string> tags, bool isAuto = false)
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
                    "DELETE FROM note_tags WHERE note_id = @NoteId",
                    new { NoteId = noteId.ToString() },
                    transaction
                );

                // Insert new tags
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                foreach (var tag in tags.Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    await connection.ExecuteAsync(
                        @"INSERT INTO note_tags (note_id, tag, is_auto, created_at)
                          VALUES (@NoteId, @Tag, @IsAuto, @CreatedAt)",
                        new
                        {
                            NoteId = noteId.ToString(),
                            Tag = tag.Trim(),
                            IsAuto = isAuto ? 1 : 0,
                            CreatedAt = now
                        },
                        transaction
                    );
                }

                transaction.Commit();
                _logger.Info($"Set {tags.Count} tags for note {noteId}");
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to set note tags for {noteId}", ex);
            throw;
        }
    }

    public async Task AddNoteTagAsync(Guid noteId, string tag, bool isAuto = false)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await connection.ExecuteAsync(
                @"INSERT OR REPLACE INTO note_tags (note_id, tag, is_auto, created_at)
                  VALUES (@NoteId, @Tag, @IsAuto, @CreatedAt)",
                new
                {
                    NoteId = noteId.ToString(),
                    Tag = tag.Trim(),
                    IsAuto = isAuto ? 1 : 0,
                    CreatedAt = now
                }
            );

            _logger.Info($"Added tag '{tag}' to note {noteId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to add tag '{tag}' to note {noteId}", ex);
            throw;
        }
    }

    public async Task RemoveNoteTagsAsync(Guid noteId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var count = await connection.ExecuteAsync(
                "DELETE FROM note_tags WHERE note_id = @NoteId",
                new { NoteId = noteId.ToString() }
            );

            _logger.Info($"Removed {count} tags from note {noteId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove note tags for {noteId}", ex);
            throw;
        }
    }

    public async Task RemoveNoteTagAsync(Guid noteId, string tag)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(
                "DELETE FROM note_tags WHERE note_id = @NoteId AND tag = @Tag",
                new { NoteId = noteId.ToString(), Tag = tag }
            );

            _logger.Info($"Removed tag '{tag}' from note {noteId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove tag '{tag}' from note {noteId}", ex);
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

    // Helper method to map from DTO to domain model
    private static NoteTag MapFromDto(NoteTagDto dto)
    {
        return new NoteTag
        {
            NoteId = Guid.Parse(dto.note_id),
            Tag = dto.tag,
            IsAuto = dto.is_auto == 1,
            CreatedAt = DateTime.Parse(dto.created_at) // SQLite datetime() returns string
        };
    }

    // DTO for Dapper mapping (matches SQLite column names)
    private class NoteTagDto
    {
        public string note_id { get; set; } = string.Empty;
        public string tag { get; set; } = string.Empty;
        public int is_auto { get; set; }
        public string created_at { get; set; } = string.Empty; // datetime() returns string
    }
}

