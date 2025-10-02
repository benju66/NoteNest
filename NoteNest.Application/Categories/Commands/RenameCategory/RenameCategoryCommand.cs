using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Categories.Commands.RenameCategory
{
    /// <summary>
    /// Command to rename a category.
    /// Updates category name, path, and ALL descendant paths in database.
    /// Renames physical directory.
    /// Critical for drag & drop foundation (MoveCategoryCommand will reuse path update logic).
    /// </summary>
    public class RenameCategoryCommand : IRequest<Result<RenameCategoryResult>>
    {
        public string CategoryId { get; set; }
        public string NewName { get; set; }
    }

    public class RenameCategoryResult
    {
        public bool Success { get; set; }
        public string CategoryId { get; set; }
        public string OldName { get; set; }
        public string NewName { get; set; }
        public string OldPath { get; set; }
        public string NewPath { get; set; }
        public int UpdatedDescendantCount { get; set; }
    }
}

