using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Categories.Commands.DeleteCategory
{
    /// <summary>
    /// Command to delete a category and all its contents.
    /// Uses soft delete in database (is_deleted = 1) for recovery capability.
    /// Physically deletes the directory and all files.
    /// </summary>
    public class DeleteCategoryCommand : IRequest<Result<DeleteCategoryResult>>
    {
        public string CategoryId { get; set; }
        
        /// <summary>
        /// If true, physically delete directory. If false, only soft-delete in database.
        /// </summary>
        public bool DeleteFiles { get; set; } = true;
    }

    public class DeleteCategoryResult
    {
        public string DeletedCategoryId { get; set; }
        public string DeletedCategoryName { get; set; }
        public int DeletedDescendantCount { get; set; }
    }
}

