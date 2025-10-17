using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Application.Queries;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Categories;
using NoteNest.Domain.Common;
using NoteNest.Domain.Notes;
using NoteNest.Domain.Trees;

namespace NoteNest.Infrastructure.Queries
{
    /// <summary>
    /// Read-only repository for Notes using ITreeQueryService projection.
    /// Provides Note aggregate data from the tree_view projection.
    /// </summary>
    public class NoteQueryRepository : INoteRepository
    {
        private readonly ITreeQueryService _treeQueryService;
        private readonly IAppLogger _logger;
        private readonly string _notesRootPath;

        public NoteQueryRepository(ITreeQueryService treeQueryService, string notesRootPath, IAppLogger logger)
        {
            _treeQueryService = treeQueryService ?? throw new ArgumentNullException(nameof(treeQueryService));
            _notesRootPath = notesRootPath ?? throw new ArgumentNullException(nameof(notesRootPath));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Note> GetByIdAsync(NoteId id)
        {
            try
            {
                var node = await _treeQueryService.GetByIdAsync(Guid.Parse(id.Value));
                return node != null && node.NodeType == TreeNodeType.Note 
                    ? ConvertTreeNodeToNote(node) 
                    : null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get note by ID: {id}");
                return null;
            }
        }

        public async Task<IReadOnlyList<Note>> GetByCategoryAsync(CategoryId categoryId)
        {
            try
            {
                var categoryGuid = Guid.Parse(categoryId.Value);
                var children = await _treeQueryService.GetChildrenAsync(categoryGuid);
                
                var notes = children
                    .Where(n => n.NodeType == TreeNodeType.Note)
                    .Select(ConvertTreeNodeToNote)
                    .Where(n => n != null)
                    .ToList();

                _logger.Debug($"Loaded {notes.Count} notes for category {categoryId}");
                return notes;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get notes for category: {categoryId}");
                return new List<Note>();
            }
        }

        public async Task<IReadOnlyList<Note>> GetPinnedAsync()
        {
            try
            {
                // Get all nodes and filter for pinned notes
                var allNodes = await _treeQueryService.GetAllNodesAsync();
                
                var notes = allNodes
                    .Where(n => n.NodeType == TreeNodeType.Note && n.IsPinned)
                    .Select(ConvertTreeNodeToNote)
                    .Where(n => n != null)
                    .ToList();

                return notes;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get pinned notes");
                return new List<Note>();
            }
        }

        // Write operations are not supported in read-only query repository
        public Task<Result> CreateAsync(Note note)
        {
            throw new NotSupportedException("Create operations not supported in query repository. Use command handlers instead.");
        }

        public Task<Result> UpdateAsync(Note note)
        {
            throw new NotSupportedException("Update operations not supported in query repository. Use command handlers instead.");
        }

        public Task<Result> DeleteAsync(NoteId id)
        {
            throw new NotSupportedException("Delete operations not supported in query repository. Use command handlers instead.");
        }

        public async Task<bool> ExistsAsync(NoteId id)
        {
            var note = await GetByIdAsync(id);
            return note != null;
        }

        public async Task<bool> TitleExistsInCategoryAsync(CategoryId categoryId, string title, NoteId excludeId = null)
        {
            var notes = await GetByCategoryAsync(categoryId);
            return notes.Any(n => n.Title.Equals(title, StringComparison.OrdinalIgnoreCase) 
                                  && (excludeId == null || !n.NoteId.Equals(excludeId)));
        }

        private Note ConvertTreeNodeToNote(TreeNode treeNode)
        {
            try
            {
                if (treeNode.NodeType != TreeNodeType.Note)
                    return null;

                var noteId = NoteId.From(treeNode.Id.ToString());
                var categoryId = treeNode.ParentId.HasValue 
                    ? CategoryId.From(treeNode.ParentId.Value.ToString())
                    : CategoryId.Create();

                // Construct absolute file path from display path + .rtf extension
                // DisplayPath is like "Notes/Category/NoteTitle"
                // We need absolute: "C:/Users/.../MyNotes/Notes/Category/NoteTitle.rtf"
                var filePath = string.Empty;
                if (!string.IsNullOrEmpty(treeNode.DisplayPath))
                {
                    // DisplayPath includes full relative path from Notes root
                    // Add .rtf extension and combine with notes root path
                    var relativePath = treeNode.DisplayPath + ".rtf";
                    filePath = System.IO.Path.Combine(_notesRootPath, relativePath);
                    
                    // Normalize path separators for the current OS
                    filePath = System.IO.Path.GetFullPath(filePath);
                }

                // Create Note with ID from projection (preserves aggregate identity)
                var note = new Note(noteId, categoryId, treeNode.Name, filePath, string.Empty);
                
                return note;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to convert TreeNode to Note: {treeNode.Name}");
                return null;
            }
        }
    }
}

