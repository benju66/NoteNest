using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoteNest.Application.Tags.Services
{
    /// <summary>
    /// Service for propagating category folder tags to child items (notes, todos).
    /// Used by both UI layer (TodoPlugin) and Infrastructure layer (background processing).
    /// </summary>
    public interface ITagPropagationService
    {
        /// <summary>
        /// Bulk update all todos in a folder with new tags.
        /// Removes old auto-tags, adds new tags, preserves manual tags.
        /// </summary>
        Task BulkUpdateFolderTodosAsync(Guid folderId, List<string> newTags);
    }
}

