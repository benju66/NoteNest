using System;
using MediatR;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.SetDueDate
{
    public class SetDueDateCommand : IRequest<Result<SetDueDateResult>>
    {
        public Guid TodoId { get; set; }
        public DateTime? DueDate { get; set; }
    }

    public class SetDueDateResult
    {
        public Guid TodoId { get; set; }
        public DateTime? DueDate { get; set; }
        public bool Success { get; set; }
    }
}

