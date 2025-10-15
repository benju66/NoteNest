using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.FolderTags.Repositories;
using NoteNest.Application.NoteTags.Repositories;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;

namespace NoteNest.UI.Plugins.TodoPlugin.Services;

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
/// Manages tag inheritance from folders to todos.
/// Follows CQRS patterns - this is a service, not a command handler.
/// </summary>
public class TagInheritanceService : ITagInheritanceService
{
    private readonly IFolderTagRepository _folderTagRepository;
    private readonly INoteTagRepository _noteTagRepository;
    private readonly ITodoTagRepository _todoTagRepository;
    private readonly ITodoRepository _todoRepository;
    private readonly IAppLogger _logger;

    public TagInheritanceService(
        IFolderTagRepository folderTagRepository,
        INoteTagRepository noteTagRepository,
        ITodoTagRepository todoTagRepository,
        ITodoRepository todoRepository,
        IAppLogger logger)
    {
        _folderTagRepository = folderTagRepository ?? throw new ArgumentNullException(nameof(folderTagRepository));
        _noteTagRepository = noteTagRepository ?? throw new ArgumentNullException(nameof(noteTagRepository));
        _todoTagRepository = todoTagRepository ?? throw new ArgumentNullException(nameof(todoTagRepository));
        _todoRepository = todoRepository ?? throw new ArgumentNullException(nameof(todoRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<string>> GetApplicableTagsAsync(Guid folderId)
    {
        try
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Get inherited tags from folder and ancestors
            var inheritedTags = await _folderTagRepository.GetInheritedTagsAsync(folderId);
            
            foreach (var tag in inheritedTags)
            {
                tags.Add(tag.Tag);
            }

            _logger.Info($"Found {tags.Count} applicable tags for folder {folderId}");
            return tags.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get applicable tags for folder {folderId}", ex);
            return new List<string>();
        }
    }

    public async Task UpdateTodoTagsAsync(Guid todoId, Guid? oldFolderId, Guid? newFolderId, Guid? noteId = null)
    {
        try
        {
            _logger.Info($"Updating todo {todoId} tags: moving from {oldFolderId} to {newFolderId}, noteId: {noteId}");

            // Step 1: Remove old folder's auto-tags (if any)
            if (oldFolderId.HasValue && oldFolderId.Value != Guid.Empty)
            {
                await RemoveInheritedTagsAsync(todoId, oldFolderId.Value);
            }

            // Step 2: Get tags from folder (if any)
            var folderTags = new List<string>();
            if (newFolderId.HasValue && newFolderId.Value != Guid.Empty)
            {
                folderTags = await GetApplicableTagsAsync(newFolderId.Value);
            }

            // Step 3: Get tags from note (if any)
            var noteTags = new List<string>();
            if (noteId.HasValue && noteId.Value != Guid.Empty)
            {
                var noteTagList = await _noteTagRepository.GetNoteTagsAsync(noteId.Value);
                noteTags = noteTagList.Select(t => t.Tag).ToList();
                _logger.Info($"Found {noteTags.Count} tags on source note {noteId.Value}");
            }

            // Step 4: Merge folder and note tags (union - no duplicates)
            var allTags = folderTags.Union(noteTags, StringComparer.OrdinalIgnoreCase).ToList();
            
            // Step 5: Apply all tags to todo
            foreach (var tag in allTags)
            {
                // Add as auto-tag (is_auto = 1) so we can remove it later if needed
                var existingTags = await _todoTagRepository.GetByTodoIdAsync(todoId);
                if (!existingTags.Any(t => t.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                {
                    await _todoTagRepository.AddAsync(new Infrastructure.Persistence.Models.TodoTag
                    {
                        TodoId = todoId,
                        Tag = tag,
                        IsAuto = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            _logger.Info($"Added {allTags.Count} inherited tags to todo {todoId} (folder: {folderTags.Count}, note: {noteTags.Count})");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to update todo tags for {todoId}", ex);
            throw;
        }
    }

    public async Task BulkUpdateFolderTodosAsync(Guid folderId, List<string> newTags)
    {
        try
        {
            _logger.Info($"Bulk updating todos in folder {folderId} with {newTags.Count} tags");

            // Get all todos in this folder
            var allTodos = await _todoRepository.GetAllAsync();
            var todosInFolder = allTodos.Where(t => t.CategoryId == folderId).ToList();

            _logger.Info($"Found {todosInFolder.Count} todos in folder {folderId}");

            foreach (var todo in todosInFolder)
            {
                // Remove old auto-tags
                await _todoTagRepository.DeleteAutoTagsAsync(todo.Id);

                // Add new tags
                foreach (var tag in newTags)
                {
                    await _todoTagRepository.AddAsync(new Infrastructure.Persistence.Models.TodoTag
                    {
                        TodoId = todo.Id,
                        Tag = tag,
                        IsAuto = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            _logger.Info($"Successfully updated {todosInFolder.Count} todos with new folder tags");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to bulk update folder todos for {folderId}", ex);
            throw;
        }
    }

    public async Task RemoveInheritedTagsAsync(Guid todoId, Guid folderId)
    {
        try
        {
            // Get folder's tags (including inherited)
            var folderTags = await GetApplicableTagsAsync(folderId);
            
            // Get todo's current tags
            var todoTags = await _todoTagRepository.GetByTodoIdAsync(todoId);
            
            // Remove only auto-tags that match folder tags
            foreach (var todoTag in todoTags.Where(t => t.IsAuto))
            {
                if (folderTags.Contains(todoTag.Tag, StringComparer.OrdinalIgnoreCase))
                {
                    await _todoTagRepository.DeleteAsync(todoId, todoTag.Tag);
                }
            }

            _logger.Info($"Removed inherited tags from todo {todoId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove inherited tags from todo {todoId}", ex);
            throw;
        }
    }
}

