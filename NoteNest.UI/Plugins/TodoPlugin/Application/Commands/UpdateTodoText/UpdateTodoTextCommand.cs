using System;
using MediatR;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.UpdateTodoText
{
    public class UpdateTodoTextCommand : IRequest<Result<UpdateTodoTextResult>>
    {
        public Guid TodoId { get; set; }
        public string NewText { get; set; }
    }

    public class UpdateTodoTextResult
    {
        public Guid TodoId { get; set; }
        public string NewText { get; set; }
        public bool Success { get; set; }
    }
}

