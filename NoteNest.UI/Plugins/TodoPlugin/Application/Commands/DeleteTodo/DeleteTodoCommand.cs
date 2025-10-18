using System;
using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.DeleteTodo
{
    public class DeleteTodoCommand : IRequest<Result<DeleteTodoResult>>
    {
        public Guid TodoId { get; set; }
    }

    public class DeleteTodoResult
    {
        public Guid TodoId { get; set; }
        public bool Success { get; set; }
    }
}

