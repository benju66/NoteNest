using System;
using MediatR;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.SetPriority
{
    public class SetPriorityCommand : IRequest<Result<SetPriorityResult>>
    {
        public Guid TodoId { get; set; }
        public Priority Priority { get; set; }
    }

    public class SetPriorityResult
    {
        public Guid TodoId { get; set; }
        public Priority Priority { get; set; }
        public bool Success { get; set; }
    }
}

