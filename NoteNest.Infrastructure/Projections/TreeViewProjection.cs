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
            
            // Update all child notes' paths to reflect the category rename
            await UpdateChildNotePaths(connection, e.CategoryId.ToString(), e.NewPath);
            
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
            
            // Update all child notes' paths to reflect the category move
            await UpdateChildNotePaths(connection, e.CategoryId.ToString(), e.NewPath);
            
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
            
            var noteIdStr = e.NoteId.Value;
            var categoryIdStr = e.CategoryId.Value;
            
            // Query parent category to get its full path
            var categoryPath = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT display_path FROM tree_view WHERE id = @CategoryId",
                new { CategoryId = categoryIdStr });
            
            // Build full relative path: "Notes/Category/Subcategory/NoteTitle"
            string displayPath = e.Title;
            if (!string.IsNullOrEmpty(categoryPath))
            {
                // Category path already has the full path, append note title
                displayPath = System.IO.Path.Combine(categoryPath, e.Title);
            }
            
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
                    CanonicalPath = $"notes/{noteIdStr}",
                    DisplayPath = displayPath,  // Full path: "Notes/Category/NoteTitle"
                    NodeType = "note",
                    Name = e.Title,
                    FileExtension = ".rtf",
                    IsPinned = 0,
                    SortOrder = 0,
                    CreatedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds(),
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Debug($"[{Name}] Note created: {e.Title} with path: {displayPath}");
        }
        
        private async Task HandleNoteRenamedAsync(NoteRenamedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            // Get the current note to find its category
            var currentNote = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT parent_id FROM tree_view WHERE id = @Id",
                new { Id = e.NoteId.Value });
            
            if (currentNote != null)
            {
                // Query parent category to get its full path
                var categoryPath = await connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT display_path FROM tree_view WHERE id = @CategoryId",
                    new { CategoryId = (string)currentNote.parent_id });
                
                // Build new display path with new title
                string displayPath = e.NewTitle;
                if (!string.IsNullOrEmpty(categoryPath))
                {
                    displayPath = System.IO.Path.Combine(categoryPath, e.NewTitle);
                }
                
                await connection.ExecuteAsync(
                    @"UPDATE tree_view 
                      SET name = @Name, display_path = @DisplayPath, modified_at = @ModifiedAt
                      WHERE id = @Id",
                    new
                    {
                        Id = e.NoteId.Value,
                        Name = e.NewTitle,
                        DisplayPath = displayPath,
                        ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                    });
                
                _logger.Debug($"[{Name}] Note renamed: {e.OldTitle} → {e.NewTitle}, path updated to: {displayPath}");
            }
        }
        
        private async Task HandleNoteMovedAsync(NoteMovedEvent e)
        {
            using var connection = await OpenConnectionAsync();
            
            // Get the note's current name
            var noteName = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT name FROM tree_view WHERE id = @Id",
                new { Id = e.NoteId.Value });
            
            // Query new parent category to get its full path
            var categoryPath = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT display_path FROM tree_view WHERE id = @CategoryId",
                new { CategoryId = e.ToCategoryId.Value });
            
            // Build new display path in new category
            string displayPath = noteName;
            if (!string.IsNullOrEmpty(categoryPath))
            {
                displayPath = System.IO.Path.Combine(categoryPath, noteName);
            }
            
            await connection.ExecuteAsync(
                @"UPDATE tree_view 
                  SET parent_id = @ParentId, display_path = @DisplayPath, modified_at = @ModifiedAt
                  WHERE id = @Id",
                new
                {
                    Id = e.NoteId.Value,
                    ParentId = e.ToCategoryId.Value,
                    DisplayPath = displayPath,
                    ModifiedAt = new DateTimeOffset(e.OccurredAt).ToUnixTimeSeconds()
                });
            
            _logger.Debug($"[{Name}] Note moved: {e.NoteId} to category {e.ToCategoryId}, new path: {displayPath}");
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
        
        // =============================================================================
        // HELPER METHODS
        // =============================================================================
        
        /// <summary>
        /// Recursively updates display paths for all notes in a category and its subcategories.
        /// Called when a category is renamed or moved to cascade path updates to child notes.
        /// </summary>
        private async Task UpdateChildNotePaths(SqliteConnection connection, string categoryId, string categoryPath)
        {
            // Get all notes in this category
            var notes = await connection.QueryAsync<dynamic>(
                "SELECT id, name FROM tree_view WHERE parent_id = @CategoryId AND node_type = 'note'",
                new { CategoryId = categoryId });
            
            foreach (var note in notes)
            {
                var newNotePath = System.IO.Path.Combine(categoryPath, (string)note.name);
                await connection.ExecuteAsync(
                    "UPDATE tree_view SET display_path = @DisplayPath WHERE id = @Id",
                    new { Id = (string)note.id, DisplayPath = newNotePath });
                
                _logger.Debug($"Updated note path: {note.name} -> {newNotePath}");
            }
            
            // Recursively update child categories and their notes
            var childCategories = await connection.QueryAsync<dynamic>(
                "SELECT id, display_path FROM tree_view WHERE parent_id = @CategoryId AND node_type = 'category'",
                new { CategoryId = categoryId });
            
            foreach (var child in childCategories)
            {
                await UpdateChildNotePaths(connection, (string)child.id, (string)child.display_path);
            }
        }
    }
}

