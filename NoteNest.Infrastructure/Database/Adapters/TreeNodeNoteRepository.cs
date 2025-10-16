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

namespace NoteNest.Infrastructure.Database.Adapters
{
    /// <summary>
    /// Adapter that bridges the legacy Note domain model with the new TreeNode database.
    /// Provides INoteRepository interface while using TreeDatabaseRepository underneath.
    /// </summary>
    public class TreeNodeNoteRepository : INoteRepository
    {
        private readonly ITreeDatabaseRepository _treeRepository;
        private readonly IAppLogger _logger;

        public TreeNodeNoteRepository(ITreeDatabaseRepository treeRepository, IAppLogger logger)
        {
            _treeRepository = treeRepository ?? throw new ArgumentNullException(nameof(treeRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Note> GetByIdAsync(NoteId id)
        {
            try
            {
                if (!Guid.TryParse(id.Value, out var guid))
                {
                    _logger.Warning($"Invalid NoteId format: {id.Value}");
                    return null;
                }

                var treeNode = await _treeRepository.GetNodeByIdAsync(guid);
                if (treeNode == null || treeNode.NodeType != TreeNodeType.Note)
                {
                    return null;
                }

                return await ConvertTreeNodeToNote(treeNode);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get note by id: {id.Value}");
                return null;
            }
        }

        public async Task<IReadOnlyList<Note>> GetByCategoryAsync(CategoryId categoryId)
        {
            try
            {
                if (!Guid.TryParse(categoryId.Value, out var parentGuid))
                {
                    _logger.Warning($"Invalid CategoryId format: {categoryId.Value}");
                    return new List<Note>().AsReadOnly();
                }

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

                return notes.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get notes by category: {categoryId.Value}");
                return new List<Note>().AsReadOnly();
            }
        }

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

                return notes.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get pinned notes");
                return new List<Note>().AsReadOnly();
            }
        }

        public async Task<Result> CreateAsync(Note note)
        {
            try
            {
                var treeNode = await ConvertNoteToTreeNode(note);
                var success = await _treeRepository.InsertNodeAsync(treeNode);
                
                return success 
                    ? Result.Ok()
                    : Result.Fail("Failed to create note in database");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create note: {note.Title}");
                return Result.Fail($"Failed to create note: {ex.Message}");
            }
        }

        public async Task<Result> UpdateAsync(Note note)
        {
            try
            {
                var treeNode = await ConvertNoteToTreeNode(note);
                var success = await _treeRepository.UpdateNodeAsync(treeNode);
                
                return success 
                    ? Result.Ok()
                    : Result.Fail("Failed to update note in database");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to update note: {note.Title}");
                return Result.Fail($"Failed to update note: {ex.Message}");
            }
        }

        public async Task<Result> DeleteAsync(NoteId id)
        {
            try
            {
                if (!Guid.TryParse(id.Value, out var guid))
                {
                    return Result.Fail($"Invalid NoteId format: {id.Value}");
                }

                var success = await _treeRepository.DeleteNodeAsync(guid, softDelete: true);
                
                return success 
                    ? Result.Ok()
                    : Result.Fail("Failed to delete note from database");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to delete note: {id.Value}");
                return Result.Fail($"Failed to delete note: {ex.Message}");
            }
        }

        public async Task<bool> ExistsAsync(NoteId id)
        {
            try
            {
                if (!Guid.TryParse(id.Value, out var guid))
                {
                    return false;
                }

                var treeNode = await _treeRepository.GetNodeByIdAsync(guid);
                return treeNode != null && treeNode.NodeType == TreeNodeType.Note;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to check note existence: {id.Value}");
                return false;
            }
        }

        public async Task<bool> TitleExistsInCategoryAsync(CategoryId categoryId, string title, NoteId excludeId = null)
        {
            try
            {
                var notesInCategory = await GetByCategoryAsync(categoryId);
                return notesInCategory.Any(n => 
                    string.Equals(n.Title, title, StringComparison.OrdinalIgnoreCase) &&
                    (excludeId == null || n.NoteId.Value != excludeId.Value));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to check title existence in category: {categoryId.Value}");
                return false;
            }
        }

        // =============================================================================
        // PRIVATE CONVERSION METHODS
        // =============================================================================

        private async Task<Note> ConvertTreeNodeToNote(TreeNode treeNode)
        {
            if (treeNode.NodeType != TreeNodeType.Note)
            {
                return null;
            }

            try
            {
                // Create a Note using the private constructor via reflection
                // This is needed because Note has domain validation in its public constructor
                var note = (Note)Activator.CreateInstance(typeof(Note), true);
                
                // Set properties using reflection (since they have private setters)
                SetPrivateProperty(note, "Id", NoteId.From(treeNode.Id.ToString()));
                SetPrivateProperty(note, "CategoryId", CategoryId.From(treeNode.ParentId?.ToString() ?? Guid.NewGuid().ToString()));
                SetPrivateProperty(note, "Title", treeNode.Name);
                SetPrivateProperty(note, "FilePath", treeNode.AbsolutePath);
                SetPrivateProperty(note, "IsPinned", treeNode.IsPinned);
                SetPrivateProperty(note, "Position", treeNode.SortOrder);
                SetPrivateProperty(note, "CreatedAt", treeNode.CreatedAt);
                SetPrivateProperty(note, "UpdatedAt", treeNode.ModifiedAt);

                // Load content from file if path exists
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
            // If the note already exists as a TreeNode, get it first to preserve metadata
            if (Guid.TryParse(note.NoteId.Value, out var existingGuid))
            {
                var existingNode = await _treeRepository.GetNodeByIdAsync(existingGuid);
                if (existingNode != null)
                {
                    // Update existing TreeNode with note data
                    var updatedNode = TreeNode.CreateFromDatabase(
                        id: existingNode.Id,
                        parentId: Guid.TryParse(note.CategoryId.Value, out var parentGuid) ? parentGuid : existingNode.ParentId,
                        canonicalPath: existingNode.CanonicalPath,
                        displayPath: existingNode.DisplayPath,
                        absolutePath: note.FilePath ?? existingNode.AbsolutePath,
                        nodeType: TreeNodeType.Note,
                        name: note.Title,
                        fileExtension: existingNode.FileExtension,
                        fileSize: existingNode.FileSize,
                        createdAt: existingNode.CreatedAt,
                        modifiedAt: note.UpdatedAt,
                        accessedAt: existingNode.AccessedAt,
                        quickHash: existingNode.QuickHash,
                        fullHash: existingNode.FullHash,
                        hashAlgorithm: existingNode.HashAlgorithm,
                        hashCalculatedAt: existingNode.HashCalculatedAt,
                        isExpanded: existingNode.IsExpanded,
                        isPinned: note.IsPinned,
                        isSelected: existingNode.IsSelected,
                        sortOrder: note.Position,
                        colorTag: existingNode.ColorTag,
                        iconOverride: existingNode.IconOverride,
                        isDeleted: existingNode.IsDeleted,
                        deletedAt: existingNode.DeletedAt,
                        metadataVersion: existingNode.MetadataVersion,
                        customProperties: existingNode.CustomProperties
                    );
                    return updatedNode;
                }
            }

            // Create new TreeNode
            var newGuid = Guid.TryParse(note.NoteId.Value, out var noteGuid) ? noteGuid : Guid.NewGuid();
            var parentId = Guid.TryParse(note.CategoryId.Value, out var catGuid) ? (Guid?)catGuid : null;

            var newNode = TreeNode.CreateFromDatabase(
                id: newGuid,
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

            return newNode;
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
