using System;
using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.AddTag
{
    /// <summary>
    /// Command to add a manual tag to a todo.
    /// Auto-tags cannot be added manually (they're generated from folder path).
    /// </summary>
    public class AddTagCommand : IRequest<Result<AddTagResult>>
    {
        public Guid TodoId { get; set; }
        public string TagName { get; set; } = string.Empty;
    }

    public class AddTagResult
    {
        public Guid TodoId { get; set; }
        public string TagName { get; set; } = string.Empty;
        public bool Success { get; set; }
    }
}

