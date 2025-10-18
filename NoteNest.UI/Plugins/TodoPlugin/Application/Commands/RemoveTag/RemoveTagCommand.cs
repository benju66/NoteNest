using System;
using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.RemoveTag
{
    /// <summary>
    /// Command to remove a manual tag from a todo.
    /// Auto-tags cannot be removed manually (they're managed by the system).
    /// </summary>
    public class RemoveTagCommand : IRequest<Result<RemoveTagResult>>
    {
        public Guid TodoId { get; set; }
        public string TagName { get; set; } = string.Empty;
    }

    public class RemoveTagResult
    {
        public Guid TodoId { get; set; }
        public string TagName { get; set; } = string.Empty;
        public bool Success { get; set; }
    }
}

