using System;
using MediatR;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.MarkOrphaned
{
    /// <summary>
    /// Command to mark a todo as orphaned (source note deleted or todo removed from note).
    /// Used by RTF sync service.
    /// </summary>
    public class MarkOrphanedCommand : IRequest<Result<MarkOrphanedResult>>
    {
        public Guid TodoId { get; set; }
        public bool IsOrphaned { get; set; }
    }

    public class MarkOrphanedResult
    {
        public Guid TodoId { get; set; }
        public bool IsOrphaned { get; set; }
        public bool Success { get; set; }
    }
}

