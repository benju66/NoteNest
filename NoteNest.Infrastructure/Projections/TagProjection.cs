using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Domain.Tags.Events;
using NoteNest.Domain.Categories.Events;
using NoteNest.Domain.Notes.Events;
using NoteNest.Application.FolderTags.Events;
using NoteNest.Application.NoteTags.Events;

namespace NoteNest.Infrastructure.Projections
{
    /// <summary>
    /// Builds tag projection from tag-related events.
    /// Maintains unified tag vocabulary and entity associations.
    /// Solves the tag persistence problem permanently.
    /// </summary>
    public class TagProjection : BaseProjection
    {
        public override string Name => "TagView";
        
        public TagProjection(string connectionString, IAppLogger logger)
            : base(connectionString, logger)
        {
        }
        
        public override async Task HandleAsync(IDomainEvent @event)
        {
            switch (@event)
            {
                // Tag aggregate events
                case TagCreated e:
                    await HandleTagCreatedAsync(e);
                    break;
                    
                case TagUsageIncremented e:
                    await HandleTagUsageIncrementedAsync(e);
                    break;
                    
                case TagUsageDecremented e:
                    await HandleTagUsageDecrementedAsync(e);
                    break;
                    
                case TagCategorySet e:
                    await HandleTagCategorySetAsync(e);
                    break;
                    
                case TagColorSet e:
                    await HandleTagColorSetAsync(e);
                    break;
                    
                // Entity tag events
                case TagAddedToEntity e:
                    await HandleTagAddedToEntityAsync(e);
                    break;
                    
                case TagRemovedFromEntity e:
                    await HandleTagRemovedFromEntityAsync(e);
                    break;
                    
                // Legacy folder tag events (during migration)
                case FolderTaggedEvent e:
                    await HandleFolderTaggedAsync(e);
                    break;
                    
                case FolderUntaggedEvent e:
                    await HandleFolderUntaggedAsync(e);
                    break;
                    
                // Legacy note tag events (during migration)
                case NoteTaggedEvent e:
                    await HandleNoteTaggedAsync(e);
                    break;
                    
                case NoteUntaggedEvent e:
                    await HandleNoteUntaggedAsync(e);
                    break;
                    
                // NEW: Event-sourced category tags
                case CategoryTagsSet e:
                    await HandleCategoryTagsSetAsync(e);
                    break;
                    
                // NEW: Event-sourced note tags
                case NoteTagsSet e:
                    await HandleNoteTagsSetAsync(e);
                    break;
            }
        }
        
        protected override async Task ClearProjectionDataAsync()
        {
            using var connection = await OpenConnectionAsync();
            await connection.ExecuteAsync("DELETE FROM tag_vocabulary");
            await connection.ExecuteAsync("DELETE FROM entity_tags");
            _logger.Info($"[{Name}] Cleared projection data");
        }
        
        // =============================================================================
        // TAG AGGREGATE EVENT HANDLERS
        // =============================================================================
        
        private async Task HandleTagCreatedAsync(TagCreated e)
        {
            using var connection = await OpenConnectionAsync();
            
            await connection.ExecuteAsync(
                @"INSERT OR REPLACE INTO tag_vocabulary 
                  (tag, display_name, usage_count, first_used_at, last_used_at)
                  VALUES (@Tag, @DisplayName, 0, @CreatedAt, @CreatedAt)",
                new
                {
                    Tag = e.Name,
                    DisplayName = e.DisplayName,
                    CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Debug($"[{Name}] Tag created in vocabulary: {e.DisplayName}");
        }
        
        private async Task HandleTagUsageIncrementedAsync(TagUsageIncremented e)
        {
            using var connection = await OpenConnectionAsync();
            
            // We need to look up tag name by ID (future enhancement)
            // For now, this will be handled by TagAddedToEntity event
            _logger.Debug($"[{Name}] Tag usage incremented: {e.TagId}");
        }
        
        private async Task HandleTagUsageDecrementedAsync(TagUsageDecremented e)
        {
            using var connection = await OpenConnectionAsync();
            
            // Future enhancement
            _logger.Debug($"[{Name}] Tag usage decremented: {e.TagId}");
        }
        
        private async Task HandleTagCategorySetAsync(TagCategorySet e)
        {
            using var connection = await OpenConnectionAsync();
            
            // Future enhancement for tag organization
            _logger.Debug($"[{Name}] Tag category set: {e.TagId} → {e.Category}");
        }
        
        private async Task HandleTagColorSetAsync(TagColorSet e)
        {
            using var connection = await OpenConnectionAsync();
            
            // Future enhancement for tag visualization
            _logger.Debug($"[{Name}] Tag color set: {e.TagId} → {e.Color}");
        }
        
        // =============================================================================
        // ENTITY TAG EVENT HANDLERS
        // =============================================================================
        
        private async Task HandleTagAddedToEntityAsync(TagAddedToEntity e)
        {
            using var connection = await OpenConnectionAsync();
            
            // Ensure tag exists in vocabulary
            await connection.ExecuteAsync(
                @"INSERT OR IGNORE INTO tag_vocabulary 
                  (tag, display_name, usage_count, first_used_at, last_used_at)
                  VALUES (@Tag, @DisplayName, 0, @CreatedAt, @CreatedAt)",
                new
                {
                    Tag = e.Tag.ToLowerInvariant(),
                    DisplayName = e.DisplayName,
                    CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            // Add to entity_tags
            await connection.ExecuteAsync(
                @"INSERT OR REPLACE INTO entity_tags 
                  (entity_id, entity_type, tag, display_name, source, created_at)
                  VALUES (@EntityId, @EntityType, @Tag, @DisplayName, @Source, @CreatedAt)",
                new
                {
                    EntityId = e.EntityId.ToString(),
                    EntityType = e.EntityType,
                    Tag = e.Tag.ToLowerInvariant(),
                    DisplayName = e.DisplayName,
                    Source = e.Source,
                    CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            // Increment usage count
            await connection.ExecuteAsync(
                @"UPDATE tag_vocabulary 
                  SET usage_count = usage_count + 1, last_used_at = @LastUsedAt
                  WHERE tag = @Tag",
                new
                {
                    Tag = e.Tag.ToLowerInvariant(),
                    LastUsedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Debug($"[{Name}] Tag added: '{e.DisplayName}' to {e.EntityType} {e.EntityId}");
        }
        
        private async Task HandleTagRemovedFromEntityAsync(TagRemovedFromEntity e)
        {
            using var connection = await OpenConnectionAsync();
            
            // Remove from entity_tags
            await connection.ExecuteAsync(
                "DELETE FROM entity_tags WHERE entity_id = @EntityId AND tag = @Tag",
                new
                {
                    EntityId = e.EntityId.ToString(),
                    Tag = e.Tag.ToLowerInvariant()
                });
            
            // Decrement usage count
            await connection.ExecuteAsync(
                @"UPDATE tag_vocabulary 
                  SET usage_count = MAX(0, usage_count - 1)
                  WHERE tag = @Tag",
                new { Tag = e.Tag.ToLowerInvariant() });
            
            _logger.Debug($"[{Name}] Tag removed: '{e.Tag}' from {e.EntityType} {e.EntityId}");
        }
        
        // =============================================================================
        // LEGACY EVENT HANDLERS (For backward compatibility during migration)
        // =============================================================================
        
        private async Task HandleFolderTaggedAsync(FolderTaggedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            // First, remove all existing tags for this folder
            await connection.ExecuteAsync(
                "DELETE FROM entity_tags WHERE entity_id = @EntityId AND entity_type = 'category'",
                new { EntityId = e.FolderId.ToString() });
            
            // Add new tags
            foreach (var tag in e.Tags)
            {
                // Ensure tag in vocabulary
                await connection.ExecuteAsync(
                    @"INSERT OR IGNORE INTO tag_vocabulary 
                      (tag, display_name, usage_count, first_used_at, last_used_at)
                      VALUES (@Tag, @DisplayName, 0, @CreatedAt, @CreatedAt)",
                    new
                    {
                        Tag = tag.ToLowerInvariant(),
                        DisplayName = tag,
                        CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                    });
                
                // Add to entity_tags
                await connection.ExecuteAsync(
                    @"INSERT OR REPLACE INTO entity_tags 
                      (entity_id, entity_type, tag, display_name, source, created_at)
                      VALUES (@EntityId, @EntityType, @Tag, @DisplayName, @Source, @CreatedAt)",
                    new
                    {
                        EntityId = e.FolderId.ToString(),
                        EntityType = "category",
                        Tag = tag.ToLowerInvariant(),
                        DisplayName = tag,
                        Source = "manual",
                        CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                    });
                
                // Increment usage
                await connection.ExecuteAsync(
                    @"UPDATE tag_vocabulary 
                      SET usage_count = usage_count + 1, last_used_at = @LastUsedAt
                      WHERE tag = @Tag",
                    new
                    {
                        Tag = tag.ToLowerInvariant(),
                        LastUsedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                    });
            }
            
            _logger.Debug($"[{Name}] Folder tagged: {e.FolderId} with {e.Tags.Count} tags");
        }
        
        private async Task HandleFolderUntaggedAsync(FolderUntaggedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            foreach (var tag in e.RemovedTags)
            {
                // Remove from entity_tags
                await connection.ExecuteAsync(
                    "DELETE FROM entity_tags WHERE entity_id = @EntityId AND tag = @Tag",
                    new
                    {
                        EntityId = e.FolderId.ToString(),
                        Tag = tag.ToLowerInvariant()
                    });
                
                // Decrement usage
                await connection.ExecuteAsync(
                    "UPDATE tag_vocabulary SET usage_count = MAX(0, usage_count - 1) WHERE tag = @Tag",
                    new { Tag = tag.ToLowerInvariant() });
            }
            
            _logger.Debug($"[{Name}] Folder untagged: {e.FolderId}, removed {e.RemovedTags.Count} tags");
        }
        
        private async Task HandleNoteTaggedAsync(NoteTaggedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            // Remove existing tags
            await connection.ExecuteAsync(
                "DELETE FROM entity_tags WHERE entity_id = @EntityId AND entity_type = 'note'",
                new { EntityId = e.NoteId.ToString() });
            
            // Add new tags
            foreach (var tag in e.Tags)
            {
                // Ensure tag in vocabulary
                await connection.ExecuteAsync(
                    @"INSERT OR IGNORE INTO tag_vocabulary 
                      (tag, display_name, usage_count, first_used_at, last_used_at)
                      VALUES (@Tag, @DisplayName, 0, @CreatedAt, @CreatedAt)",
                    new
                    {
                        Tag = tag.ToLowerInvariant(),
                        DisplayName = tag,
                        CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                    });
                
                // Add to entity_tags
                await connection.ExecuteAsync(
                    @"INSERT OR REPLACE INTO entity_tags 
                      (entity_id, entity_type, tag, display_name, source, created_at)
                      VALUES (@EntityId, @EntityType, @Tag, @DisplayName, @Source, @CreatedAt)",
                    new
                    {
                        EntityId = e.NoteId.ToString(),
                        EntityType = "note",
                        Tag = tag.ToLowerInvariant(),
                        DisplayName = tag,
                        Source = "manual",
                        CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                    });
                
                // Increment usage
                await connection.ExecuteAsync(
                    @"UPDATE tag_vocabulary 
                      SET usage_count = usage_count + 1, last_used_at = @LastUsedAt
                      WHERE tag = @Tag",
                    new
                    {
                        Tag = tag.ToLowerInvariant(),
                        LastUsedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                    });
            }
            
            _logger.Debug($"[{Name}] Note tagged: {e.NoteId} with {e.Tags.Count} tags");
        }
        
        private async Task HandleNoteUntaggedAsync(NoteUntaggedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            foreach (var tag in e.RemovedTags)
            {
                // Remove from entity_tags
                await connection.ExecuteAsync(
                    "DELETE FROM entity_tags WHERE entity_id = @EntityId AND tag = @Tag",
                    new
                    {
                        EntityId = e.NoteId.ToString(),
                        Tag = tag.ToLowerInvariant()
                    });
                
                // Decrement usage
                await connection.ExecuteAsync(
                    "UPDATE tag_vocabulary SET usage_count = MAX(0, usage_count - 1) WHERE tag = @Tag",
                    new { Tag = tag.ToLowerInvariant() });
            }
            
            _logger.Debug($"[{Name}] Note untagged: {e.NoteId}, removed {e.RemovedTags.Count} tags");
        }
        
        // =============================================================================
        // NEW EVENT-SOURCED TAG HANDLERS
        // =============================================================================
        
        private async Task HandleCategoryTagsSetAsync(CategoryTagsSet e)
        {
            using var connection = await OpenConnectionAsync();
            
            // First, remove all existing tags for this category
            await connection.ExecuteAsync(
                "DELETE FROM entity_tags WHERE entity_id = @EntityId AND entity_type = 'category'",
                new { EntityId = e.CategoryId.ToString() });
            
            // Add new tags
            foreach (var tag in e.Tags)
            {
                // Ensure tag in vocabulary
                await connection.ExecuteAsync(
                    @"INSERT OR IGNORE INTO tag_vocabulary 
                      (tag, display_name, usage_count, first_used_at, last_used_at)
                      VALUES (@Tag, @DisplayName, 0, @CreatedAt, @CreatedAt)",
                    new
                    {
                        Tag = tag.ToLowerInvariant(),
                        DisplayName = tag,
                        CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                    });
                
                // Add to entity_tags
                await connection.ExecuteAsync(
                    @"INSERT OR REPLACE INTO entity_tags 
                      (entity_id, entity_type, tag, display_name, source, created_at)
                      VALUES (@EntityId, @EntityType, @Tag, @DisplayName, @Source, @CreatedAt)",
                    new
                    {
                        EntityId = e.CategoryId.ToString(),
                        EntityType = "category",
                        Tag = tag.ToLowerInvariant(),
                        DisplayName = tag,
                        Source = "manual",
                        CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                    });
                
                // Increment usage
                await connection.ExecuteAsync(
                    @"UPDATE tag_vocabulary 
                      SET usage_count = usage_count + 1, last_used_at = @LastUsedAt
                      WHERE tag = @Tag",
                    new
                    {
                        Tag = tag.ToLowerInvariant(),
                        LastUsedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                    });
            }
            
            _logger.Debug($"[{Name}] ✅ Category tags set: {e.CategoryId} with {e.Tags.Count} tags (event-sourced)");
        }
        
        private async Task HandleNoteTagsSetAsync(NoteTagsSet e)
        {
            using var connection = await OpenConnectionAsync();
            
            // First, remove all existing tags for this note
            await connection.ExecuteAsync(
                "DELETE FROM entity_tags WHERE entity_id = @EntityId AND entity_type = 'note'",
                new { EntityId = e.NoteId.Value });
            
            // Add new tags
            foreach (var tag in e.Tags)
            {
                // Ensure tag in vocabulary
                await connection.ExecuteAsync(
                    @"INSERT OR IGNORE INTO tag_vocabulary 
                      (tag, display_name, usage_count, first_used_at, last_used_at)
                      VALUES (@Tag, @DisplayName, 0, @CreatedAt, @CreatedAt)",
                    new
                    {
                        Tag = tag.ToLowerInvariant(),
                        DisplayName = tag,
                        CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                    });
                
                // Add to entity_tags
                await connection.ExecuteAsync(
                    @"INSERT OR REPLACE INTO entity_tags 
                      (entity_id, entity_type, tag, display_name, source, created_at)
                      VALUES (@EntityId, @EntityType, @Tag, @DisplayName, @Source, @CreatedAt)",
                    new
                    {
                        EntityId = e.NoteId.Value,
                        EntityType = "note",
                        Tag = tag.ToLowerInvariant(),
                        DisplayName = tag,
                        Source = "manual",
                        CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                    });
                
                // Increment usage
                await connection.ExecuteAsync(
                    @"UPDATE tag_vocabulary 
                      SET usage_count = usage_count + 1, last_used_at = @LastUsedAt
                      WHERE tag = @Tag",
                    new
                    {
                        Tag = tag.ToLowerInvariant(),
                        LastUsedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                    });
            }
            
            _logger.Debug($"[{Name}] ✅ Note tags set: {e.NoteId} with {e.Tags.Count} tags (event-sourced)");
        }
    }
}

