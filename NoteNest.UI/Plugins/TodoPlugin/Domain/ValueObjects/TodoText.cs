using System.Collections.Generic;
using NoteNest.UI.Plugins.TodoPlugin.Domain.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.Domain.ValueObjects
{
    public class TodoText : ValueObject
    {
        public string Value { get; }

        private TodoText(string value)
        {
            Value = value;
        }

        public static Result<TodoText> Create(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Result.Fail<TodoText>("Todo text cannot be empty");

            if (text.Length > 1000)
                return Result.Fail<TodoText>("Todo text cannot exceed 1000 characters");

            return Result.Ok(new TodoText(text.Trim()));
        }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }

        public override string ToString() => Value;
    }
}

