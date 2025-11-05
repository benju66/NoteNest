using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Queries;
using NoteNest.Application.Tags.Services;

namespace NoteNest.UI.Plugins.TodoPlugin.Services
{
    /// <summary>
    /// Service for managing tag inheritance from folders to todos.
    /// </summary>
    public interface ITagInheritanceService
    {
        /// <summary>
        /// Get all tags that should be applied to an item in a folder.
        /// Includes folder's tags + inherited tags from ancestors.
        /// </summary>
        Task<List<string>> GetApplicableTagsAsync(Guid folderId);
        
        /// <summary>
        /// Update tags for a todo when it's created or moved.
        /// Removes old folder/note auto-tags, adds new folder/note tags.
        /// Preserves manual tags.
        /// </summary>
        Task UpdateTodoTagsAsync(Guid todoId, Guid? oldFolderId, Guid? newFolderId, Guid? noteId = null);
        
        /// <summary>
        /// Bulk update all todos in a folder with folder's tags.
        /// Used when user sets tags on existing folder.
        /// </summary>
        Task BulkUpdateFolderTodosAsync(Guid folderId, List<string> newTags);
        
        /// <summary>
        /// Remove folder-inherited tags from a todo.
        /// Used when a todo is moved out of a tagged folder.
        /// </summary>
        Task RemoveInheritedTagsAsync(Guid todoId, Guid folderId);
    }
    /// <summary>
    /// Tag inheritance service that reads from projections.db.
    /// Uses recursive CTEs to get inherited tags from category hierarchy.
    /// Note: This service is read-only. Tag modifications happen through events.
    /// </summary>
    public class ProjectionBasedTagInheritanceService : ITagInheritanceService, ITagPropagationService
    {
        private readonly string _connectionString;
        private readonly ITagQueryService _tagQueryService;
        private readonly IAppLogger _logger;

        public ProjectionBasedTagInheritanceService(
            string connectionString,
            ITagQueryService tagQueryService,
            IAppLogger logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _tagQueryService = tagQueryService ?? throw new ArgumentNullException(nameof(tagQueryService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<string>> GetApplicableTagsAsync(Guid folderId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Recursive CTE to get all parent categories and their tags
                var sql = @"
                    WITH RECURSIVE
                    -- Get category hierarchy starting from the given folder
                    category_hierarchy AS (
                        -- Start with the given category
                        SELECT id, parent_id, name
                        FROM tree_nodes
                        WHERE id = @FolderId AND node_type = 'category'
                        
                        UNION ALL
                        
                        -- Recursively get parent categories
                        SELECT t.id, t.parent_id, t.name
                        FROM tree_nodes t
                        INNER JOIN category_hierarchy h ON t.id = h.parent_id
                        WHERE t.node_type = 'category'
                    )
                    -- Get all tags from the hierarchy
                    SELECT DISTINCT et.display_name
                    FROM entity_tags et
                    INNER JOIN category_hierarchy h ON et.entity_id = h.id
                    WHERE et.entity_type = 'category'
                    ORDER BY et.display_name";

                var tags = await connection.QueryAsync<string>(sql, new { FolderId = folderId.ToString() });
                var tagList = tags.ToList();

                _logger.Info($"Found {tagList.Count} applicable tags for folder {folderId}");
                return tagList;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get applicable tags for folder {folderId}", ex);
                return new List<string>();
            }
        }

        public async Task UpdateTodoTagsAsync(Guid todoId, Guid? oldFolderId, Guid? newFolderId, Guid? noteId = null)
        {
            // In the event-sourced architecture, this method should not directly modify tags.
            // Instead, it should be called by command handlers that emit events.
            // The actual tag updates happen through the event store and projections.
            
            _logger.Info($"UpdateTodoTagsAsync called for todo {todoId} - this should be handled by command handlers emitting events");
            
            // This method is kept for interface compatibility but doesn't do direct updates
            await Task.CompletedTask;
        }

        public async Task BulkUpdateFolderTodosAsync(Guid folderId, List<string> newTags)
        {
            // In the event-sourced architecture, bulk updates should be done through commands
            // that emit events for each todo. This ensures proper event sourcing and audit trail.
            
            _logger.Info($"BulkUpdateFolderTodosAsync called for folder {folderId} - this should be handled by command handlers emitting events");
            
            // This method is kept for interface compatibility but doesn't do direct updates
            await Task.CompletedTask;
        }

        public async Task RemoveInheritedTagsAsync(Guid todoId, Guid folderId)
        {
            // In the event-sourced architecture, tag removal happens through events
            
            _logger.Info($"RemoveInheritedTagsAsync called for todo {todoId} - this should be handled by command handlers emitting events");
            
            // This method is kept for interface compatibility but doesn't do direct updates
            await Task.CompletedTask;
        }

        /// <summary>
        /// Get all tags for a specific entity including inherited tags.
        /// This is a read-only operation that queries projections.
        /// </summary>
        public async Task<List<TagInfo>> GetTagsForEntityWithInheritanceAsync(Guid entityId, string entityType)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                if (entityType == "todo")
                {
                    // For todos, get direct tags plus inherited from category
                    var sql = @"
                        WITH RECURSIVE
                        -- Get the todo's category
                        todo_category AS (
                            SELECT category_id
                            FROM todo_view
                            WHERE id = @EntityId
                        ),
                        -- Get category hierarchy
                        category_hierarchy AS (
                            -- Start with the todo's category
                            SELECT id, parent_id
                            FROM tree_nodes
                            WHERE id = (SELECT category_id FROM todo_category)
                              AND node_type = 'category'
                            
                            UNION ALL
                            
                            -- Recursively get parent categories
                            SELECT t.id, t.parent_id
                            FROM tree_nodes t
                            INNER JOIN category_hierarchy h ON t.id = h.parent_id
                            WHERE t.node_type = 'category'
                        )
                        -- Get all tags
                        SELECT 
                            et.tag,
                            et.display_name,
                            et.source,
                            et.entity_type,
                            et.entity_id,
                            datetime(et.created_at, 'unixepoch') as created_at
                        FROM entity_tags et
                        WHERE 
                            -- Direct todo tags
                            (et.entity_id = @EntityId AND et.entity_type = 'todo')
                            OR
                            -- Inherited category tags
                            (et.entity_id IN (SELECT id FROM category_hierarchy) AND et.entity_type = 'category')
                        ORDER BY et.source, et.display_name";

                    var tags = await connection.QueryAsync<TagInfo>(sql, new { EntityId = entityId.ToString() });
                    return tags.ToList();
                }
                else
                {
                    // For other entities, just get direct tags
                    var tags = await _tagQueryService.GetTagsForEntityAsync(entityId, entityType);
                    return tags.Select(t => new TagInfo
                    {
                        Tag = t.Tag,
                        DisplayName = t.DisplayName,
                        Source = t.Source,
                        EntityType = entityType,
                        EntityId = entityId,
                        CreatedAt = t.CreatedAt
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get tags for entity {entityId} of type {entityType}", ex);
                return new List<TagInfo>();
            }
        }
    }

    /// <summary>
    /// Tag information including inheritance details.
    /// </summary>
    public class TagInfo
    {
        public string Tag { get; set; }
        public string DisplayName { get; set; }
        public string Source { get; set; }
        public string EntityType { get; set; }
        public Guid EntityId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
