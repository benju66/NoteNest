using System;
using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.MoveTodoCategory
{
    /// <summary>
    /// Command to move a todo to a different category.
    /// Used for drag & drop and category cleanup operations.
    /// </summary>
    public class MoveTodoCategoryCommand : IRequest<Result<MoveTodoCategoryResult>>
    {
        public Guid TodoId { get; set; }
        public Guid? TargetCategoryId { get; set; }  // null = move to uncategorized
    }

    public class MoveTodoCategoryResult
    {
        public Guid TodoId { get; set; }
        public Guid? OldCategoryId { get; set; }
        public Guid? NewCategoryId { get; set; }
        public bool Success { get; set; }
    }
}

