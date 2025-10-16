using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Domain.Notes.Events;
using NoteNest.Domain.Categories.Events;
using NoteNest.Domain.Trees;

namespace NoteNest.Infrastructure.Projections
{
    /// <summary>
    /// Builds tree_view projection from note and category events.
    /// Replaces tree.db with event-sourced read model.
    /// </summary>
    public class TreeViewProjection : BaseProjection
    {
        public override string Name => "TreeView";
        
        public TreeViewProjection(string connectionString, IAppLogger logger)
            : base(connectionString, logger)
        {
        }
        
        public override async Task HandleAsync(IDomainEvent @event)
        {
            switch (@event)
            {
                // Category events
                case CategoryCreated e:
                    await HandleCategoryCreatedAsync(e);
                    break;
                    
                case CategoryRenamed e:
                    await HandleCategoryRenamedAsync(e);
                    break;
                    
                case CategoryMoved e:
                    await HandleCategoryMovedAsync(e);
                    break;
                    
                case CategoryDeleted e:
                    await HandleCategoryDeletedAsync(e);
                    break;
                    
                case CategoryPinned e:
                    await HandleCategoryPinnedAsync(e);
                    break;
                    
                case CategoryUnpinned e:
                    await HandleCategoryUnpinnedAsync(e);
                    break;
                    
                // Note events
                case NoteCreatedEvent e:
                    await HandleNoteCreatedAsync(e);
                    break;
                    
                case NoteRenamedEvent e:
                    await HandleNoteRenamedAsync(e);
                    break;
                    
                case NoteMovedEvent e:
                    await HandleNoteMovedAsync(e);
                    break;
                    
                case NotePinnedEvent e:
                    await HandleNotePinnedAsync(e);
                    break;
                    
                case NoteUnpinnedEvent e:
                    await HandleNoteUnpinnedAsync(e);
                    break;
                    
                case NoteDeletedEvent e:
                    await HandleNoteDeletedAsync(e);
                    break;
            }
        }
        
        protected override async Task ClearProjectionDataAsync()
        {
            using var connection = await OpenConnectionAsync();
            await connection.ExecuteAsync("DELETE FROM tree_view");
            _logger.Info($"[{Name}] Cleared projection data");
        }
        
        // =============================================================================
        // CATEGORY EVENT HANDLERS
        // =============================================================================
        
        private async Task HandleCategoryCreatedAsync(CategoryCreated e)
        {
            using var connection = await OpenConnectionAsync();
            
            await connection.ExecuteAsync(
                @"INSERT INTO tree_view 
                  (id, parent_id, canonical_path, display_path, node_type, name,
                   file_extension, is_pinned, sort_order, created_at, modified_at)
                  VALUES 
                  (@Id, @ParentId, @CanonicalPath, @DisplayPath, @NodeType, @Name,
                   @FileExtension, @IsPinned, @SortOrder, @CreatedAt, @ModifiedAt)",
                new
                {
                    Id = e.CategoryId.ToString(),
                    ParentId = e.ParentId?.ToString(),
                    CanonicalPath = e.Path.ToLowerInvariant(),
                    DisplayPath = e.Path,
                    NodeType = "category",
                    Name = e.Name,
                    FileExtension = (string)null,
                    IsPinned = 0,
                    SortOrder = 0,
                    CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds(),
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Debug($"[{Name}] Category created: {e.Name} at {e.Path}");
        }
        
        private async Task HandleCategoryRenamedAsync(CategoryRenamed e)
        {
            using var connection = await OpenConnectionAsync();
            
            await connection.ExecuteAsync(
                @"UPDATE tree_view 
                  SET name = @Name, canonical_path = @CanonicalPath, display_path = @DisplayPath, modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.CategoryId.ToString(),
                    Name = e.NewName,
                    CanonicalPath = e.NewPath.ToLowerInvariant(),
                    DisplayPath = e.NewPath,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Debug($"[{Name}] Category renamed: {e.OldName} → {e.NewName}");
        }
        
        private async Task HandleCategoryMovedAsync(CategoryMoved e)
        {
            using var connection = await OpenConnectionAsync();
            
            await connection.ExecuteAsync(
                @"UPDATE tree_view 
                  SET parent_id = @ParentId, canonical_path = @CanonicalPath, display_path = @DisplayPath, modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.CategoryId.ToString(),
                    ParentId = e.NewParentId?.ToString(),
                    CanonicalPath = e.NewPath.ToLowerInvariant(),
                    DisplayPath = e.NewPath,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Debug($"[{Name}] Category moved: {e.CategoryId} to parent {e.NewParentId}");
        }
        
        private async Task HandleCategoryDeletedAsync(CategoryDeleted e)
        {
            using var connection = await OpenConnectionAsync();
            
            await connection.ExecuteAsync(
                "DELETE FROM tree_view WHERE id = @Id",
                new { Id = e.CategoryId.ToString() });
            
            _logger.Debug($"[{Name}] Category deleted: {e.Name}");
        }
        
        private async Task HandleCategoryPinnedAsync(CategoryPinned e)
        {
            using var connection = await OpenConnectionAsync();
            
            await connection.ExecuteAsync(
                "UPDATE tree_view SET is_pinned = 1 WHERE id = @Id",
                new { Id = e.CategoryId.ToString() });
            
            _logger.Debug($"[{Name}] Category pinned: {e.CategoryId}");
        }
        
        private async Task HandleCategoryUnpinnedAsync(CategoryUnpinned e)
        {
            using var connection = await OpenConnectionAsync();
            
            await connection.ExecuteAsync(
                "UPDATE tree_view SET is_pinned = 0 WHERE id = @Id",
                new { Id = e.CategoryId.ToString() });
            
            _logger.Debug($"[{Name}] Category unpinned: {e.CategoryId}");
        }
        
        // =============================================================================
        // NOTE EVENT HANDLERS
        // =============================================================================
        
        private async Task HandleNoteCreatedAsync(NoteCreatedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            // For now, we'll use the note ID as the canonical path
            // This will be enhanced when CategoryAggregate provides full paths
            var noteIdStr = e.NoteId.Value;
            var categoryIdStr = e.CategoryId.Value;
            
            await connection.ExecuteAsync(
                @"INSERT INTO tree_view 
                  (id, parent_id, canonical_path, display_path, node_type, name, 
                   file_extension, is_pinned, sort_order, created_at, modified_at)
                  VALUES 
                  (@Id, @ParentId, @CanonicalPath, @DisplayPath, @NodeType, @Name,
                   @FileExtension, @IsPinned, @SortOrder, @CreatedAt, @ModifiedAt)",
                new
                {
                    Id = noteIdStr,
                    ParentId = categoryIdStr,
                    CanonicalPath = $"notes/{noteIdStr}", // Temporary - will use real paths later
                    DisplayPath = e.Title,
                    NodeType = "note",
                    Name = e.Title,
                    FileExtension = ".rtf",
                    IsPinned = 0,
                    SortOrder = 0,
                    CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds(),
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Debug($"[{Name}] Note created: {e.Title}");
        }
        
        private async Task HandleNoteRenamedAsync(NoteRenamedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            await connection.ExecuteAsync(
                @"UPDATE tree_view 
                  SET name = @Name, display_path = @DisplayPath, modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.NoteId.Value,
                    Name = e.NewTitle,
                    DisplayPath = e.NewTitle,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Debug($"[{Name}] Note renamed: {e.OldTitle} → {e.NewTitle}");
        }
        
        private async Task HandleNoteMovedAsync(NoteMovedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            await connection.ExecuteAsync(
                @"UPDATE tree_view 
                  SET parent_id = @ParentId, modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.NoteId.Value,
                    ParentId = e.ToCategoryId.Value,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Debug($"[{Name}] Note moved: {e.NoteId} to category {e.ToCategoryId}");
        }
        
        private async Task HandleNotePinnedAsync(NotePinnedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            await connection.ExecuteAsync(
                "UPDATE tree_view SET is_pinned = 1 WHERE id = @Id",
                new { Id = e.NoteId.Value });
            
            _logger.Debug($"[{Name}] Note pinned: {e.NoteId}");
        }
        
        private async Task HandleNoteUnpinnedAsync(NoteUnpinnedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            await connection.ExecuteAsync(
                "UPDATE tree_view SET is_pinned = 0 WHERE id = @Id",
                new { Id = e.NoteId.Value });
            
            _logger.Debug($"[{Name}] Note unpinned: {e.NoteId}");
        }
        
        private async Task HandleNoteDeletedAsync(NoteDeletedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            await connection.ExecuteAsync(
                "DELETE FROM tree_view WHERE id = @Id",
                new { Id = e.NoteId.Value });
            
            _logger.Debug($"[{Name}] Note deleted: {e.NoteId}");
        }
    }
}

