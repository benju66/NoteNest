using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Categories.Commands.CreateCategory
{
    /// <summary>
    /// Command to create a new category (folder) in the file system and database.
    /// Follows CQRS pattern, mirrors CreateNoteCommand structure.
    /// </summary>
    public class CreateCategoryCommand : IRequest<Result<CreateCategoryResult>>
    {
        /// <summary>
        /// Parent category ID (null for root categories)
        /// </summary>
        public string ParentCategoryId { get; set; }
        
        /// <summary>
        /// Name of the new category
        /// </summary>
        public string Name { get; set; }
    }

    public class CreateCategoryResult
    {
        public string CategoryId { get; set; }
        public string CategoryPath { get; set; }
        public string Name { get; set; }
    }
}

