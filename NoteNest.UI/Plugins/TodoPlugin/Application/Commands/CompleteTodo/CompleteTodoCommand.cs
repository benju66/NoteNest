using System;
using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.CompleteTodo
{
    /// <summary>
    /// Command to toggle todo completion status.
    /// </summary>
    public class CompleteTodoCommand : IRequest<Result<CompleteTodoResult>>
    {
        public Guid TodoId { get; set; }
        public bool IsCompleted { get; set; }
    }

    public class CompleteTodoResult
    {
        public Guid TodoId { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime? CompletedDate { get; set; }
        public bool Success { get; set; }
    }
}

