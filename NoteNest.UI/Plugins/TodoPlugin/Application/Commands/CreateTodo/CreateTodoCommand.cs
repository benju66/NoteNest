using System;
using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.Application.Commands.CreateTodo
{
    /// <summary>
    /// Command to create a new todo item.
    /// Supports both manual creation (quick add) and RTF extraction (bracket todos).
    /// </summary>
    public class CreateTodoCommand : IRequest<Result<CreateTodoResult>>
    {
        /// <summary>
        /// Text content of the todo
        /// </summary>
        public string Text { get; set; }
        
        /// <summary>
        /// Optional category ID (null = uncategorized)
        /// </summary>
        public Guid? CategoryId { get; set; }
        
        /// <summary>
        /// Optional source note ID (for RTF-extracted todos)
        /// </summary>
        public Guid? SourceNoteId { get; set; }
        
        /// <summary>
        /// Optional source file path (for RTF-extracted todos)
        /// </summary>
        public string SourceFilePath { get; set; }
        
        /// <summary>
        /// Optional line number in source note
        /// </summary>
        public int? SourceLineNumber { get; set; }
        
        /// <summary>
        /// Optional character offset in source note
        /// </summary>
        public int? SourceCharOffset { get; set; }
    }

    public class CreateTodoResult
    {
        public Guid TodoId { get; set; }
        public string Text { get; set; }
        public Guid? CategoryId { get; set; }
        public bool Success { get; set; }
    }
}

