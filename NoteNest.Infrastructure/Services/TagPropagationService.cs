using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Application.Queries;
using NoteNest.Application.Tags.Services;
using NoteNest.Domain.Common;
using NoteNest.Domain.Categories.Events;
using NoteNest.Domain.Notes;

namespace NoteNest.Infrastructure.Services
{
    /// <summary>
    /// Background service that propagates category tags to child items.
    /// Subscribes to CategoryTagsSet events and updates existing children without blocking UI.
    /// Uses batching and throttling to prevent performance issues.
    /// </summary>
    public class TagPropagationService : IHostedService
    {
        private readonly Core.Services.IEventBus _eventBus;
        private readonly IEventStore _eventStore;
        private readonly ITagQueryService _tagQueryService;
        private readonly IProjectionOrchestrator _projectionOrchestrator;
        private readonly ITagPropagationService _tagPropagationService;
        private readonly IStatusNotifier _statusNotifier;
        private readonly string _projectionsConnectionString;
        private readonly IAppLogger _logger;
        
        // Batching configuration
        private const int BATCH_SIZE = 10;  // Process 10 items at a time
        private const int BATCH_DELAY_MS = 100;  // 100ms between batches
        private const int MAX_RETRIES = 3;  // Retry on concurrency conflicts
        
        public TagPropagationService(
            Core.Services.IEventBus eventBus,
            IEventStore eventStore,
            ITagQueryService tagQueryService,
            IProjectionOrchestrator projectionOrchestrator,
            ITagPropagationService tagPropagationService,
            IStatusNotifier statusNotifier,
            string projectionsConnectionString,
            IAppLogger logger)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _tagQueryService = tagQueryService ?? throw new ArgumentNullException(nameof(tagQueryService));
            _projectionOrchestrator = projectionOrchestrator ?? throw new ArgumentNullException(nameof(projectionOrchestrator));
            _tagPropagationService = tagPropagationService ?? throw new ArgumentNullException(nameof(tagPropagationService));
            _statusNotifier = statusNotifier ?? throw new ArgumentNullException(nameof(statusNotifier));
            _projectionsConnectionString = projectionsConnectionString ?? throw new ArgumentNullException(nameof(projectionsConnectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.Info("🚀 Starting TagPropagationService...");
            
            // Subscribe to CategoryTagsSet events for background tag propagation
            _eventBus.Subscribe<IDomainEvent>(async domainEvent =>
            {
                if (domainEvent is CategoryTagsSet e)
                {
                    // Fire-and-forget background processing (non-blocking)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await PropagateTagsToChildrenAsync(e, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, $"Failed to propagate tags for category {e.CategoryId}");
                            // Don't rethrow - background task failures shouldn't crash app
                        }
                    }, cancellationToken);
                }
            });
            
            _logger.Info("✅ TagPropagationService subscribed to CategoryTagsSet events");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Stopping TagPropagationService...");
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Propagate tags to all child items (notes, todos, subcategories).
        /// Runs asynchronously in background without blocking UI.
        /// </summary>
        private async Task PropagateTagsToChildrenAsync(CategoryTagsSet tagEvent, CancellationToken cancellationToken)
        {
            if (!tagEvent.InheritToChildren)
            {
                _logger.Info($"InheritToChildren = false for category {tagEvent.CategoryId}, skipping propagation");
                return;
            }
            
            _logger.Info($"🔄 Starting background tag propagation for category {tagEvent.CategoryId}");
            
            try
            {
                // Step 1: Get all descendant notes
                var noteIds = await GetDescendantNotesAsync(tagEvent.CategoryId);
                
                // Step 2: Get all applicable tags (category's tags + inherited from parents)
                var inheritedFromParents = await GetParentCategoryTagsAsync(tagEvent.CategoryId);
                var allTags = tagEvent.Tags
                    .Union(inheritedFromParents, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                
                _logger.Info($"Found {noteIds.Count} notes to update with {allTags.Count} tags");
                
                // Step 3: Show status notification
                var totalItems = noteIds.Count;
                if (totalItems > 0)
                {
                    _statusNotifier.ShowStatus(
                        $"🔄 Applying tags to {totalItems} items in background...",
                        StatusType.InProgress,
                        duration: 5000);
                }
                
                // Step 4: Update notes in batches
                var updated = await UpdateNotesBatchedAsync(noteIds, allTags, cancellationToken);
                
                // Step 5: Update todos via ITagPropagationService (TodoPlugin implements this)
                try
                {
                    await _tagPropagationService.BulkUpdateFolderTodosAsync(tagEvent.CategoryId, allTags);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to update todos (non-fatal): {ex.Message}");
                }
                
                // Step 6: Show completion notification
                if (totalItems > 0)
                {
                    _statusNotifier.ShowStatus(
                        $"✅ Updated {updated} items with tags",
                        StatusType.Success,
                        duration: 3000);
                }
                
                _logger.Info($"✅ Tag propagation complete for category {tagEvent.CategoryId}: {updated}/{totalItems} notes updated");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to propagate tags for category {tagEvent.CategoryId}");
                _statusNotifier.ShowStatus(
                    $"⚠️ Tag propagation encountered errors (see logs)",
                    StatusType.Warning,
                    duration: 5000);
            }
        }
        
        /// <summary>
        /// Get all descendant notes recursively using SQL CTE.
        /// Returns direct children + all nested children.
        /// </summary>
        private async Task<List<Guid>> GetDescendantNotesAsync(Guid categoryId)
        {
            try
            {
                using var connection = new SqliteConnection(_projectionsConnectionString);
                await connection.OpenAsync();
                
                // Recursive CTE to get all descendant categories, then their notes
                var sql = @"
                    WITH RECURSIVE category_tree AS (
                        -- Start with target category
                        SELECT id FROM tree_view 
                        WHERE id = @CategoryId AND node_type = 'category'
                        
                        UNION ALL
                        
                        -- Walk down to child categories
                        SELECT tv.id 
                        FROM tree_view tv
                        INNER JOIN category_tree ct ON tv.parent_id = ct.id
                        WHERE tv.node_type = 'category'
                    )
                    SELECT DISTINCT n.id
                    FROM tree_view n
                    WHERE n.node_type = 'note' 
                      AND n.parent_id IN (SELECT id FROM category_tree)";
                
                var noteIdStrings = await connection.QueryAsync<string>(sql, new { CategoryId = categoryId.ToString() });
                var noteIds = noteIdStrings.Select(Guid.Parse).ToList();
                
                _logger.Debug($"Found {noteIds.Count} descendant notes for category {categoryId}");
                return noteIds;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get descendant notes for category {categoryId}");
                return new List<Guid>();
            }
        }
        
        /// <summary>
        /// Update notes in batches with retry logic for concurrency conflicts.
        /// Preserves manual tags, merges with inherited tags.
        /// </summary>
        private async Task<int> UpdateNotesBatchedAsync(List<Guid> noteIds, List<string> inheritedTags, CancellationToken cancellationToken)
        {
            int processedCount = 0;
            
            for (int i = 0; i < noteIds.Count; i += BATCH_SIZE)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info($"Tag propagation cancelled after {processedCount}/{noteIds.Count} notes");
                    break;
                }
                
                var batch = noteIds.Skip(i).Take(BATCH_SIZE).ToList();
                
                foreach (var noteId in batch)
                {
                    try
                    {
                        var success = await UpdateNoteWithTagsAsync(noteId, inheritedTags);
                        if (success)
                            processedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to update note {noteId}: {ex.Message}");
                        // Continue with other notes
                    }
                }
                
                // Update projections after each batch
                await _projectionOrchestrator.CatchUpAsync();
                
                // Small delay between batches to avoid overwhelming the system
                if (i + BATCH_SIZE < noteIds.Count)
                {
                    await Task.Delay(BATCH_DELAY_MS, cancellationToken);
                }
            }
            
            return processedCount;
        }
        
        /// <summary>
        /// Update a single note with tags, preserving manual tags.
        /// Includes retry logic for concurrency conflicts.
        /// </summary>
        private async Task<bool> UpdateNoteWithTagsAsync(Guid noteId, List<string> inheritedTags)
        {
            for (int attempt = 0; attempt < MAX_RETRIES; attempt++)
            {
                try
                {
                    // Query manual tags from projection (preserve user's explicit tags)
                    var manualTags = await GetManualTagsForNoteAsync(noteId);
                    
                    // Merge manual tags with inherited tags (Union deduplicates)
                    var combinedTags = manualTags
                        .Union(inheritedTags, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    
                    // Load note aggregate
                    var note = await _eventStore.LoadAsync<Note>(noteId);
                    if (note == null)
                    {
                        _logger.Warning($"Note {noteId} not found in event store, skipping");
                        return false;
                    }
                    
                    // Set combined tags
                    note.SetTags(combinedTags);
                    
                    // Save to event store
                    await _eventStore.SaveAsync(note);
                    
                    return true; // Success!
                }
                catch (ConcurrencyException ex)
                {
                    if (attempt < MAX_RETRIES - 1)
                    {
                        _logger.Warning($"Concurrency conflict updating note {noteId}, retry {attempt + 1}/{MAX_RETRIES}");
                        await Task.Delay(100 * (attempt + 1)); // Exponential backoff: 100ms, 200ms, 300ms
                        continue; // Retry
                    }
                    else
                    {
                        _logger.Error($"Failed to update note {noteId} after {MAX_RETRIES} attempts due to concurrency");
                        return false; // Give up after retries
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to update note {noteId}");
                    return false; // Non-retriable error
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Get manual tags for a note (preserves user's explicit tagging).
        /// Queries projection's entity_tags with source='manual' filter.
        /// </summary>
        private async Task<List<string>> GetManualTagsForNoteAsync(Guid noteId)
        {
            try
            {
                var allTags = await _tagQueryService.GetTagsForEntityAsync(noteId, "note");
                var manualTags = allTags
                    .Where(t => t.Source == "manual")
                    .Select(t => t.DisplayName)
                    .ToList();
                
                return manualTags;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get manual tags for note {noteId}");
                return new List<string>();
            }
        }
        
        /// <summary>
        /// Get parent category tags recursively.
        /// Walks up tree collecting ancestor tags.
        /// </summary>
        private async Task<List<string>> GetParentCategoryTagsAsync(Guid categoryId)
        {
            try
            {
                using var connection = new SqliteConnection(_projectionsConnectionString);
                await connection.OpenAsync();
                
                // Recursive CTE to walk UP tree collecting parent tags
                var sql = @"
                    WITH RECURSIVE category_hierarchy AS (
                        -- Start with target category's parent
                        SELECT parent_id as id
                        FROM tree_view
                        WHERE id = @CategoryId AND node_type = 'category' AND parent_id IS NOT NULL
                        
                        UNION ALL
                        
                        -- Walk up to ancestor categories
                        SELECT tv.parent_id as id
                        FROM tree_view tv
                        INNER JOIN category_hierarchy ch ON tv.id = ch.id
                        WHERE tv.parent_id IS NOT NULL AND tv.node_type = 'category'
                    )
                    SELECT DISTINCT et.display_name
                    FROM category_hierarchy ch
                    INNER JOIN entity_tags et ON et.entity_id = ch.id
                    WHERE et.entity_type = 'category'";
                
                var tagNames = await connection.QueryAsync<string>(sql, new { CategoryId = categoryId.ToString() });
                var parentTags = tagNames.ToList();
                
                _logger.Debug($"Found {parentTags.Count} parent tags for category {categoryId}");
                return parentTags;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get parent tags for category {categoryId}");
                return new List<string>();
            }
        }
    }
}

