using System;
using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.ToggleFavorite
{
    public class ToggleFavoriteCommand : IRequest<Result<ToggleFavoriteResult>>
    {
        public Guid TodoId { get; set; }
        public bool IsFavorite { get; set; }
    }

    public class ToggleFavoriteResult
    {
        public Guid TodoId { get; set; }
        public bool IsFavorite { get; set; }
        public bool Success { get; set; }
    }
}

