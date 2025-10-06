using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Common;
using NoteNest.Domain.Notes;
using NoteNest.Domain.Categories;
using NoteNest.Domain.Trees;
using NoteNest.Core.Services.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace NoteNest.Infrastructure.Database.Services
{
    /// <summary>
    /// Ultra-fast database-backed note repository for tree view.
    /// Provides instant note loading within categories via database queries.
    /// Adapted for Clean Architecture - replaces file system NoteRepository.
    /// </summary>
    public class NoteTreeDatabaseService : INoteRepository
    {
        private readonly ITreeDatabaseRepository _treeRepository;
        private readonly IAppLogger _logger;
        private readonly IMemoryCache _cache;
        
        private const string NOTES_CACHE_PREFIX = "notes_for_category_";
        private const string NOTE_CACHE_PREFIX = "note_";
        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(10);

        public NoteTreeDatabaseService(
            ITreeDatabaseRepository treeRepository,
            IAppLogger logger,
            IMemoryCache cache)
        {
            _treeRepository = treeRepository ?? throw new ArgumentNullException(nameof(treeRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        /// Get note by ID - FAST database lookup with RTF file fallback
        /// </summary>
        public async Task<Note> GetByIdAsync(NoteId id)
        {
            try
            {
                // Check cache first
                var cacheKey = $"{NOTE_CACHE_PREFIX}{id.Value}";
                if (_cache.TryGetValue(cacheKey, out Note cached))
                {
                    return cached;
                }

                // Try database lookup first (fast path)
                if (Guid.TryParse(id.Value, out var guid))
                {
                    var treeNode = await _treeRepository.GetNodeByIdAsync(guid);
                    if (treeNode?.NodeType == TreeNodeType.Note)
                    {
                        var note = await ConvertTreeNodeToNote(treeNode);
                        if (note != null)
                        {
                            _cache.Set(cacheKey, note, CACHE_DURATION);
                            _logger.Info($"⚡ Note loaded from database: {note.Title}");
                            return note;
                        }
                    }
                }

                // Database failed - try RTF file fallback (for path-based IDs)
                _logger.Info($"Database lookup failed for note {id.Value}, trying RTF file fallback...");
                return await LoadNoteFromFileSystemFallback(id);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get note by id: {id.Value}");
                
                // Final fallback - try file system
                return await LoadNoteFromFileSystemFallback(id);
            }
        }

        /// <summary>
        /// Fallback: Load note directly from RTF file when database fails
        /// First checks if we can find the note via FileSystemNoteRepository (which has FilePath set)
        /// </summary>
        private async Task<Note> LoadNoteFromFileSystemFallback(NoteId id)
        {
            try
            {
                _logger.Info($"Attempting RTF file fallback for note ID: {id.Value}");
                
                // Strategy 1: Try to treat ID as file path directly (legacy compatibility)
                var potentialFilePath = id.Value;
                if (System.IO.File.Exists(potentialFilePath))
                {
                    return await LoadNoteFromFile(potentialFilePath, id);
                }
                
                // Strategy 2: This shouldn't happen with our current setup, but fallback to search
                // (In the real world, we'd need to search the file system, but for now just log)
                _logger.Warning($"Could not locate file for note ID: {id.Value}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"RTF file fallback failed for: {id.Value}");
                return null;
            }
        }
        
        /// <summary>
        /// Load a note from a specific file path
        /// </summary>
        private async Task<Note> LoadNoteFromFile(string filePath, NoteId noteId)
        {
            try
            {
                if (!System.IO.File.Exists(filePath))
                {
                    _logger.Warning($"RTF file not found: {filePath}");
                    return null;
                }

                var fileInfo = new System.IO.FileInfo(filePath);
                var noteTitle = System.IO.Path.GetFileNameWithoutExtension(fileInfo.Name);
                var content = await System.IO.File.ReadAllTextAsync(filePath);
                
                // Create category ID from directory path
                var categoryPath = System.IO.Path.GetDirectoryName(filePath);
                var categoryId = NoteNest.Domain.Categories.CategoryId.From(categoryPath);
                
                // Create note domain object
                var note = new Note(categoryId, noteTitle, content);
                note.SetFilePath(filePath);

                _logger.Info($"📄 Note loaded from RTF file: {noteTitle} ({content.Length} chars) from {filePath}");
                
                // Cache the result
                var cacheKey = $"{NOTE_CACHE_PREFIX}{noteId.Value}";
                _cache.Set(cacheKey, note, CACHE_DURATION);
                
                return note;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to load note from file: {filePath}");
                return null;
            }
        }

        /// <summary>
        /// Get notes by category - LIGHTNING FAST for tree view expansion
        /// </summary>
        public async Task<IReadOnlyList<Note>> GetByCategoryAsync(CategoryId categoryId)
        {
            try
            {
                // Check cache first
                var cacheKey = $"{NOTES_CACHE_PREFIX}{categoryId.Value}";
                if (_cache.TryGetValue(cacheKey, out IReadOnlyList<Note> cached))
                {
                    _logger.Debug($"Notes for category {categoryId.Value} loaded from cache");
                    return cached;
                }

                var startTime = DateTime.Now;

                if (!Guid.TryParse(categoryId.Value, out var parentGuid))
                {
                    _logger.Warning($"Invalid CategoryId format: {categoryId.Value}");
                    return new List<Note>().AsReadOnly();
                }

                // Get child nodes that are notes
                var childNodes = await _treeRepository.GetChildrenAsync(parentGuid);
                var noteNodes = childNodes.Where(n => n.NodeType == TreeNodeType.Note).ToList();
                
                var notes = new List<Note>();
                foreach (var noteNode in noteNodes)
                {
                    var note = await ConvertTreeNodeToNote(noteNode);
                    if (note != null)
                    {
                        notes.Add(note);
                    }
                }

                // Cache the result
                _cache.Set(cacheKey, notes, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = CACHE_DURATION,
                    Priority = CacheItemPriority.Normal
                });

                var loadTime = (DateTime.Now - startTime).TotalMilliseconds;
                _logger.Info($"⚡ Loaded {notes.Count} notes for category in {loadTime}ms (cached)");

                return notes.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get notes for category: {categoryId.Value}");
                return new List<Note>().AsReadOnly();
            }
        }

        /// <summary>
        /// Get pinned notes - FAST database query
        /// </summary>
        public async Task<IReadOnlyList<Note>> GetPinnedAsync()
        {
            try
            {
                var pinnedNodes = await _treeRepository.GetPinnedNodesAsync();
                var noteNodes = pinnedNodes.Where(n => n.NodeType == TreeNodeType.Note).ToList();
                
                var notes = new List<Note>();
                foreach (var noteNode in noteNodes)
                {
                    var note = await ConvertTreeNodeToNote(noteNode);
                    if (note != null)
                    {
                        notes.Add(note);
                    }
                }

                _logger.Info($"Loaded {notes.Count} pinned notes from database");
                return notes.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get pinned notes");
                return new List<Note>().AsReadOnly();
            }
        }

        /// <summary>
        /// Create note - UPDATES database immediately
        /// </summary>
        public async Task<Result> CreateAsync(Note note)
        {
            try
            {
                var treeNode = await ConvertNoteToTreeNode(note);
                var success = await _treeRepository.InsertNodeAsync(treeNode);
                
                if (success)
                {
                    InvalidateCacheForCategory(note.CategoryId);
                    _logger.Info($"Created note in database: {note.Title}");
                    return Result.Ok();
                }
                
                return Result.Fail("Failed to create note in database");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create note: {note.Title}");
                return Result.Fail($"Failed to create note: {ex.Message}");
            }
        }

        /// <summary>
        /// Update note - UPDATES database immediately
        /// </summary>
        public async Task<Result> UpdateAsync(Note note)
        {
            try
            {
                var treeNode = await ConvertNoteToTreeNode(note);
                var success = await _treeRepository.UpdateNodeAsync(treeNode);
                
                if (success)
                {
                    InvalidateCacheForNote(note.Id);
                    InvalidateCacheForCategory(note.CategoryId);
                    _logger.Info($"Updated note in database: {note.Title}");
                    return Result.Ok();
                }
                
                return Result.Fail("Failed to update note in database");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to update note: {note.Title}");
                return Result.Fail($"Failed to update note: {ex.Message}");
            }
        }

        /// <summary>
        /// Delete note - SOFT DELETE in database
        /// </summary>
        public async Task<Result> DeleteAsync(NoteId id)
        {
            try
            {
                if (!Guid.TryParse(id.Value, out var guid))
                {
                    return Result.Fail($"Invalid NoteId format: {id.Value}");
                }

                // Get the note first to find its category (for cache invalidation)
                var treeNode = await _treeRepository.GetNodeByIdAsync(guid);
                if (treeNode == null || treeNode.NodeType != TreeNodeType.Note)
                {
                    return Result.Fail($"Note not found: {id.Value}");
                }

                var success = await _treeRepository.DeleteNodeAsync(guid, softDelete: true);
                
                if (success)
                {
                    InvalidateCacheForNote(id);
                    
                    // ✨ CRITICAL FIX: Also invalidate category cache so deleted notes disappear from UI
                    if (treeNode.ParentId.HasValue)
                    {
                        var categoryId = CategoryId.From(treeNode.ParentId.Value.ToString());
                        InvalidateCacheForCategory(categoryId);
                        _logger.Debug($"Invalidated cache for category: {categoryId.Value}");
                    }
                    
                    _logger.Info($"Deleted note from database: {id.Value}");
                    return Result.Ok();
                }
                
                return Result.Fail("Failed to delete note from database");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to delete note: {id.Value}");
                return Result.Fail($"Failed to delete note: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if note exists - FAST database lookup
        /// </summary>
        public async Task<bool> ExistsAsync(NoteId id)
        {
            try
            {
                if (!Guid.TryParse(id.Value, out var guid))
                {
                    return false;
                }

                var treeNode = await _treeRepository.GetNodeByIdAsync(guid);
                return treeNode?.NodeType == TreeNodeType.Note;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to check note existence: {id.Value}");
                return false;
            }
        }

        /// <summary>
        /// Check if title exists in category - OPTIMIZED database query
        /// </summary>
        public async Task<bool> TitleExistsInCategoryAsync(CategoryId categoryId, string title, NoteId excludeId = null)
        {
            try
            {
                var notesInCategory = await GetByCategoryAsync(categoryId);
                return notesInCategory.Any(n => 
                    string.Equals(n.Title, title, StringComparison.OrdinalIgnoreCase) &&
                    (excludeId == null || n.Id.Value != excludeId.Value));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to check title existence in category: {categoryId.Value}");
                return false;
            }
        }

        // =============================================================================
        // PRIVATE CONVERSION AND CACHE METHODS
        // =============================================================================

        private async Task<Note> ConvertTreeNodeToNote(TreeNode treeNode)
        {
            if (treeNode.NodeType != TreeNodeType.Note)
            {
                return null;
            }

            try
            {
                // Create Note using reflection for private constructor
                var note = (Note)Activator.CreateInstance(typeof(Note), true);
                
                SetPrivateProperty(note, "Id", NoteId.From(treeNode.Id.ToString()));
                SetPrivateProperty(note, "CategoryId", CategoryId.From(treeNode.ParentId?.ToString() ?? Guid.NewGuid().ToString()));
                SetPrivateProperty(note, "Title", treeNode.Name);
                SetPrivateProperty(note, "FilePath", treeNode.AbsolutePath);
                SetPrivateProperty(note, "IsPinned", treeNode.IsPinned);
                SetPrivateProperty(note, "Position", treeNode.SortOrder);
                SetPrivateProperty(note, "CreatedAt", treeNode.CreatedAt);
                SetPrivateProperty(note, "UpdatedAt", treeNode.ModifiedAt);

                // Load content from file if exists
                if (!string.IsNullOrEmpty(treeNode.AbsolutePath) && System.IO.File.Exists(treeNode.AbsolutePath))
                {
                    var content = await System.IO.File.ReadAllTextAsync(treeNode.AbsolutePath);
                    SetPrivateProperty(note, "Content", content);
                }
                else
                {
                    SetPrivateProperty(note, "Content", "");
                }

                return note;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to convert TreeNode to Note: {treeNode.Name}");
                return null;
            }
        }

        private async Task<TreeNode> ConvertNoteToTreeNode(Note note)
        {
            var noteGuid = Guid.TryParse(note.Id.Value, out var id) ? id : Guid.NewGuid();
            var parentId = Guid.TryParse(note.CategoryId.Value, out var catGuid) ? (Guid?)catGuid : null;

            return TreeNode.CreateFromDatabase(
                id: noteGuid,
                parentId: parentId,
                canonicalPath: note.FilePath?.ToLowerInvariant() ?? "",
                displayPath: note.FilePath ?? "",
                absolutePath: note.FilePath ?? "",
                nodeType: TreeNodeType.Note,
                name: note.Title,
                fileExtension: System.IO.Path.GetExtension(note.FilePath),
                fileSize: null,
                createdAt: note.CreatedAt,
                modifiedAt: note.UpdatedAt,
                isPinned: note.IsPinned,
                sortOrder: note.Position
            );
        }

        private void InvalidateCacheForNote(NoteId noteId)
        {
            var cacheKey = $"{NOTE_CACHE_PREFIX}{noteId.Value}";
            _cache.Remove(cacheKey);
        }

        private void InvalidateCacheForCategory(CategoryId categoryId)
        {
            var cacheKey = $"{NOTES_CACHE_PREFIX}{categoryId.Value}";
            _cache.Remove(cacheKey);
        }

        private void SetPrivateProperty(object obj, string propertyName, object value)
        {
            var property = obj.GetType().GetProperty(propertyName, 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Public | 
                System.Reflection.BindingFlags.Instance);
            
            property?.SetValue(obj, value);
        }
    }
}
